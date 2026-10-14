require "concurrent"
require "json"
require "pathname"

# 再計算ホスト（CoreHost）を呼ぶ。担当は次だけ：DLL の確認・入力の上限・起動（引数は配列）・制限時間・同時実行・
# 終了コードの対応・出力の形の検査。規則は持たない（規則違反は、ホストの理由コードを ApiError にするだけ）。
class CoreHostClient
  # 同時実行の枠。読み込み時に 1 つだけ作り、すべての呼び出しで共有する（遅延の初期化は競合するため使わない）。
  SHARED_GATE = Concurrent::Semaphore.new(CoreHostSettings.max_concurrent)

  class << self
    def init(difficulty:)
      new.init(difficulty: difficulty)
    end

    def replay(seed:, difficulty:, history:)
      new.replay(seed: seed, difficulty: difficulty, history: history)
    end

    def gate
      SHARED_GATE
    end
  end

  # launcher は CoreHostProcess と同じ run を持つもの（テストでは差し替える）。
  def initialize(launcher: CoreHostProcess, gate: self.class.gate, wait_seconds: CoreHostSettings.wait_seconds)
    @launcher = launcher
    @gate = gate
    @wait_seconds = wait_seconds
  end

  def init(difficulty:)
    call(:init, { difficulty: difficulty }, :init)
  end

  def replay(seed:, difficulty:, history:)
    call(:replay, { seed: seed, difficulty: difficulty, history: history }, :replay)
  end

  private

  def call(command, payload, shape)
    body = JSON.generate(payload)
    enforce_input_limit!(body)
    argv = [ CoreHostSettings.executable, dll_path, CoreHostSettings.command(command) ]

    interpret(run_within_gate(argv, body), command, shape)
  end

  # 終了コード：0 は成功（形を検査して返す）、2 は理由コード付きの規則違反、ほかは内部エラー。
  # 内部エラーのログは、呼んだ種類と終了コードだけ（標準エラーの内容は記録しない）。
  def interpret(result, command, shape)
    case result.status
    when CoreHostSettings.exit_code(:success)
      checked(parse(result.stdout), shape)
    when CoreHostSettings.exit_code(:rule_violation)
      raise_rule_violation(checked(parse(result.stdout), :error))
    else
      log_failure("#{command} exit status #{result.status.inspect}")
      raise ApiError.new(:internal_error)
    end
  end

  def raise_rule_violation(response)
    code = response.fetch("error").fetch("code")
    unless CoreHostSettings.rule_violation_codes.include?(code)
      log_failure("rule code is not mapped: #{code.truncate(40)}")
      raise ApiError.new(:internal_error)
    end

    raise ApiError.new(code.to_sym)
  end

  def checked(value, shape)
    CoreHostShape.check!(value, shape)
    value
  rescue CoreHostShape::Mismatch => error
    log_failure("unexpected output shape at #{error.message}")
    raise ApiError.new(:internal_error)
  end

  def parse(stdout)
    JSON.parse(stdout.to_s.dup.force_encoding(Encoding::UTF_8))
  rescue JSON::ParserError
    log_failure("output is not JSON")
    raise ApiError.new(:internal_error)
  end

  # 同時実行の枠を待つ（待ちは上限まで）。枠を得てから起動し、終わったら必ず枠を返す。
  def run_within_gate(argv, body)
    unless @gate.try_acquire(1, @wait_seconds)
      log_failure("no slot within the wait limit")
      raise ApiError.new(:service_unavailable)
    end

    begin
      @launcher.run(
        argv: argv,
        env: child_environment,
        input: body,
        timeout: CoreHostSettings.timeout_seconds,
        max_output_bytes: CoreHostSettings.max_output_bytes,
        max_stderr_bytes: CoreHostSettings.max_stderr_bytes
      )
    rescue CoreHostProcess::TimedOut
      log_failure("timed out")
      raise ApiError.new(:service_unavailable)
    rescue CoreHostProcess::Failure
      log_failure("process failed")
      raise ApiError.new(:internal_error)
    ensure
      @gate.release
    end
  end

  # 入力が上限を超えたら、起動せずに拒否する。
  def enforce_input_limit!(body)
    return if body.bytesize <= CoreHostSettings.max_input_bytes

    log_failure("input exceeds the limit")
    raise ApiError.new(:internal_error)
  end

  # DLL は絶対パスで、存在するファイルだけを使う。
  def dll_path
    value = ENV[CoreHostSettings.dll_env_key]
    return value if value.is_a?(String) && Pathname.new(value).absolute? && File.file?(value)

    log_failure("dll is not available")
    raise ApiError.new(:internal_error)
  end

  # 子プロセスに渡す環境変数は、許可したものだけ。
  def child_environment
    inherited = CoreHostSettings.child_env_keys
                                .select { |key| ENV.key?(key) }
                                .to_h { |key| [ key, ENV.fetch(key) ] }
    inherited.merge(CoreHostSettings.child_env)
  end

  # ログには、入力の JSON・出力の値・標準エラーの内容・Cookie・セッション ID を出さない（操作の名前と結果だけ）。
  def log_failure(message)
    Rails.logger.error("[core_host] #{message}")
  end
end
