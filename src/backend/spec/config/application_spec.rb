require "rails_helper"

RSpec.describe "アプリケーションの設定" do
  it "API mode で動く（config.api_only が true）" do
    expect(Rails.application.config.api_only).to be(true)
  end

  it "Active Storage・Action Mailbox・Action Text を読み込まない" do
    expect(defined?(ActiveStorage)).to be_nil
    expect(defined?(ActionMailbox)).to be_nil
    expect(defined?(ActionText)).to be_nil
    expect(Rails::Engine.subclasses.map(&:name))
      .not_to include("ActiveStorage::Engine", "ActionMailbox::Engine", "ActionText::Engine")
  end

  it "日本語版だけを使う（既定の言語は ja。翻訳の補完はしない）" do
    expect(I18n.default_locale).to eq(:ja)
    expect(I18n.available_locales).to eq([ :ja ])
  end

  it "Cookie を読み書きする中間層を持つ（API mode では既定で入らない）" do
    expect(Rails.application.middleware.map(&:klass)).to include(ActionDispatch::Cookies)
  end

  it "接続元の IP は X-Real-IP だけから読む（Rails の RemoteIp は使わない。なりすまし検査の例外を応答に出さないため）" do
    expect(Rails.application.middleware.map(&:klass)).not_to include(ActionDispatch::RemoteIp)
  end

  it "要求ログは接続元の IP を出さない版（ApiRequestLogger）を使う" do
    klasses = Rails.application.middleware.map(&:klass)

    expect(klasses).to include(ApiRequestLogger)
    expect(klasses).not_to include(Rails::Rack::Logger)
  end

  it "本体を読む前の入口の検査（ApiWriteGuard）を、例外の JSON 化（ShowExceptions）の内側・要求ログの内側に置く" do
    klasses = Rails.application.middleware.map(&:klass)

    expect(klasses.index(ApiWriteGuard)).to be > klasses.index(ActionDispatch::ShowExceptions)
    expect(klasses.index(ApiWriteGuard)).to be > klasses.index(ApiRequestLogger)
  end

  it "APP_ENV が development 以外（この spec では未設定）なら HTTPS を強制し、Proxy の HTTPS を前提にする" do
    expect(AppEnvironment.development?).to be(false)
    expect(Rails.application.config.force_ssl).to be(true)
    expect(Rails.application.config.assume_ssl).to be(true)
  end

  it "Ruby のバージョンは 3.4.11 に統一する（.ruby-version）" do
    expect(File.read(Rails.root.join(".ruby-version")).strip).to eq("3.4.11")
  end

  it "経路は GET（/up・/api/game）と POST（/api/session・/api/games・/api/games/turns）だけ。状態を変えるのは POST だけ" do
    routes = Rails.application.routes.routes.map { |route| [ route.verb, route.path.spec.to_s ] }
    expect(routes).to contain_exactly(
      [ "GET", "/up(.:format)" ],
      [ "GET", "/api/game(.:format)" ],
      [ "POST", "/api/session(.:format)" ],
      [ "POST", "/api/games(.:format)" ],
      [ "POST", "/api/games/turns(.:format)" ]
    )
  end
end
