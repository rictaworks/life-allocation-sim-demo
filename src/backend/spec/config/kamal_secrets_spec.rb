require "rails_helper"

# .kamal/secrets は Git に載る（ファイルのヘッダーのとおり、安全な参照だけを書く）。
# 値は直接書かず、環境変数の参照（NAME=$NAME）だけにする（CLAUDE.md：シークレットは環境変数で渡す）。
# 失敗しても値は出さない（キー名だけを示す）。
RSpec.describe "Kamal の秘密の設定ファイル（.kamal/secrets）" do
  let(:path) { Rails.root.join(".kamal/secrets") }
  let(:assignments) do
    File.readlines(path, chomp: true).reject { |line| line.strip.empty? || line.lstrip.start_with?("#") }
  end

  it "代入はすべて環境変数の参照（NAME=$NAME）で、値を直接書かない" do
    expect(assignments).not_to be_empty

    literal_keys = assignments
                   .reject { |line| line.match?(/\A(?<name>[A-Z][A-Z0-9_]*)=\$\k<name>\z/) }
                   .map { |line| line.split("=", 2).first }

    expect(literal_keys).to be_empty, "値を直接書いているキー: #{literal_keys.inspect}"
  end
end
