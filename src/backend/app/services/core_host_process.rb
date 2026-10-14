require "open3"

# 再計算ホストの子プロセスを起動し、入力を渡して、終了コードと出力を受け取る。
# 引数は配列で渡し、シェルを介さない。環境変数は渡された分だけにする。
# 制限時間を過ぎたら、プロセスグループごと止めて TimedOut にする。出力が上限を超えたら、読むのをやめて止めて Failure にする。
class CoreHostProcess
  Result = Data.define(:status, :stdout, :stderr)

  # 起動できない・出力が上限を超えた（内部エラー）。
  class Failure < StandardError; end

  # 制限時間を超えた（利用できない）。
  class TimedOut < Failure; end

  # 後始末の待ち時間（秒）。ここで読み取りが終わらなければ、打ち切る。
  DRAIN_SECONDS = 1

  class << self
    def run(argv:, env:, input:, timeout:, max_output_bytes:, max_stderr_bytes:)
      Open3.popen3(env, *argv, unsetenv_others: true, pgroup: true) do |stdin, stdout, stderr, wait_thread|
        pid = wait_thread.pid
        writer = Thread.new { feed(stdin, input) }
        out_reader = Thread.new { capture(stdout, max_output_bytes, pid) }
        err_reader = Thread.new { capture(stderr, max_stderr_bytes, pid) }

        unless wait_thread.join(timeout)
          kill_group(pid)
          drain(writer, out_reader, err_reader)
          raise TimedOut, InternalMessages.text(:core_host, :process_time_limit)
        end

        drain(writer, out_reader, err_reader)
        stdout_data = out_reader.value
        stderr_data = err_reader.value
        raise Failure, InternalMessages.text(:core_host, :process_output_limit) if stdout_data.bytesize > max_output_bytes || stderr_data.bytesize > max_stderr_bytes

        Result.new(status: wait_thread.value.exitstatus, stdout: stdout_data, stderr: stderr_data)
      end
    rescue SystemCallError => error
      # 実行ファイルが無い・実行できない（詳細は呼び出し側に渡さない）
      raise Failure, error.class.name
    end

    private

    # 入力を書き、標準入力を閉じる。子プロセスが先に終わっていても、結果は終了コードと出力で判断する。
    def feed(io, input)
      io.binmode
      io.write(input.b)
    rescue Errno::EPIPE
      nil
    ensure
      io.close unless io.closed?
    end

    # 出力を上限まで読む。上限を超えたら、読むのをやめてプロセスを止める（超過の判定は呼び出し側）。
    def capture(io, limit, pid)
      io.binmode
      data = io.read(limit + 1) || "".b
      kill_group(pid) if data.bytesize > limit
      data
    end

    def kill_group(pid)
      Process.kill("KILL", -pid)
    rescue Errno::ESRCH
      # すでに終わっている
      nil
    end

    def drain(*threads)
      threads.each { |thread| thread.join(DRAIN_SECONDS) }
    end
  end
end
