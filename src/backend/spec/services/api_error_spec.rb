require "rails_helper"

RSpec.describe "ApiError（エラー応答の共通形式）" do
  it "すべてのコードに config/locales/ja.yml の文言があり、形式は {error: {code, message}}" do
    ApiError::STATUSES.each_key do |code|
      body = ApiError.new(code).body

      expect(body.keys).to eq([ :error ])
      expect(body[:error].keys).to contain_exactly(:code, :message)
      expect(body[:error][:code]).to eq(code.to_s)
      expect(body[:error][:message]).to eq(I18n.t("api_errors.#{code}", locale: :ja, raise: true))
    end
  end

  it "コードごとの HTTP ステータスは定義どおり" do
    expect(ApiError.new(:bad_request).status).to eq(400)
    expect(ApiError.new(:origin_forbidden).status).to eq(403)
    expect(ApiError.new(:not_found).status).to eq(404)
    expect(ApiError.new(:payload_too_large).status).to eq(413)
    expect(ApiError.new(:unsupported_media_type).status).to eq(415)
    expect(ApiError.new(:rate_limited).status).to eq(429)
    expect(ApiError.new(:internal_error).status).to eq(500)
    expect(ApiError.new(:session_required).status).to eq(401)
    expect(ApiError.new(:difficulty_missing).status).to eq(422)
    expect(ApiError.new(:difficulty_unknown).status).to eq(422)
    expect(ApiError.new(:allocation_negative).status).to eq(422)
    expect(ApiError.new(:allocation_step_mismatch).status).to eq(422)
    expect(ApiError.new(:allocation_sum_mismatch).status).to eq(422)
    expect(ApiError.new(:game_finished).status).to eq(409)
    expect(ApiError.new(:game_not_found).status).to eq(404)
    expect(ApiError.new(:turn_conflict).status).to eq(409)
    expect(ApiError.new(:turn_allocation_mismatch).status).to eq(409)
    expect(ApiError.new(:service_unavailable).status).to eq(503)
  end

  it "定義されていないコードは例外にする（既定値で補わない）" do
    expect { ApiError.new(:unknown_code) }.to raise_error(ArgumentError)
  end

  it "Rack の応答は、ステータス・JSON の Content-Type・本文（{error: {code, message}}）を持つ" do
    status, headers, parts = ApiError.new(:origin_forbidden).to_rack_response

    expect(status).to eq(403)
    expect(headers["Content-Type"]).to eq("application/json; charset=utf-8")
    expect(JSON.parse(parts.join)).to eq(
      "error" => { "code" => "origin_forbidden", "message" => I18n.t("api_errors.origin_forbidden", locale: :ja) }
    )
  end

  it "429 の Retry-After は Rack の応答のヘッダーに入る" do
    _status, headers, = ApiError.new(:rate_limited, retry_after: 59).to_rack_response

    expect(headers["Retry-After"]).to eq("59")
  end
end
