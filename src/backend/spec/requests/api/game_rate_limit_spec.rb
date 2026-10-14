require "rails_helper"

# GET /api/game は再計算ホストを呼んで中身を作るため、セッションごとに頻度を制限する（1 秒 2 件・1 分 60 件）。
# 超過は 429 と Retry-After。超過した要求は、ホストを呼ばない。
RSpec.describe "GET /api/game の頻度の制限（セッションごと。超過は 429。ホストは呼ばない）", type: :request do
  include CoreHostFixtures

  let(:start) { Time.utc(2026, 10, 14, 12, 0, 0) }

  before do
    https!
    allow(CoreHostClient).to receive(:replay).and_return(one_year_replay_output)
  end

  # ゲームを 1 件持つセッションを作り、Cookie の値を返す。
  def token_with_game(ip: ApiRequestHelpers::TEST_CLIENT_IP)
    token = travel_to(start) { issue_session_token(ip: ip) }
    Game.create!(session: Session.find_active(token), seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)
    token
  end

  def get_game(token)
    get "/api/game", headers: { "Cookie" => cookie_header(token) }
  end

  it "1 秒に 2 件まで。3 件目は 429 と Retry-After。ホストは 2 回だけ呼ぶ" do
    token = token_with_game

    travel_to(start) do
      2.times do
        get_game(token)
        expect(response).to have_http_status(:ok)
      end

      get_game(token)
    end

    expect(response).to have_http_status(:too_many_requests)
    expect(error_code).to eq("rate_limited")
    expect(response.headers["Retry-After"]).to eq("1")
    expect(CoreHostClient).to have_received(:replay).twice
  end

  it "0.999 秒後はまだ読めない。ちょうど 1 秒後は読める（境界）" do
    token = token_with_game
    travel_to(start) { 2.times { get_game(token) } }

    travel_to(start + 0.999, with_usec: true) do
      get_game(token)
      expect(response).to have_http_status(:too_many_requests)
    end

    travel_to(start + 1) do
      get_game(token)
      expect(response).to have_http_status(:ok)
    end
  end

  it "1 分に 60 件まで。1 秒おきの 60 件は通り、59.5 秒後の 61 件目は 429（Retry-After は 1）。60 秒後は読める（境界）" do
    token = token_with_game
    60.times do |index|
      travel_to(start + index) do
        get_game(token)
        expect(response).to have_http_status(:ok), "件目=#{index + 1}"
      end
    end

    travel_to(start + 59.5, with_usec: true) do
      get_game(token)
      expect(response).to have_http_status(:too_many_requests)
      expect(response.headers["Retry-After"]).to eq("1")
    end

    travel_to(start + 60) do
      get_game(token)
      expect(response).to have_http_status(:ok)
    end
    expect(CoreHostClient).to have_received(:replay).exactly(61).times
  end

  it "別のセッションは別に数える" do
    first = token_with_game
    second = token_with_game(ip: "198.51.100.90")

    travel_to(start) do
      2.times { get_game(first) }
      get_game(second)
      expect(response).to have_http_status(:ok)
    end
  end

  it "ゲームが無くても、有効なセッションの読み取りは数える（記録は同じ枠）" do
    token = travel_to(start) { issue_session_token }

    travel_to(start) do
      2.times do
        get_game(token)
        expect(json_response).to eq("game" => nil)
      end

      get_game(token)
    end

    expect(response).to have_http_status(:too_many_requests)
    expect(CoreHostClient).not_to have_received(:replay)
  end

  it "Cookie が無い・期限切れの要求は数えない（{game: null}。ホストは呼ばない）" do
    travel_to(start) do
      25.times do
        get "/api/game"
        expect(response).to have_http_status(:ok)
        expect(json_response).to eq("game" => nil)
      end
    end

    expect(RequestEvent.where(kind: "game_read").count).to eq(0)
    expect(CoreHostClient).not_to have_received(:replay)
  end

  it "保存するのは HMAC のダイジェストだけ（Cookie の値は保存しない）" do
    token = token_with_game
    session = Session.find_active(token)

    get_game(token)

    row = RequestEvent.where(kind: "game_read").sole
    expect(row.scope_digest).to eq(RequestRateLimit.digest_for(:game_read, session.id))
    expect(RequestEvent.all.flat_map { |record| record.attributes.values.map(&:to_s) }).not_to include(token)
  end
end
