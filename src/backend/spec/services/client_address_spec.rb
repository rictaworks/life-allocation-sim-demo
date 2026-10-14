require "rails_helper"

RSpec.describe "ClientAddress（X-Real-IP だけから接続元を取る）" do
  def request_with_headers(headers)
    env = Rack::MockRequest.env_for("/api/session", method: "POST")
    headers.each { |name, value| env["HTTP_#{name.upcase.tr("-", "_")}"] = value }
    ActionDispatch::Request.new(env)
  end

  it "IPv4・IPv6 を正規化した文字列で返す" do
    expect(ClientAddress.parse!("203.0.113.10")).to eq("203.0.113.10")
    expect(ClientAddress.parse!("2001:DB8::1")).to eq("2001:db8::1")
    expect(ClientAddress.parse!("::ffff:203.0.113.10")).to eq("::ffff:203.0.113.10")
  end

  it "形式が不正なら bad_request（CIDR・ポート・複数の値・範囲外・ゾーン指定を含む）" do
    invalid = [ "", "abc", "203.0.113.10/32", "203.0.113.10:8080", "203.0.113.256",
               "203.0.113.10, 198.51.100.2", "fe80::1%eth0", "1234", "a" * 46, nil, 42 ]
    invalid.each do |value|
      expect { ClientAddress.parse!(value) }.to raise_error(ApiError) { |error|
        expect(error.code).to eq(:bad_request)
      }, "value=#{value.inspect}"
    end
  end

  it "X-Real-IP だけを読む（X-Forwarded-For は見ない）" do
    request = request_with_headers("X-Real-IP" => "203.0.113.10", "X-Forwarded-For" => "198.51.100.9")

    expect(ClientAddress.from_request!(request)).to eq("203.0.113.10")
  end

  it "X-Real-IP が無ければ bad_request（X-Forwarded-For があっても）" do
    request = request_with_headers("X-Forwarded-For" => "198.51.100.9")

    expect { ClientAddress.from_request!(request) }.to raise_error(ApiError) { |error|
      expect(error.code).to eq(:bad_request)
    }
  end
end
