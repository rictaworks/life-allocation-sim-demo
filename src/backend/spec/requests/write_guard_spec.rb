require "rails_helper"

RSpec.describe "入口の検査（ApiWriteGuard：本体を読む前の Origin と Content-Type）", type: :request do
  before { https! }

  it "Origin が一致しなければ、form の本体でも 415 ではなく 403 を返す（検査の順番は Origin → Content-Type）" do
    post_session(body: "difficulty=easy",
                 headers: { "Origin" => "https://evil.example", "Content-Type" => "application/x-www-form-urlencoded" })

    expect(response).to have_http_status(:forbidden)
    expect(error_code).to eq("origin_forbidden")
    expect(Session.count).to eq(0)
  end

  it "form の本体は、コントローラに届かない（セッションを作らず、コントローラの処理のログも出ない）" do
    logs = capture_logs do
      post_session(body: "difficulty=easy", headers: { "Content-Type" => "application/x-www-form-urlencoded" })
    end

    expect(response).to have_http_status(:unsupported_media_type)
    expect(Session.count).to eq(0)
    expect(logs).not_to include("Processing by Api::SessionsController#create")
  end

  it "415 の応答は JSON の形（code と message）で、文言は config/locales/ja.yml から取る" do
    post_session(body: "difficulty=easy", headers: { "Content-Type" => "application/x-www-form-urlencoded" })

    expect(response.media_type).to eq("application/json")
    expect(json_response).to eq(
      "error" => { "code" => "unsupported_media_type", "message" => I18n.t("api_errors.unsupported_media_type", locale: :ja) }
    )
  end
end
