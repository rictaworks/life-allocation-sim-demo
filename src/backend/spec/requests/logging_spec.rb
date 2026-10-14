require "rails_helper"

RSpec.describe "ログに出さないもの（Cookie・セッション ID・リクエストボディ）", type: :request do
  before { https! }

  it "Cookie の値も本体の値も、正常・解析失敗のどちらでもログに出さない" do
    token = issue_session_token

    logs = capture_logs do
      post_session(body: '{"difficulty":"MARKER_VALUE_ONE"}',
                   headers: { "Cookie" => cookie_header(token), "X-Real-IP" => "198.51.100.30" })
      post_session(body: '{"difficulty":"MARKER_VALUE_TWO"',
                   headers: { "X-Real-IP" => "198.51.100.31" })
    end

    expect(logs).to include("Processing by Api::SessionsController#create")
    expect(logs).not_to include(token)
    expect(logs).not_to include("MARKER_VALUE_ONE")
    expect(logs).not_to include("MARKER_VALUE_TWO")
  end

  it "要求ログに接続元の IP を出さない（X-Real-IP・X-Forwarded-For のどちらの値も出ない）" do
    logs = capture_logs do
      post_session(headers: { "X-Real-IP" => "198.51.100.40", "X-Forwarded-For" => "198.51.100.88" })
    end

    expect(logs).to include('Started POST "/api/session"')
    expect(logs).not_to include("198.51.100.40")
    expect(logs).not_to include("198.51.100.88")
  end

  describe "form・multipart の本体（Rack が解析する前に 415 で拒否する）" do
    let(:boundary) { "LASD-TEST-BOUNDARY" }

    def multipart_body(value)
      "--#{boundary}\r\nContent-Disposition: form-data; name=\"difficulty\"\r\n\r\n#{value}\r\n--#{boundary}--\r\n"
    end

    it "application/x-www-form-urlencoded の本体は、値もキーもログに出さず 415 で拒否する" do
      logs = capture_logs do
        post_session(body: "difficulty=MARKER_FORM_1&MARKER_FORM_KEY=1",
                     headers: { "Content-Type" => "application/x-www-form-urlencoded" })
      end

      expect(response).to have_http_status(:unsupported_media_type)
      expect(logs).not_to include("MARKER_FORM_1")
      expect(logs).not_to include("MARKER_FORM_KEY")
      expect(logs).not_to include("Parameters:")
    end

    it "multipart/form-data の本体は、値をログに出さず 415 で拒否する" do
      logs = capture_logs do
        post_session(body: multipart_body("MARKER_MULTIPART_1"),
                     headers: { "Content-Type" => "multipart/form-data; boundary=#{boundary}" })
      end

      expect(response).to have_http_status(:unsupported_media_type)
      expect(logs).not_to include("MARKER_MULTIPART_1")
      expect(logs).not_to include("Parameters:")
    end

    it "2MB の form 本体も読まずに 415 で拒否し、本体の大きさのログ行を出さない" do
      large_body = "difficulty=MARKER_FORM_BIG&pad=#{"x" * (2 * 1024 * 1024)}"

      logs = capture_logs do
        post_session(body: large_body, headers: { "Content-Type" => "application/x-www-form-urlencoded" })
      end

      expect(response).to have_http_status(:unsupported_media_type)
      expect(logs).not_to include("MARKER_FORM_BIG")
      expect(logs.bytesize).to be < 4096
    end

    it "GET（死活確認を含む）でも form の本体は読まず、ログに出さない" do
      result = nil
      logs = capture_logs do
        result = Rack::MockRequest.new(Rails.application).get(
          "/up",
          input: "MARKER_GET_KEY=MARKER_GET_VALUE",
          "CONTENT_TYPE" => "application/x-www-form-urlencoded",
          "HTTPS" => "on",
          "HTTP_HOST" => "www.example.com"
        )
      end

      expect(result.status).to eq(415)
      expect(logs).not_to include("MARKER_GET_KEY")
      expect(logs).not_to include("MARKER_GET_VALUE")
      expect(logs).not_to include("Parameters:")
    end
  end
end
