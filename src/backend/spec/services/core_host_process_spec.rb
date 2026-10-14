require "rails_helper"

# 実際の子プロセスを起動して、起動の仕方（シェルを介さない・環境変数・入出力・制限時間・出力の上限）を検査する。
RSpec.describe CoreHostProcess do
  def run(argv, input: "", env: {}, timeout: 5, max_output_bytes: 65_536)
    described_class.run(
      argv: argv, env: env, input: input, timeout: timeout,
      max_output_bytes: max_output_bytes, max_stderr_bytes: 4_096
    )
  end

  it "引数を配列のまま渡し、シェルを介さない（$(...) は展開されない）" do
    result = run([ "echo", "$(echo injected)" ])

    expect(result.stdout).to eq("$(echo injected)\n")
  end

  it "入力を標準入力に渡し、終了コードと標準出力を返す" do
    result = run([ "cat" ], input: "abc")

    expect(result).to have_attributes(status: 0, stdout: "abc")
  end

  it "0 以外の終了コードも返す" do
    expect(run([ "sh", "-c", "exit 3" ]).status).to eq(3)
  end

  it "渡した環境変数だけが子プロセスに届く（親の環境変数は届かない）" do
    script = 'echo "[$LASD_PROCESS_SPEC_MARKER]"'
    with_env("LASD_PROCESS_SPEC_MARKER" => "parent-only") do
      expect(run([ "sh", "-c", script ]).stdout).to eq("[]\n")
      expect(run([ "sh", "-c", script ], env: { "LASD_PROCESS_SPEC_MARKER" => "given" }).stdout).to eq("[given]\n")
    end
  end

  it "制限時間を超えたら止めて TimedOut にする（待ちきらない）" do
    started = Process.clock_gettime(Process::CLOCK_MONOTONIC)

    expect { run([ "sleep", "10" ], timeout: 0.2) }.to raise_error(CoreHostProcess::TimedOut)
    expect(Process.clock_gettime(Process::CLOCK_MONOTONIC) - started).to be < 5
  end

  it "出力が上限を超えたら止めて Failure にする" do
    expect { run([ "head", "-c", "2000000", "/dev/zero" ], max_output_bytes: 1_000) }
      .to raise_error(CoreHostProcess::Failure)
  end

  it "起動できない実行ファイルは Failure にする" do
    expect { run([ "/nonexistent/lasd-core-host" ]) }.to raise_error(CoreHostProcess::Failure)
  end
end
