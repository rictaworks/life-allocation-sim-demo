require "rails_helper"

RSpec.describe "POST /api/session の新規発行の制限（IP あたり 1 分 10 件）", type: :request do
  let(:start) { Time.utc(2026, 10, 14, 12, 0, 0) }

  before { https! }

  def forget_cookies!
    reset!
    https!
  end

  it "同じ IP は 1 分に 10 件まで。11 件目は 429 と Retry-After。セッションは作らない" do
    travel_to(start) { 10.times { issue_session_token } }
    expect(Session.count).to eq(10)

    travel_to(start + 1) do
      post_session
      expect(response).to have_http_status(429)
      expect(error_code).to eq("rate_limited")
      expect(response.headers["Retry-After"]).to eq("59")
    end
    expect(Session.count).to eq(10)
  end

  it "1 分経てば再び発行できる（59.9 秒後は不可、60 秒後は可）" do
    travel_to(start) { 10.times { issue_session_token } }

    travel_to(start + 59.9, with_usec: true) do
      post_session
      expect(response).to have_http_status(429)
    end
    travel_to(start + 60) do
      post_session
      expect(response).to have_http_status(201)
    end
  end

  it "別の IP は別に数える" do
    travel_to(start) do
      10.times { issue_session_token }

      # issue_session_token は 201 を確かめてから Cookie を空にする。
      issue_session_token(ip: "198.51.100.7")
    end
  end

  it "X-Forwarded-For を変えても数は変わらない（X-Real-IP だけで数える）" do
    travel_to(start) do
      10.times do |index|
        post_session(headers: { "X-Forwarded-For" => "192.0.2.#{index}" })
        expect(response).to have_http_status(201)
        forget_cookies!
      end

      post_session(headers: { "X-Forwarded-For" => "192.0.2.200" })
      expect(response).to have_http_status(429)
    end
  end

  it "有効な Cookie による 200（既存）は数えない" do
    travel_to(start) do
      token = issue_session_token
      25.times do
        post_session(headers: { "Cookie" => cookie_header(token) })
        expect(response).to have_http_status(:ok)
      end
    end

    expect(RequestEvent.where(kind: "session_issue").count).to eq(1)
  end

  it "X-Real-IP が不正な 400 は数えない（セッションも作らない）" do
    travel_to(start) do
      20.times do
        post_session(headers: { "X-Real-IP" => "bad-address" })
        expect(response).to have_http_status(400)
      end

      10.times { issue_session_token }
    end
    expect(Session.count).to eq(10)
  end

  it "保存するのは IP のダイジェストだけ（IP の文字列は保存しない）" do
    issue_session_token

    row = RequestEvent.sole
    expect(row.kind).to eq("session_issue")
    expect(row.scope_digest).to eq(RequestRateLimit.digest_for(:session_issue, ApiRequestHelpers::TEST_CLIENT_IP))
    expect(RequestEvent.all.flat_map { |record| record.attributes.values.map(&:to_s) })
      .not_to include(ApiRequestHelpers::TEST_CLIENT_IP)
  end
end
