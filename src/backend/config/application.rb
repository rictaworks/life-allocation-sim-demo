require_relative "boot"

require "rails"
# 必要な railties だけを読む。Active Storage・Action Mailbox・Action Text は読まない
# （Active Job・Action Mailer・Action Cable も、この API では使わないので読まない）。
require "active_model/railtie"
require "active_record/railtie"
require "action_controller/railtie"

# Require the gems listed in Gemfile, including any gems
# you've limited to :test, :development, or :production.
Bundler.require(*Rails.groups)

require_relative "app_environment"
require_relative "app_settings"
# サーバーの設定値（Puma・SQLite の接続数・ログの水準）。database.yml と production.rb が使う。
require_relative "server_settings"

module Backend
  class Application < Rails::Application
    # 起動時の設定検査。不足・不正なら起動を止める（既定値では補わない）。
    # dotenv-rails は .env を before_configuration で読むため、検査はこのクラスの本体で行う（読み込みの後）。
    AppSettings.verify!(ENV)

    # Initialize configuration defaults for originally generated Rails version.
    config.load_defaults 8.1

    # API mode。ビュー・アセット・セッションは使わない。
    config.api_only = true

    # Cookie を読み書きするため。API mode では既定で入らないので明示的に加える。
    config.middleware.use ActionDispatch::Cookies

    # 文言は日本語版だけ。翻訳が無ければ例外にする（既定の言語への補完はしない）。
    config.i18n.default_locale = :ja
    config.i18n.available_locales = [ :ja ]
    config.i18n.raise_on_missing_translations = true

    # 読まない railties（Active Job・Action Mailer）に依存する scaffold の app/jobs・app/mailers は、
    # 読み込み（Zeitwerk）の対象から外す。ファイル自体の削除は手動で行う。
    config.before_initialize do
      Rails.autoloaders.main.ignore(Rails.root.join("app/jobs"), Rails.root.join("app/mailers"))
    end

    # 応答に例外を出さない。コントローラの外で起きた例外も、同じ形式の JSON にする。
    # ShowExceptions より前に動く中間層の例外は、この設定でも rescue_from でも JSON にできない。
    # そのため、中間層は例外を投げない構成にする（下の RemoteIp の削除と、Host の応答の指定）。
    config.action_dispatch.show_exceptions = :all
    config.exceptions_app = ->(env) { ApiExceptionsApp.call(env) }

    # 接続元の IP は X-Real-IP だけから読む（ClientAddress）。Rails の RemoteIp は外す。
    # RemoteIp は Client-Ip と X-Forwarded-For が食い違うと、中間層で例外を投げるため。
    config.middleware.delete ActionDispatch::RemoteIp

    # 起動時の検査で確かめた値を使う。
    config.secret_key_base = AppSettings.secret_key_base
    config.hosts = [ AppSettings.origin_host ]
    # 死活確認（/up）は Host の検査から外す（Railway の死活確認が別の Host で来るため）。
    # Host が一致しない応答は、Rails の既定（空の text/html）ではなく JSON で返す。
    config.host_authorization = {
      exclude: ->(request) { request.path == "/up" },
      response_app: ->(env) { ApiHostRejection.call(env) }
    }

    # Vercel と Railway の経由（SSL を終端するプロキシ）を前提にし、HTTPS を強制する。
    # APP_ENV が development 以外のすべて（本番・APP_ENV 未設定の test）で有効にする。RAILS_ENV では決めない。
    config.assume_ssl = !AppEnvironment.development?
    config.force_ssl = !AppEnvironment.development?
    # 死活確認（/up）は http からのアクセスでも転送しない（Railway の死活確認のため）。
    config.ssl_options = { redirect: { exclude: ->(request) { request.path == "/up" } } }
  end
end
