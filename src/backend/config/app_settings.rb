require "pathname"
require "uri"
require "yaml"

# 起動時の設定検査。Rails を読む前に呼ぶため、文言は config/locales/ja.yml の startup から直接読む。
# 既定値では補わない。不足・不正なら AppSettings::Invalid で起動を止める。値そのものは出さない。
module AppSettings
  class Invalid < StandardError; end

  RAILS_ENV_KEY = "RAILS_ENV".freeze
  APP_ENV_KEY = AppEnvironment::ENV_KEY
  SECRET_KEY_BASE_KEY = "SECRET_KEY_BASE".freeze
  DATABASE_PATH_KEY = "DATABASE_PATH".freeze
  APP_ORIGIN_KEY = "APP_ORIGIN".freeze

  RAILS_ENVIRONMENTS = %w[development test production].freeze
  SECRET_KEY_BASE_MIN_LENGTH = 64
  # scheme://ホスト[:ポート]。小文字だけ（ブラウザが送る Origin と完全一致させるため）、末尾の / や path は不可。
  ORIGIN_PATTERN = %r{\A(?<scheme>https?)://(?<host>[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*)(:\d{1,5})?\z}
  MESSAGES_PATH = File.expand_path("locales/ja.yml", __dir__).freeze

  class << self
    def verify!(env = ENV)
      verify_rails_env!(env)
      verify_app_env_matches_rails_env!(env)
      verify_secret_key_base!(env)
      verify_database_path!(env)
      verify_app_origin!(env)
      true
    end

    def secret_key_base(env = ENV)
      env.fetch(SECRET_KEY_BASE_KEY)
    end

    def origin(env = ENV)
      env.fetch(APP_ORIGIN_KEY)
    end

    def origin_host(env = ENV)
      URI.parse(origin(env)).host
    end

    private

    def verify_rails_env!(env)
      return if RAILS_ENVIRONMENTS.include?(env[RAILS_ENV_KEY])

      fail_with(:invalid_rails_env, RAILS_ENV_KEY)
    end

    # 開発用の設定（RAILS_ENV=development の config/environments/development.rb）と、開発用の分岐（APP_ENV=development）は、
    # 必ず両方そろえる。片方だけなら起動を止める。未設定・表記ゆれは本番扱いのため、開発用の設定は有効にしない。
    def verify_app_env_matches_rails_env!(env)
      rails_development = env[RAILS_ENV_KEY] == AppEnvironment::DEVELOPMENT
      app_development = AppEnvironment.development?(env)
      return if rails_development == app_development

      key = app_development ? :development_app_env_outside_development : :development_rails_env_without_app_env
      fail_with(key, APP_ENV_KEY)
    end

    def verify_secret_key_base!(env)
      value = env[SECRET_KEY_BASE_KEY].to_s
      fail_with(:missing_env, SECRET_KEY_BASE_KEY) if value.empty?
      return if value.length >= SECRET_KEY_BASE_MIN_LENGTH

      fail_with(:short_secret_key_base, SECRET_KEY_BASE_KEY)
    end

    def verify_database_path!(env)
      value = env[DATABASE_PATH_KEY].to_s
      fail_with(:missing_env, DATABASE_PATH_KEY) if value.empty?
      return if Pathname.new(value).absolute?

      fail_with(:relative_database_path, DATABASE_PATH_KEY)
    end

    def verify_app_origin!(env)
      value = env[APP_ORIGIN_KEY].to_s
      fail_with(:missing_env, APP_ORIGIN_KEY) if value.empty?

      match = ORIGIN_PATTERN.match(value)
      fail_with(:invalid_app_origin, APP_ORIGIN_KEY) if match.nil?
      return if match[:scheme] == "https" || AppEnvironment.development?(env)

      fail_with(:https_required_app_origin, APP_ORIGIN_KEY)
    end

    def fail_with(key, name)
      template = messages.fetch(key.to_s)
      raise Invalid, format(template, name: name)
    end

    def messages
      @messages ||= YAML.safe_load_file(MESSAGES_PATH).dig("ja", "startup")
    end
  end
end
