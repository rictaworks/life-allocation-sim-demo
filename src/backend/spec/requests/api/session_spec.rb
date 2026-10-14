require "rails_helper"

RSpec.describe "POST /api/session", type: :request do
  let(:now) { Time.utc(2026, 10, 14, 12, 0, 0) }

  before { https! }

  describe "新規の発行（201）" do
    it "created を返し、Cookie で token を渡す" do
      post_session

      expect(response).to have_http_status(:created)
      expect(json_response).to eq("session" => { "status" => "created" })
      expect(token_from_set_cookie).to match(/\A[A-Za-z0-9_-]{43}\z/)
    end

    it "Cookie は __Host- 接頭辞・Secure・HttpOnly・SameSite=Lax・Path=/・Domain なし・7 日" do
      post_session

      attributes = set_cookie_header_text.downcase.split(";").map(&:strip)
      expect(set_cookie_header_text).to start_with("__Host-lasd_session=")
      expect(attributes).to include("path=/", "secure", "httponly", "samesite=lax", "max-age=604800")
      expect(attributes.grep(/\Adomain=/)).to be_empty
    end

    it "DB には SHA-256 のハッシュだけを保存し、token そのものは保存しない" do
      travel_to(now) { post_session }
      token = token_from_set_cookie

      record = Session.sole
      expect(record.token_digest).to eq(Digest::SHA256.hexdigest(token))
      expect(record.attributes.values.map(&:to_s)).not_to include(token)
    end

    it "created_at を記録し、expires_at は 7 日後にする" do
      travel_to(now) { post_session }

      record = Session.sole
      expect(record.created_at).to eq(now)
      expect(record.expires_at).to eq(now + 7.days)
    end
  end

  describe "有効な Cookie がある場合（200）" do
    it "existing を返し、行も Cookie も増やさない" do
      token = issue_session_token

      expect { post_session(headers: { "Cookie" => cookie_header(token) }) }
        .not_to change(Session, :count)
      expect(response).to have_http_status(:ok)
      expect(json_response).to eq("session" => { "status" => "existing" })
      expect(response.headers["Set-Cookie"]).to be_nil
    end

    it "期限切れの Cookie は無効。新規として 201 を返す" do
      token = travel_to(now) { issue_session_token }

      travel_to(now + 7.days + 1.second) do
        post_session(headers: { "Cookie" => cookie_header(token) })
      end

      expect(response).to have_http_status(:created)
    end

    it "利用しても寿命は延びない（発行から 7 日で切れ、利用のたびに延ばさない）" do
      token = travel_to(now) { issue_session_token }

      travel_to(now + 6.days) do
        get "/api/game", headers: { "Cookie" => cookie_header(token) }
        expect(response).to have_http_status(:ok)

        post_session(headers: { "Cookie" => cookie_header(token) })
        expect(response).to have_http_status(:ok)
      end
      expect(Session.sole.expires_at).to eq(now + 7.days)

      travel_to(now + 7.days + 1.second) do
        post_session(headers: { "Cookie" => cookie_header(token) })
      end
      expect(response).to have_http_status(:created)
    end

    it "一致しない Cookie の値は無効。新規として 201 を返す" do
      post_session(headers: { "Cookie" => cookie_header("x" * 43) })

      expect(response).to have_http_status(:created)
    end
  end

  describe "セッションを読むのは Cookie だけ" do
    it "URL のクエリや本体に入れた token は無視する" do
      token = issue_session_token
      name = ApiSettings.cookie_name

      post "/api/session?#{name}=#{token}",
           params: "{\"#{name}\":\"#{token}\"}",
           headers: write_headers

      expect(response).to have_http_status(:created)
    end
  end

  describe "Origin（403）" do
    it "APP_ORIGIN と完全一致しなければ拒否する（Origin が無い場合も同じ）" do
      [ nil, "https://evil.example", "http://www.example.com", "https://www.example.com/", "https://www.example.com:8443" ]
        .each do |origin|
          post_session(headers: { "Origin" => origin })

          expect(response).to have_http_status(:forbidden), "Origin=#{origin.inspect}"
          expect(error_code).to eq("origin_forbidden")
        end
      expect(Session.count).to eq(0)
    end
  end

  describe "Content-Type（415）" do
    it "application/json（charset=utf-8 だけ可）以外は拒否する" do
      [
        "text/plain",
        "application/x-www-form-urlencoded",
        "application/json; foo=bar",
        "application/json; charset=ascii",
        "application/jsonx"
      ].each do |content_type|
        post_session(headers: { "Content-Type" => content_type })

        expect(response).to have_http_status(415), "Content-Type=#{content_type}"
        expect(error_code).to eq("unsupported_media_type")
      end
      expect(Session.count).to eq(0)
    end

    it "charset=utf-8 は受け付ける（大文字小文字は問わない）" do
      post_session(headers: { "Content-Type" => "Application/JSON; Charset=UTF-8" })

      expect(response).to have_http_status(:created)
    end
  end

  describe "本体のサイズ（413）" do
    it "8192 バイトまでは受け付け、8193 バイトは拒否する" do
      post_session(body: padded_json(8192), headers: { "X-Real-IP" => "198.51.100.1" })
      expect(response).to have_http_status(:created)

      post_session(body: padded_json(8193), headers: { "X-Real-IP" => "198.51.100.2" })
      expect(response).to have_http_status(413)
      expect(error_code).to eq("payload_too_large")
    end
  end

  describe "JSON（400）" do
    it "壊れた JSON・空の本体・配列・null・UTF-8 でない本体は拒否する" do
      bodies = [ "{", "", "[]", "\"text\"", "null", "{\"a\":\"\xff\"}".b ]
      bodies.each_with_index do |body, index|
        post_session(body: body, headers: { "X-Real-IP" => "198.51.100.#{10 + index}" })

        expect(response).to have_http_status(:bad_request), "body=#{body.inspect}"
        expect(error_code).to eq("bad_request")
      end
      expect(Session.count).to eq(0)
    end

    it "未知のキーは無視する" do
      post_session(body: '{"unknown":1,"difficulty":"easy"}', headers: { "X-Real-IP" => "198.51.100.20" })

      expect(response).to have_http_status(:created)
    end
  end

  describe "X-Real-IP（400）" do
    it "無い・形式が不正なら拒否し、セッションを発行しない" do
      [ nil, "", "abc", "203.0.113.10/32", "203.0.113.256", "203.0.113.10, 198.51.100.2", "203.0.113.10:8080", "fe80::1%eth0" ]
        .each do |ip|
          post_session(headers: { "X-Real-IP" => ip })

          expect(response).to have_http_status(:bad_request), "X-Real-IP=#{ip.inspect}"
          expect(error_code).to eq("bad_request")
        end
      expect(Session.count).to eq(0)
    end

    it "X-Forwarded-For があっても、X-Real-IP が無ければ 400" do
      post_session(headers: { "X-Real-IP" => nil, "X-Forwarded-For" => "198.51.100.9" })

      expect(response).to have_http_status(:bad_request)
    end

    it "有効な Cookie があっても、X-Real-IP が不正なら 400" do
      token = issue_session_token

      post_session(headers: { "Cookie" => cookie_header(token), "X-Real-IP" => "not-an-ip" })

      expect(response).to have_http_status(:bad_request)
    end
  end

  describe "X-Real-IP 以外の接続元のヘッダー（Client-Ip など）は読まない" do
    it "Client-Ip と X-Forwarded-For が食い違っても例外にせず、X-Real-IP で 201 を返す" do
      post_session(headers: { "Client-Ip" => "198.51.100.50", "X-Forwarded-For" => "198.51.100.60" })

      expect(response).to have_http_status(:created)
      expect(json_response).to eq("session" => { "status" => "created" })
    end

    it "Client-Ip と X-Forwarded-For が食い違い、X-Real-IP が無ければ JSON の 400 を返す" do
      post_session(headers: { "X-Real-IP" => nil, "Client-Ip" => "198.51.100.50", "X-Forwarded-For" => "198.51.100.60" })

      expect(response).to have_http_status(:bad_request)
      expect(error_code).to eq("bad_request")
      expect(Session.count).to eq(0)
    end
  end

  describe "エラー応答の形" do
    it "{error: {code, message}} の形で、message は config/locales/ja.yml の文言" do
      post_session(headers: { "Origin" => "https://evil.example" })

      expect(json_response.keys).to eq([ "error" ])
      expect(json_response["error"]).to eq(
        "code" => "origin_forbidden",
        "message" => I18n.t("api_errors.origin_forbidden", locale: :ja)
      )
    end
  end

  describe "CORS は付けない（同一オリジンだけ）" do
    it "CORS のヘッダーを返さず、プリフライトにも応答しない" do
      post_session

      expect(response.headers.keys.grep(/\Aaccess-control-/i)).to be_empty

      # ブラウザのプリフライトは本体も Content-Type も持たないため、integration の既定（form の Content-Type）を使わない。
      preflight = Rack::MockRequest.new(Rails.application).options(
        "/api/session",
        "HTTP_ORIGIN" => ApiRequestHelpers::TEST_ORIGIN,
        "HTTP_ACCESS_CONTROL_REQUEST_METHOD" => "POST",
        "HTTPS" => "on",
        "HTTP_HOST" => "www.example.com"
      )

      expect(preflight.status).to eq(404)
      expect(preflight.headers.keys.grep(/\Aaccess-control-/i)).to be_empty
    end
  end

  describe "APP_ENV=development でも緩めない" do
    it "Cookie は Secure のまま" do
      with_env("APP_ENV" => "development") do
        post_session

        expect(set_cookie_header_text.downcase).to include("secure")
      end
    end

    it "Origin の検査は同じく完全一致" do
      with_env("APP_ENV" => "development") do
        post_session(headers: { "Origin" => "http://localhost:8080" })

        expect(response).to have_http_status(:forbidden)
      end
    end

    it "新規の発行の頻度は同じに働く（IP あたり 1 分 10 件。11 件目は 429 で、セッションは作らない）" do
      with_env("APP_ENV" => "development") do
        travel_to(now) do
          10.times { issue_session_token }
          post_session
        end

        expect(response).to have_http_status(429)
        expect(error_code).to eq("rate_limited")
        expect(Session.count).to eq(10)
      end
    end

    it "CORS のヘッダーは付けず、開発用の localhost のオリジンのプリフライトにも応答しない" do
      with_env("APP_ENV" => "development") do
        post_session
        expect(response.headers.keys.grep(/\Aaccess-control-/i)).to be_empty

        preflight = Rack::MockRequest.new(Rails.application).options(
          "/api/session",
          "HTTP_ORIGIN" => "http://localhost:8080",
          "HTTP_ACCESS_CONTROL_REQUEST_METHOD" => "POST",
          "HTTPS" => "on",
          "HTTP_HOST" => "www.example.com"
        )

        expect(preflight.status).to eq(404)
        expect(preflight.headers.keys.grep(/\Aaccess-control-/i)).to be_empty
      end
    end
  end
end
