require "yaml"

# config/server.yml の値を読む。Puma の設定（config/puma.rb）は Rails より前に読まれるため、Rails には依存しない（AppSettings と同じ作り）。
# 既定値はすべて設定ファイルにあり、コードには書かない。環境変数は、設定されているときだけ値を上書きする
# （設定されていなければ、設定ファイルの値を使う）。値の形（整数など）は、読む側（Puma・Rails）が検査する。
module ServerSettings
  FILE_PATH = File.expand_path("server.yml", __dir__).freeze
  MAX_THREADS_ENV_KEY = "RAILS_MAX_THREADS".freeze
  PORT_ENV_KEY = "PORT".freeze
  LOG_LEVEL_ENV_KEY = "RAILS_LOG_LEVEL".freeze

  class << self
    # Puma のスレッド数（RAILS_MAX_THREADS で上書き）。
    def puma_max_threads
      overridable(MAX_THREADS_ENV_KEY) { fetch(:puma, :max_threads) }
    end

    # Puma の待ち受けポート（PORT で上書き）。
    def puma_port
      overridable(PORT_ENV_KEY) { fetch(:puma, :port) }
    end

    # SQLite の接続数の上限（RAILS_MAX_THREADS で上書き。config/database.yml が使う）。
    def database_max_connections
      overridable(MAX_THREADS_ENV_KEY) { fetch(:database, :max_connections) }
    end

    # 本番のログの水準（RAILS_LOG_LEVEL で上書き。config/environments/production.rb が使う）。
    def log_level
      overridable(LOG_LEVEL_ENV_KEY) { fetch(:log, :level) }
    end

    private

    # 環境変数が設定されていれば、その値（文字列のまま）。設定されていなければ、設定ファイルの値。
    def overridable(name)
      return ENV.fetch(name) if ENV.key?(name)

      yield
    end

    def fetch(*path)
      path.reduce(settings) { |node, key| node.fetch(key) }
    end

    def settings
      @settings ||= YAML.safe_load_file(FILE_PATH, symbolize_names: true).fetch(:shared)
    end
  end
end
