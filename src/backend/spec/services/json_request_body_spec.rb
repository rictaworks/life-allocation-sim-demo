require "rails_helper"

RSpec.describe "JsonRequestBody（JSON の本体を読む）" do
  def request_for(body:, content_type: "application/json", content_length: nil)
    env = Rack::MockRequest.env_for("/api/session", method: "POST", input: body, "CONTENT_TYPE" => content_type)
    env["CONTENT_LENGTH"] = content_length if content_length
    ActionDispatch::Request.new(env)
  end

  describe ".read!" do
    it "JSON のオブジェクトを返す" do
      expect(JsonRequestBody.read!(request_for(body: '{"a":1}'))).to eq("a" => 1)
    end

    it "Content-Type が合わなければ 415" do
      expect { JsonRequestBody.read!(request_for(body: "{}", content_type: "text/plain")) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:unsupported_media_type) }
    end

    it "8193 バイトの実際の本体は 413" do
      expect { JsonRequestBody.read!(request_for(body: "x" * 8193)) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:payload_too_large) }
    end

    it "Content-Length の申告が小さくても、実際に読んだ長さが上限を超えれば 413" do
      request = request_for(body: "x" * 9000, content_length: "10")

      expect { JsonRequestBody.read!(request) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:payload_too_large) }
    end

    it "Content-Length の申告が上限を超えていれば、本体を読まずに 413" do
      request = request_for(body: "{}", content_length: "8193")

      expect { JsonRequestBody.read!(request) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:payload_too_large) }
    end

    it "Content-Length が数値でなければ 400" do
      request = request_for(body: "{}", content_length: "abc")

      expect { JsonRequestBody.read!(request) }
        .to raise_error(ApiError) { |error| expect(error.code).to eq(:bad_request) }
    end
  end

  describe ".permit" do
    it "許可リストのキーだけを返し、未知のキーは無視する" do
      body = { "difficulty" => "easy", "unknown" => 1, "turn_no" => 2 }

      expect(JsonRequestBody.permit(body, %w[difficulty turn_no])).to eq("difficulty" => "easy", "turn_no" => 2)
    end

    it "許可リストが空なら何も返さない" do
      expect(JsonRequestBody.permit({ "a" => 1 }, [])).to eq({})
    end
  end

  describe ".json_content_type?" do
    it "application/json と charset=utf-8 だけを受け付ける" do
      accepted = [ "application/json", "application/json; charset=utf-8", "APPLICATION/JSON;charset=\"UTF-8\"" ]
      rejected = [ nil, "", "application/json; charset=ascii", "application/json;", "application/json; a=b", "text/json" ]

      accepted.each { |value| expect(JsonRequestBody.json_content_type?(value)).to be(true), value.inspect }
      rejected.each { |value| expect(JsonRequestBody.json_content_type?(value)).to be(false), value.inspect }
    end
  end
end
