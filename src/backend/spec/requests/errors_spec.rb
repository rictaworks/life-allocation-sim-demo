require "rails_helper"

RSpec.describe "エラー応答", type: :request do
  before { https! }

  it "経路が無い場合も同じ形（code と message だけ）の 404 を返す" do
    get "/api/session"

    expect(response).to have_http_status(:not_found)
    expect(json_response).to eq(
      "error" => { "code" => "not_found", "message" => I18n.t("api_errors.not_found", locale: :ja) }
    )
  end

  it "予期しない例外は 500 の internal_error にし、例外の内容・スタックトレース・SQL を出さない" do
    allow(Session).to receive(:issue!).and_raise(RuntimeError, "SELECT * FROM sessions WHERE secret_marker = 1")

    post_session

    expect(response).to have_http_status(:internal_server_error)
    expect(json_response).to eq(
      "error" => { "code" => "internal_error", "message" => I18n.t("api_errors.internal_error", locale: :ja) }
    )
    expect(response.body).not_to include("secret_marker")
    expect(response.body).not_to include("SELECT")
    expect(response.body).not_to include(".rb")
  end

  it "死活確認（GET /up）は 200 を返す" do
    get "/up"

    expect(response).to have_http_status(:ok)
  end

  it "中間層で扱う不正な入力（不正な符号化・壊れた問い合わせ文字列）も、例外にせず JSON の形で返す" do
    [
      [ "POST", "/%ZZ", "" ],
      [ "POST", "/api/session%ff", "" ],
      [ "POST", "/api/session", "a[]=1&a[b]=2" ],
      [ "POST", "/api/session", "%ff=%zz" ],
      [ "GET", "/%ff", "" ]
    ].each do |method, path, query|
      status, headers, text = call_raw_request(method: method, path: path, query: query)
      label = "#{method} #{path}?#{query}"

      expect(headers["Content-Type"]).to start_with("application/json"), label
      expect(status).to be_between(400, 499), label
      expect(JSON.parse(text).dig("error", "code")).to be_present, label
    end
  end

  # URI として組み立てられない値を通すため、Rack の env を直接作って Rails の入口を呼ぶ。
  def call_raw_request(method:, path:, query:)
    body = "{}"
    env = {
      "REQUEST_METHOD" => method, "PATH_INFO" => path, "QUERY_STRING" => query,
      "SERVER_NAME" => "www.example.com", "SERVER_PORT" => "443", "HTTP_HOST" => "www.example.com",
      "HTTPS" => "on", "rack.url_scheme" => "https", "SERVER_PROTOCOL" => "HTTP/1.1",
      "REMOTE_ADDR" => "127.0.0.1", "rack.input" => StringIO.new(body), "rack.errors" => StringIO.new,
      "CONTENT_TYPE" => "application/json", "CONTENT_LENGTH" => body.bytesize.to_s,
      "HTTP_ORIGIN" => ApiRequestHelpers::TEST_ORIGIN, "HTTP_X_REAL_IP" => ApiRequestHelpers::TEST_CLIENT_IP
    }
    status, headers, parts = Rails.application.call(env)
    text = +""
    parts.each { |part| text << part }
    parts.close if parts.respond_to?(:close)
    [ status, headers, text ]
  end
end
