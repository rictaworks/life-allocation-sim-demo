require "rails_helper"

RSpec.describe "Host の検査（APP_ORIGIN のホストだけを受け付ける）", type: :request do
  before { https! }

  it "Host が一致しなければ、JSON（code と message）の 403 を返す（Rails の既定の空の text/html にしない）" do
    host! "evil.example"
    post_session

    expect(response).to have_http_status(:forbidden)
    expect(response.media_type).to eq("application/json")
    expect(json_response).to eq(
      "error" => { "code" => "origin_forbidden", "message" => I18n.t("api_errors.origin_forbidden", locale: :ja) }
    )
    expect(Session.count).to eq(0)
  end

  it "ホスト名の一部一致（サブドメインの付加・名前の混入）も同じ形で拒否する" do
    [ "www.example.com.evil.example", "evil-www.example.com" ].each do |name|
      host! name
      post_session

      expect(response).to have_http_status(:forbidden), "Host=#{name}"
      expect(response.media_type).to eq("application/json")
      expect(error_code).to eq("origin_forbidden")
    end
  end

  it "死活確認（GET /up）は Host の検査から外す（別の Host でも 200）" do
    host! "evil.example"
    get "/up"

    expect(response).to have_http_status(:ok)
  end

  it "拒否した Host は運用のためログに出す（引用符で囲み、応答には出さない）" do
    logs = capture_logs do
      host! "evil.example"
      post_session
    end

    expect(logs).to include('[api] blocked host: "evil.example"')
    expect(response.body).not_to include("evil.example")
  end
end
