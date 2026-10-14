require "rails_helper"

RSpec.describe "入口の検査（ApiWriteGuard の単体）" do
  let(:reached) { [] }
  let(:inner_app) do
    lambda do |env|
      reached << env["REQUEST_METHOD"]
      [ 200, { "Content-Type" => "text/plain" }, [ "passed" ] ]
    end
  end
  let(:guard) { ApiWriteGuard.new(inner_app) }

  def call_with(method: "POST", origin: ApiRequestHelpers::TEST_ORIGIN, content_type: "application/json", body: "{}")
    options = { method: method, input: body }
    options["HTTP_ORIGIN"] = origin if origin
    options["CONTENT_TYPE"] = content_type if content_type
    guard.call(Rack::MockRequest.env_for("https://www.example.com/api/session", options))
  end

  def error_code_of(body)
    JSON.parse(body.join).dig("error", "code")
  end

  it "Origin が一致し、application/json（charset=utf-8）の POST は内側のアプリに渡す" do
    status, = call_with(content_type: "application/json; charset=utf-8")

    expect(status).to eq(200)
    expect(reached).to eq([ "POST" ])
  end

  it "form・multipart の POST は、内側に渡さず 415 で拒否する（本体は読まない）" do
    [ "application/x-www-form-urlencoded", "multipart/form-data; boundary=x" ].each do |content_type|
      status, headers, body = call_with(content_type: content_type, body: "difficulty=easy")

      expect(status).to eq(415), "Content-Type=#{content_type}"
      expect(headers["Content-Type"]).to start_with("application/json")
      expect(error_code_of(body)).to eq("unsupported_media_type")
    end
    expect(reached).to be_empty
  end

  it "Content-Type の無い POST は 415（Rack は form として解析しうるため）" do
    status, = call_with(content_type: nil, body: "difficulty=easy")

    expect(status).to eq(415)
    expect(reached).to be_empty
  end

  it "Origin が無い・一致しない POST は、Content-Type に関係なく 403 を返す（先に検査する）" do
    [ nil, "https://evil.example" ].each do |origin|
      status, _headers, body = call_with(origin: origin, content_type: "application/x-www-form-urlencoded", body: "a=b")

      expect(status).to eq(403), "Origin=#{origin.inspect}"
      expect(error_code_of(body)).to eq("origin_forbidden")
    end
    expect(reached).to be_empty
  end

  it "Origin は APP_ORIGIN と完全一致で比べる（末尾の /・大文字・scheme の違いは不可）" do
    [ "https://www.example.com/", "https://WWW.example.com", "http://www.example.com" ].each do |origin|
      status, = call_with(origin: origin)

      expect(status).to eq(403), "Origin=#{origin}"
    end
    expect(reached).to be_empty
  end

  it "GET でも form・multipart の本体（Content-Type）は 415 で拒否する" do
    status, = call_with(method: "GET", origin: nil, content_type: "application/x-www-form-urlencoded", body: "a=b")

    expect(status).to eq(415)
    expect(reached).to be_empty
  end

  it "GET で Content-Type の無い要求は内側に渡す（Origin は GET では検査しない）" do
    status, = call_with(method: "GET", origin: nil, content_type: nil, body: "")

    expect(status).to eq(200)
    expect(reached).to eq([ "GET" ])
  end

  it "境界の無い multipart など解析できない Content-Type でも、例外にせず 415 を返す" do
    status, = nil
    expect { status, = call_with(content_type: "multipart/form-data", body: "x") }.not_to raise_error
    expect(status).to eq(415)
  end
end
