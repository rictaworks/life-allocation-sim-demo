require "rails_helper"

RSpec.describe "POST /api/games", type: :request do
  include CoreHostFixtures

  before { https! }

  # 本体は JSON の文字列のまま送る。token を渡すと、Cookie でセッションを示す。
  def post_game(body: '{"difficulty":"normal"}', token: nil, headers: {})
    cookie = token ? { "Cookie" => cookie_header(token) } : {}
    post "/api/games", params: body, headers: write_headers(cookie.merge(headers))
  end

  describe "セッションが必要（401）" do
    it "Cookie が無ければ session_required。ホストは呼ばず、ゲームも作らない" do
      expect(CoreHostClient).not_to receive(:init)

      post_game

      expect(response).to have_http_status(:unauthorized)
      expect(error_code).to eq("session_required")
      expect(Game.count).to eq(0)
    end

    it "期限切れの Cookie も 401" do
      token = travel_to(8.days.ago) { issue_session_token }

      post_game(token: token)

      expect(response).to have_http_status(:unauthorized)
      expect(error_code).to eq("session_required")
    end

    it "一致しない Cookie の値も 401" do
      post_game(token: "x" * 43)

      expect(response).to have_http_status(:unauthorized)
    end
  end

  describe "新規開始（201）" do
    before { allow(CoreHostClient).to receive(:init).and_return(init_output) }

    it "GAME を返す（seed・difficulty・status・current_age・state・turns・evaluation）" do
      token = issue_session_token

      post_game(token: token)

      expect(response).to have_http_status(:created)
      game = json_response.fetch("game")
      expect(game.keys).to contain_exactly("seed", "difficulty", "status", "current_age", "state", "turns", "evaluation")
      expect(game).to include("difficulty" => "normal", "status" => "playing", "current_age" => 22, "turns" => [], "evaluation" => nil)
      expect(game["state"]).to eq(initial_state)
      expect(game["seed"]).to be_between(0, 2**63 - 1)
    end

    it "保存する行は現在のセッションのもので、seed は応答と一致する" do
      token = issue_session_token

      post_game(token: token)

      record = Game.sole
      expect(record.session_id).to eq(Session.find_active(token).id)
      expect(record.seed).to eq(json_response.dig("game", "seed"))
      expect(record).to have_attributes(difficulty: "normal", status: "playing", current_age: 22)
    end

    it "seed はサーバーが生成する（保存要求の seed は使わない）" do
      token = issue_session_token
      allow(SecureRandom).to receive(:random_number).with(2**63).and_return(777)

      post_game(token: token, body: '{"difficulty":"normal","seed":5}')

      expect(json_response.dig("game", "seed")).to eq(777)
      expect(Game.sole.seed).to eq(777)
    end

    it "ホストには難易度だけを渡す（seed は渡さない）" do
      token = issue_session_token

      post_game(token: token)

      expect(CoreHostClient).to have_received(:init).with(difficulty: "normal")
    end
  end

  describe "既存のゲームの上書き" do
    it "旧ゲームと旧履歴を消し、新しいゲームを 1 件だけ残す" do
      token = issue_session_token
      session = Session.find_active(token)
      old = Game.create!(session: session, seed: 1, difficulty: "easy", status: "playing", current_age: 30)
      Turn.create!(game: old, session: session, turn_no: 1, age: 22, living_pct: 60, saving_pct: 20, invest_pct: 20,
                   savings_after: 1, invest_after: 2, satisfaction_after: 3)
      allow(CoreHostClient).to receive(:init).and_return(init_output)

      post_game(token: token)

      expect(response).to have_http_status(:created)
      expect(Game.count).to eq(1)
      expect(Game.exists?(old.id)).to be(false)
      expect(Turn.count).to eq(0)
    end

    it "ホストが難易度を拒否したら、旧ゲームは消さない（422）" do
      token = issue_session_token
      old = Game.create!(session: Session.find_active(token), seed: 1, difficulty: "easy", status: "playing", current_age: 30)
      allow(CoreHostClient).to receive(:init).and_raise(ApiError.new(:difficulty_unknown))

      post_game(token: token, body: '{"difficulty":"nope"}')

      expect(response).to have_http_status(:unprocessable_entity)
      expect(error_code).to eq("difficulty_unknown")
      expect(Game.sole.id).to eq(old.id)
    end

    it "他のセッションのゲームには触れない" do
      other_token = issue_session_token(ip: "198.51.100.77")
      other_game = Game.create!(session: Session.find_active(other_token), seed: 9, difficulty: "hard",
                                status: "playing", current_age: 25)
      allow(CoreHostClient).to receive(:init).and_return(init_output)
      token = issue_session_token

      post_game(token: token)

      expect(response).to have_http_status(:created)
      expect(Game.exists?(other_game.id)).to be(true)
      expect(Game.count).to eq(2)
    end
  end

  describe "難易度の形（ホストを呼ぶ前に 422）" do
    it "文字列でない難易度は difficulty_unknown。ホストは呼ばない" do
      token = issue_session_token
      expect(CoreHostClient).not_to receive(:init)

      post_game(token: token, body: '{"difficulty":5}')

      expect(response).to have_http_status(:unprocessable_entity)
      expect(error_code).to eq("difficulty_unknown")
      expect(Game.count).to eq(0)
    end
  end

  describe "頻度の制限（セッションごと。超過は 429 で、ホストは呼ばない）" do
    let(:start) { Time.utc(2026, 10, 14, 12, 0, 0) }

    before { allow(CoreHostClient).to receive(:init).and_return(init_output) }

    it "1 秒に 1 件まで。2 件目は 429 と Retry-After。ホストは 1 回だけ呼び、ゲームも 1 件のまま" do
      token = travel_to(start) { issue_session_token }

      travel_to(start) do
        post_game(token: token)
        post_game(token: token)
      end

      expect(response).to have_http_status(:too_many_requests)
      expect(error_code).to eq("rate_limited")
      expect(response.headers["Retry-After"]).to eq("1")
      expect(CoreHostClient).to have_received(:init).once
      expect(Game.count).to eq(1)
    end

    it "1 分に 20 件まで。2 秒おきの 20 件は通り、21 件目は 429" do
      token = travel_to(start) { issue_session_token }

      20.times do |index|
        travel_to(start + (2 * index)) { post_game(token: token) }
        expect(response).to have_http_status(:created), "件目=#{index + 1}"
      end

      travel_to(start + 40) { post_game(token: token) }
      expect(response).to have_http_status(:too_many_requests)
      expect(CoreHostClient).to have_received(:init).exactly(20).times
    end

    it "別のセッションは別に数える" do
      first = travel_to(start) { issue_session_token }
      second = travel_to(start) { issue_session_token(ip: "198.51.100.90") }

      travel_to(start) do
        post_game(token: first)
        expect(response).to have_http_status(:created)

        post_game(token: second)
        expect(response).to have_http_status(:created)
      end
    end
  end

  describe "入口の検査（既存の検査が効く）" do
    it "Origin が一致しなければ 403。ホストもゲームも触らない" do
      token = issue_session_token
      expect(CoreHostClient).not_to receive(:init)

      post_game(token: token, headers: { "Origin" => "https://evil.example" })

      expect(response).to have_http_status(:forbidden)
      expect(Game.count).to eq(0)
    end

    it "form の本体は 415" do
      token = issue_session_token

      post_game(token: token, body: "difficulty=normal", headers: { "Content-Type" => "application/x-www-form-urlencoded" })

      expect(response).to have_http_status(:unsupported_media_type)
    end

    it "8192 バイトを超える本体は 413（ホストは呼ばない）" do
      token = issue_session_token
      expect(CoreHostClient).not_to receive(:init)

      post_game(token: token, body: padded_json(8193))

      expect(response).to have_http_status(:payload_too_large)
      expect(error_code).to eq("payload_too_large")
    end

    it "壊れた JSON は 400" do
      token = issue_session_token

      post_game(token: token, body: "{")

      expect(response).to have_http_status(:bad_request)
    end
  end

  describe "ログと応答に出さないもの" do
    it "Cookie・セッション ID・本体の値はログに出さない" do
      token = issue_session_token
      allow(CoreHostClient).to receive(:init).and_raise(ApiError.new(:difficulty_unknown))

      logs = capture_logs do
        post_game(token: token, body: '{"difficulty":"MARKER_GAME_DIFFICULTY"}')
      end

      expect(logs).to include('Started POST "/api/games"')
      expect(logs).not_to include(token)
      expect(logs).not_to include("MARKER_GAME_DIFFICULTY")
    end

    it "エラーの応答は code と message だけ（文言は config/locales/ja.yml）" do
      post_game(body: '{"difficulty":"normal"}')

      expect(json_response).to eq(
        "error" => { "code" => "session_required", "message" => I18n.t("api_errors.session_required", locale: :ja) }
      )
    end
  end
end
