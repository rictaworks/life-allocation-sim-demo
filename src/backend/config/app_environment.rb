# APP_ENV による分岐の判定。開発用の分岐は APP_ENV が development のときだけ。
# 未設定・空・不明な値は本番として扱う（fail closed）。
# Cookie の Secure・レート制限・CORS は、この判定で緩めない。
module AppEnvironment
  ENV_KEY = "APP_ENV".freeze
  DEVELOPMENT = "development".freeze

  def self.development?(env = ENV)
    env[ENV_KEY] == DEVELOPMENT
  end
end
