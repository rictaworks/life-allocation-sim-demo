# テスト用の設定値。本体のコードには既定値を置かないため、Rails を読み込む前にここで明示的に設定する。
# 実行環境に export された値（RAILS_ENV・APP_ENV など）には左右されないようにする。
require "securerandom"

ENV["RAILS_ENV"] = "test"
ENV.delete("APP_ENV") # 未設定（本番扱い）で実行する。開発用の分岐は個別の spec で切り替える。
ENV["SECRET_KEY_BASE"] = SecureRandom.hex(64) # 実行ごとに作る（秘密の値をリポジトリに置かない）。
ENV["DATABASE_PATH"] = File.expand_path("../tmp/test.sqlite3", __dir__)
ENV["APP_ORIGIN"] = "https://www.example.com"
# 再計算ホスト（統合テストだけが使う）。CI と開発では CORE_HOST_DLL を明示する。未設定なら、リポジトリ内のビルド結果を指す。
ENV["CORE_HOST_DLL"] ||= File.expand_path("../../core-host/CoreHost/bin/Release/net10.0/CoreHost.dll", __dir__)

RSpec.configure do |config|
  config.expect_with :rspec do |expectations|
    expectations.include_chain_clauses_in_custom_matcher_descriptions = true
  end

  config.mock_with :rspec do |mocks|
    mocks.verify_partial_doubles = true
  end

  config.shared_context_metadata_behavior = :apply_to_host_groups
end
