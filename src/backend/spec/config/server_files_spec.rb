require "rails_helper"
require "erb"
require "puma"
require "puma/configuration"

# 設定ファイル（Puma・DB・本番のログ）が、ServerSettings の値を使い、従来と同じ値になることを確かめる。
RSpec.describe "サーバーの設定ファイル（config/server.yml の値と、環境変数による上書き）" do
  describe "config/puma.rb（スレッド数と待ち受けポート）" do
    # Puma の設定ファイルを、実際の読み込みと同じ順で読んで、結果の options を返す。
    def puma_options(overrides)
      with_env(overrides) do
        configuration = Puma::Configuration.new(config_files: [ Rails.root.join("config/puma.rb").to_s ])
        configuration.clamp
      end
    end

    it "環境変数が無ければ、スレッド 3（最小と最大）・ポート 3000" do
      options = puma_options("RAILS_MAX_THREADS" => nil, "PORT" => nil)

      expect([ options[:min_threads], options[:max_threads] ]).to eq([ 3, 3 ])
      expect(options[:binds].sole).to end_with(":3000")
    end

    it "RAILS_MAX_THREADS と PORT が設定されていれば、その値を使う" do
      options = puma_options("RAILS_MAX_THREADS" => "6", "PORT" => "4567")

      expect([ options[:min_threads], options[:max_threads] ]).to eq([ 6, 6 ])
      expect(options[:binds].sole).to end_with(":4567")
    end
  end

  describe "config/database.yml（接続数）" do
    # database.yml の ERB を描画して読む（ENV の値に従う）。
    def max_connections(overrides)
      with_env(overrides) do
        rendered = ERB.new(File.read(Rails.root.join("config/database.yml"))).result
        YAML.safe_load(rendered, aliases: true).fetch("test").fetch("max_connections")
      end
    end

    it "RAILS_MAX_THREADS が無ければ 5（従来の既定値）。設定されていればその値" do
      expect(max_connections("RAILS_MAX_THREADS" => nil)).to eq(5)
      expect(max_connections("RAILS_MAX_THREADS" => "7")).to eq(7)
    end
  end

  describe "config/environments/production.rb（ログの水準）" do
    it "水準は ServerSettings.log_level（config/server.yml の値、RAILS_LOG_LEVEL で上書き）を使う" do
      source = File.read(Rails.root.join("config/environments/production.rb"))

      expect(source).to include("config.log_level = ServerSettings.log_level")
    end
  end
end
