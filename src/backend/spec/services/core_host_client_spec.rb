require "rails_helper"

# 起動処理を差し替えて、CoreHostClient の担当（引数・入力の上限・終了コード・形の検査・制限時間・同時実行）を検査する。
RSpec.describe CoreHostClient do
  include CoreHostFixtures

  # DLL は存在するファイルであればよい（起動しないので中身は使わない）。
  let(:dll_path) { File.expand_path(__FILE__) }
  let(:launcher) { FakeCoreHostLauncher.new }
  let(:gate) { Concurrent::Semaphore.new(2) }
  let(:client) { described_class.new(launcher: launcher, gate: gate, wait_seconds: 0.1) }

  around do |example|
    with_env("CORE_HOST_DLL" => dll_path) { example.run }
  end

  describe "起動の仕方" do
    it "dotnet・DLL（絶対パス）・操作名を配列で渡す（シェルを介さない）" do
      launcher.respond_with(host_success(init_output))

      client.init(difficulty: "normal")

      expect(launcher.calls.sole[:argv]).to eq([ "dotnet", dll_path, "init" ])
    end

    it "入力は JSON で渡し、制限時間と出力・標準エラーの上限を指定する" do
      launcher.respond_with(host_success(init_output))

      client.init(difficulty: "normal")

      call = launcher.calls.sole
      expect(call[:input]).to eq('{"difficulty":"normal"}')
      expect(call).to include(
        timeout: CoreHostSettings.timeout_seconds,
        max_output_bytes: CoreHostSettings.max_output_bytes,
        max_stderr_bytes: CoreHostSettings.max_stderr_bytes
      )
    end

    it "子プロセスに渡す環境変数は許可したものだけ（秘密の値は渡さない）" do
      launcher.respond_with(host_success(init_output))

      with_env("LASD_CLIENT_SPEC_SECRET" => "must-not-pass") { client.init(difficulty: "normal") }

      env = launcher.calls.sole[:env]
      allowed = CoreHostSettings.child_env_keys + CoreHostSettings.child_env.keys
      expect(env.keys).to include("PATH")
      expect(env.keys - allowed).to be_empty
      expect(env.values).not_to include("must-not-pass")
    end
  end

  describe "DLL の確認（起動しない）" do
    it "CORE_HOST_DLL が無ければ内部エラー（500）" do
      with_env("CORE_HOST_DLL" => nil) do
        expect { client.init(difficulty: "normal") }.to raise_error(ApiError) { |error| expect(error.status).to eq(500) }
      end
      expect(launcher.calls).to be_empty
    end

    it "相対パスと存在しないファイルも内部エラー（起動しない）" do
      [ "relative/CoreHost.dll", "/nonexistent/lasd/CoreHost.dll" ].each do |path|
        with_env("CORE_HOST_DLL" => path) do
          expect { client.init(difficulty: "normal") }
            .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
        end
      end
      expect(launcher.calls).to be_empty
    end
  end

  describe "入力の上限（64KB を超えたら起動しない）" do
    it "65,536 バイトまでは起動し、それを超えると起動せずに内部エラー（500）" do
      launcher.respond_with(host_success(init_output))
      # {"difficulty":"…"} は文字列の長さに 17 バイトを足した大きさ
      client.init(difficulty: "x" * (65_536 - 17))
      expect(launcher.calls.sole[:input].bytesize).to eq(65_536)

      expect { client.init(difficulty: "x" * (65_536 - 16)) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
      expect(launcher.calls.size).to eq(1)
    end
  end

  describe "成功（終了コード 0）の出力" do
    it "形が正しい出力は、そのまま返す" do
      launcher.respond_with(host_success(init_output))

      expect(client.init(difficulty: "normal")).to eq(init_output)
    end

    it "JSON として壊れた出力は内部エラー（500）" do
      launcher.respond_with(host_result(status: 0, stdout: "{"))

      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
    end

    it "キーが欠けた・型が違う出力は内部エラー（500）" do
      missing = init_output.deep_dup
      missing["state"].delete("age")
      launcher.respond_with(host_success(missing))
      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }

      wrong_type = init_output.deep_dup
      wrong_type["state"]["age"] = "22"
      launcher.respond_with(host_success(wrong_type))
      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
    end
  end

  describe "規則違反（終了コード 2）" do
    it "理由コードが API のエラーコードなら、その ApiError（difficulty_unknown は 422）" do
      launcher.respond_with(host_rule_violation("difficulty_unknown"))

      expect { client.init(difficulty: "nope") }.to raise_error(ApiError) do |error|
        expect(error.code).to eq(:difficulty_unknown)
        expect(error.status).to eq(422)
      end
    end

    it "allocation_sum_mismatch は 422、game_finished は 409 にする" do
      launcher.respond_with(host_rule_violation("allocation_sum_mismatch", turn_no: 2))
      expect { client.replay(seed: 1, difficulty: "normal", history: []) }.to raise_error(ApiError) do |error|
        expect([ error.code, error.status ]).to eq([ :allocation_sum_mismatch, 422 ])
      end

      launcher.respond_with(host_rule_violation("game_finished", turn_no: 44))
      expect { client.replay(seed: 1, difficulty: "normal", history: []) }.to raise_error(ApiError) do |error|
        expect([ error.code, error.status ]).to eq([ :game_finished, 409 ])
      end
    end

    it "allocation_negative・allocation_step_mismatch も 422 にする（理由コードを対応させる）" do
      %w[allocation_negative allocation_step_mismatch].each do |code|
        launcher.respond_with(host_rule_violation(code, turn_no: 2))

        expect { client.replay(seed: 1, difficulty: "normal", history: []) }.to raise_error(ApiError) do |error|
          expect([ error.code, error.status ]).to eq([ code.to_sym, 422 ])
        end
      end
    end

    it "対応表にない理由コード（request_invalid など）は内部エラー（500）" do
      launcher.respond_with(host_rule_violation("request_invalid"))

      expect { client.replay(seed: 1, difficulty: "normal", history: []) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
    end

    it "エラーの形が壊れていれば内部エラー（500）" do
      launcher.respond_with(host_result(status: 2, stdout: '{"error":{"message":"x"}}'))

      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
    end
  end

  describe "内部エラー（終了コード 1・ほかの終了コード・終了コードが無い）" do
    it "終了コード 1 は 500（出力の中身は見ない）" do
      launcher.respond_with(host_result(status: 1, stdout: '{"error":{"code":"internal_error","message":"x"}}'))

      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
    end

    it "終了コードが無い（シグナルで終わった）・ほかの値も 500" do
      [ nil, 3 ].each do |status|
        launcher.respond_with(host_result(status: status))

        expect { client.init(difficulty: "normal") }
          .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
      end
    end
  end

  describe "起動の失敗と制限時間" do
    it "制限時間を超えた（TimedOut）は 503" do
      launcher.fail_with(CoreHostProcess::TimedOut.new("time"))

      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:service_unavailable) }
    end

    it "起動できない・出力が大きすぎる（Failure）は 500" do
      launcher.fail_with(CoreHostProcess::Failure.new("launch"))

      expect { client.init(difficulty: "normal") }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:internal_error) }
    end

    it "失敗しても同時実行の枠は戻る" do
      single = Concurrent::Semaphore.new(1)
      limited = described_class.new(launcher: launcher, gate: single, wait_seconds: 0.1)
      launcher.fail_with(CoreHostProcess::Failure.new("launch"))

      expect { limited.init(difficulty: "normal") }.to raise_error(ApiError)
      expect(single.available_permits).to eq(1)
    end
  end

  describe "同時実行（2 件まで・待ちは 2 秒まで。テストでは待ちを 0.2 秒にする）" do
    let(:blocking) { BlockingCoreHostLauncher.new(JSON.generate(init_output)) }
    let(:limited) { described_class.new(launcher: blocking, gate: gate, wait_seconds: 0.2) }

    it "実行中が 2 件のとき、3 件目は待ってから 503 にし、起動しない" do
      workers = Array.new(2) { Thread.new { limited.init(difficulty: "normal") } }
      begin
        blocking.wait_for_started(2)

        started_at = Process.clock_gettime(Process::CLOCK_MONOTONIC)
        expect { limited.init(difficulty: "normal") }
          .to raise_error(ApiError) { |error| expect(error.code).to eq(:service_unavailable) }
        expect(Process.clock_gettime(Process::CLOCK_MONOTONIC) - started_at).to be >= 0.15
        expect(blocking.started_count).to eq(2)
      ensure
        blocking.open!
        workers.each(&:join)
      end

      expect(gate.available_permits).to eq(2)
    end

    it "待っている間に枠が空けば、3 件目は実行される（待ちは本番の 2 秒）" do
      patient = described_class.new(launcher: blocking, gate: gate, wait_seconds: CoreHostSettings.wait_seconds)
      workers = Array.new(2) { Thread.new { patient.init(difficulty: "normal") } }
      begin
        blocking.wait_for_started(2)
        opener = Thread.new do
          sleep 0.05
          blocking.open!
        end

        expect(patient.init(difficulty: "normal")).to eq(init_output)
        opener.join
      ensure
        blocking.open!
        workers.each(&:join)
      end
    end

    it "共有の枠は 2 件（プロセス内で共有する）" do
      expect(described_class.gate.available_permits).to eq(2)
    end
  end

  describe "ログ" do
    it "内部エラーのとき、入力の JSON も出力の値もログに出さない" do
      launcher.respond_with(host_result(status: 1, stdout: '{"marker":"MARKER_OUTPUT"}'))

      logs = capture_logs do
        expect { client.init(difficulty: "MARKER_INPUT") }.to raise_error(ApiError)
      end

      expect(logs).to include("[core_host]")
      expect(logs).not_to include("MARKER_INPUT")
      expect(logs).not_to include("MARKER_OUTPUT")
    end

    it "終了コードの失敗は、呼んだ種類（init）と終了コードだけを記録し、標準エラーの内容は記録しない" do
      launcher.respond_with(host_result(status: 1, stderr: "STDERR_MARKER /internal/host/path"))

      logs = capture_logs do
        expect { client.init(difficulty: "normal") }.to raise_error(ApiError)
      end

      expect(logs).to include("[core_host] init exit status 1")
      expect(logs).not_to include("STDERR_MARKER")
      expect(logs).not_to include("/internal/host/path")
    end

    it "replay の失敗も、呼んだ種類（replay）と終了コードだけを記録する（終了コードが無い場合も標準エラーは出さない）" do
      launcher.respond_with(host_result(status: 1, stderr: "STDERR_MARKER"))
      logs = capture_logs do
        expect { client.replay(seed: 1, difficulty: "normal", history: []) }.to raise_error(ApiError)
      end
      expect(logs).to include("[core_host] replay exit status 1")
      expect(logs).not_to include("STDERR_MARKER")

      launcher.respond_with(host_result(status: nil, stderr: "STDERR_MARKER"))
      logs = capture_logs do
        expect { client.replay(seed: 1, difficulty: "normal", history: []) }.to raise_error(ApiError)
      end
      expect(logs).to include("[core_host] replay exit status nil")
      expect(logs).not_to include("STDERR_MARKER")
    end
  end
end
