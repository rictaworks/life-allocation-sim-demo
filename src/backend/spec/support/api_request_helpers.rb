# 保存 API の request spec 用の補助。既定では正しい Origin・Content-Type・X-Real-IP を付ける。
module ApiRequestHelpers
  TEST_ORIGIN = "https://www.example.com".freeze
  TEST_CLIENT_IP = "203.0.113.10".freeze
  JSON_CONTENT_TYPE = "application/json".freeze

  def write_headers(overrides = {})
    {
      "Origin" => TEST_ORIGIN,
      "Content-Type" => JSON_CONTENT_TYPE,
      "X-Real-IP" => TEST_CLIENT_IP
    }.merge(overrides).compact
  end

  def post_session(body: "{}", headers: {})
    post "/api/session", params: body, headers: write_headers(headers)
  end

  def json_response
    JSON.parse(response.body)
  end

  def error_code
    json_response.dig("error", "code")
  end

  # Set-Cookie を 1 つの文字列として返す。HTTPS の中間層（force_ssl）を通ると配列になるため、結合してから読む。
  def set_cookie_header_text
    Array(response.headers["Set-Cookie"]).join("\n")
  end

  def token_from_set_cookie
    set_cookie_header_text[/\A#{Regexp.escape(ApiSettings.cookie_name)}=([^;]+)/, 1]
  end

  def cookie_header(token)
    "#{ApiSettings.cookie_name}=#{token}"
  end

  # セッションを 1 つ発行し、Cookie の値を返す。テストの Cookie の入れ物は空に戻す。
  def issue_session_token(ip: TEST_CLIENT_IP)
    post_session(headers: { "X-Real-IP" => ip })
    expect(response).to have_http_status(:created)
    token = token_from_set_cookie
    reset!
    https!
    token
  end

  # 指定したバイト数ちょうどの JSON（未使用のキーで埋める）。
  def padded_json(total_bytes)
    prefix = '{"pad":"'
    suffix = '"}'
    "#{prefix}#{"x" * (total_bytes - prefix.bytesize - suffix.bytesize)}#{suffix}"
  end
end

RSpec.configure do |config|
  config.include ApiRequestHelpers, type: :request
end
