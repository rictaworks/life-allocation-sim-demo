# 再計算ホストの出力の見本（実物の出力をそのまま写した値）と、テスト用の起動処理の差し替え。
module CoreHostFixtures
  # 1 年目の状態（難易度 normal）。実物の init の出力と同じ。
  def initial_state
    {
      "age" => 22, "income_yen" => 2_800_000, "savings_yen" => 0, "investment_yen" => 0,
      "satisfaction" => 60, "price_index_bp" => 10_000, "status" => "playing",
      "struggling" => false, "raise_total_yen" => 0, "occurred_once_events" => []
    }
  end

  def init_output
    { "difficulty" => "normal", "state" => initial_state }
  end

  # seed 12345・難易度 normal・配分 60/20/20 の 1 年分（実物の replay の出力と同じ）。
  def one_year_replay_output
    {
      "turns" => [
        {
          "turn_no" => 1, "age" => 22, "living_pct" => 60, "saving_pct" => 20, "invest_pct" => 20,
          "event_code" => nil, "savings_after" => 240_560, "invest_after" => 600_040, "satisfaction_after" => 57
        }
      ],
      "state" => {
        "age" => 23, "income_yen" => 2_800_000, "savings_yen" => 240_560, "investment_yen" => 600_040,
        "satisfaction" => 57, "price_index_bp" => 10_100, "status" => "playing",
        "struggling" => false, "raise_total_yen" => 0, "occurred_once_events" => []
      },
      "evaluation" => nil
    }
  end

  # 完走や破綻のあとの評価（形の確認用の見本）。
  def evaluation_output
    {
      "total_assets_yen" => 0, "average_satisfaction" => 60, "is_bankrupt" => false,
      "struggle_years" => 0, "investment_sold_years" => 0, "rank" => "D",
      "comments" => { "retirement" => "見本", "quality" => "見本", "preparedness" => "見本" }
    }
  end

  def host_result(status:, stdout: "", stderr: "")
    CoreHostProcess::Result.new(status: status, stdout: stdout, stderr: stderr)
  end

  def host_success(payload)
    host_result(status: 0, stdout: JSON.generate(payload))
  end

  def host_rule_violation(code, turn_no: nil)
    error = { "code" => code, "message" => "ホストの文言（見本）" }
    error["turn_no"] = turn_no if turn_no
    host_result(status: 2, stdout: JSON.generate("error" => error))
  end
end

# 起動処理の差し替え。呼ばれた引数を記録し、設定した結果（または例外）を返す。
class FakeCoreHostLauncher
  attr_reader :calls

  def initialize
    @calls = []
    @outcome = nil
  end

  def respond_with(result)
    @outcome = result
  end

  def fail_with(error)
    @outcome = error
  end

  def run(**arguments)
    @calls << arguments
    raise @outcome if @outcome.is_a?(Exception)

    @outcome
  end
end

# 起動処理の差し替え（同時実行の検査用）。open! が呼ばれるまで、実行中の呼び出しは戻らない。
class BlockingCoreHostLauncher
  DEADLINE_SECONDS = 5

  def initialize(stdout)
    @stdout = stdout
    @mutex = Mutex.new
    @changed = ConditionVariable.new
    @started = 0
    @open = false
  end

  def run(**_arguments)
    @mutex.synchronize do
      @started += 1
      @changed.broadcast
      @changed.wait(@mutex) until @open
    end
    CoreHostProcess::Result.new(status: 0, stdout: @stdout, stderr: "")
  end

  def started_count
    @mutex.synchronize { @started }
  end

  # 起動した件数が count になるまで待つ（テストが止まらないよう期限を付ける）。
  def wait_for_started(count)
    deadline = Process.clock_gettime(Process::CLOCK_MONOTONIC) + DEADLINE_SECONDS
    @mutex.synchronize do
      while @started < count
        raise "起動の呼び出しが届きません" if Process.clock_gettime(Process::CLOCK_MONOTONIC) > deadline

        @changed.wait(@mutex, 0.05)
      end
    end
  end

  def open!
    @mutex.synchronize do
      @open = true
      @changed.broadcast
    end
  end
end
