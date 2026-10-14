require "rails_helper"

RSpec.describe CoreHostSettings do
  it "要件どおりの値：制限時間 5 秒・待ち 2 秒・同時実行 2 件・入力 64KB" do
    expect(described_class.timeout_seconds).to eq(5)
    expect(described_class.wait_seconds).to eq(2)
    expect(described_class.max_concurrent).to eq(2)
    expect(described_class.max_input_bytes).to eq(65_536)
  end

  it "終了コードは 0（成功）と 2（規則違反）。ほかは内部エラーとして扱う" do
    expect(described_class.exit_code(:success)).to eq(0)
    expect(described_class.exit_code(:rule_violation)).to eq(2)
  end

  it "コマンドは init と replay" do
    expect(described_class.command(:init)).to eq("init")
    expect(described_class.command(:replay)).to eq("replay")
  end

  it "規則違反の理由コードは、すべて API のエラーコードとして定義されている" do
    described_class.rule_violation_codes.each do |code|
      expect(ApiError::STATUSES).to have_key(code.to_sym), "未定義のエラーコード: #{code}"
    end
  end

  it "子プロセスに渡す環境変数は PATH と HOME だけ（DB や秘密の値は含めない）" do
    expect(described_class.child_env_keys).to contain_exactly("PATH", "HOME")
    expect(described_class.child_env.keys).to all(be_a(String))
  end

  it "DLL のパスは CORE_HOST_DLL から読む" do
    expect(described_class.dll_env_key).to eq("CORE_HOST_DLL")
  end
end
