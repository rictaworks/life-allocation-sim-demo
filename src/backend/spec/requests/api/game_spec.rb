require "rails_helper"

RSpec.describe "GET /api/game", type: :request do
  include CoreHostFixtures

  before { https! }

  def get_game(token: nil)
    get "/api/game", headers: token ? { "Cookie" => cookie_header(token) } : {}
  end

  describe "ゲームが無いとき（200 で null）" do
    it "Cookie が無ければ {game: null}" do
      get_game

      expect(response).to have_http_status(:ok)
      expect(json_response).to eq("game" => nil)
    end

    it "期限切れ・一致しない Cookie も {game: null}" do
      token = travel_to(8.days.ago) { issue_session_token }

      get_game(token: token)
      expect(json_response).to eq("game" => nil)

      get_game(token: "x" * 43)
      expect(json_response).to eq("game" => nil)
    end

    it "セッションはあるがゲームが無ければ {game: null}" do
      token = issue_session_token

      get_game(token: token)

      expect(response).to have_http_status(:ok)
      expect(json_response).to eq("game" => nil)
    end
  end

  describe "ゲームがあるとき" do
    let!(:token) { issue_session_token }
    let(:session) { Session.find_active(token) }
    let!(:game) { Game.create!(session: session, seed: 12_345, difficulty: "normal", status: "playing", current_age: 22) }

    before do
      Turn.create!(game: game, session: session, turn_no: 1, age: 22, living_pct: 60, saving_pct: 20, invest_pct: 20,
                   savings_after: 0, invest_after: 0, satisfaction_after: 0)
      allow(CoreHostClient).to receive(:replay).and_return(one_year_replay_output)
    end

    it "再計算ホストの結果で GAME を返す（状態・履歴・評価はホストの出力どおり）" do
      get_game(token: token)

      expect(response).to have_http_status(:ok)
      expect(json_response.fetch("game")).to eq(
        "seed" => 12_345, "difficulty" => "normal", "status" => "playing", "current_age" => 23,
        "state" => one_year_replay_output["state"], "turns" => one_year_replay_output["turns"], "evaluation" => nil
      )
    end

    it "再計算には、保存された seed・難易度と、配分履歴を turn_no の順で渡す" do
      Turn.create!(game: game, session: session, turn_no: 3, age: 24, living_pct: 50, saving_pct: 30, invest_pct: 20,
                   savings_after: 0, invest_after: 0, satisfaction_after: 0)
      Turn.create!(game: game, session: session, turn_no: 2, age: 23, living_pct: 70, saving_pct: 10, invest_pct: 20,
                   savings_after: 0, invest_after: 0, satisfaction_after: 0)

      get_game(token: token)

      expect(CoreHostClient).to have_received(:replay).with(
        seed: 12_345,
        difficulty: "normal",
        history: [
          { living_pct: 60, saving_pct: 20, invest_pct: 20 },
          { living_pct: 70, saving_pct: 10, invest_pct: 20 },
          { living_pct: 50, saving_pct: 30, invest_pct: 20 }
        ]
      )
    end

    it "状態は変えない（ゲームの行もターンの行も更新日時も変わらず、Cookie も発行しない）" do
      before_rows = [ Game.pluck(:id, :status, :current_age, :updated_at), Turn.count, Session.count ]

      get_game(token: token)

      expect([ Game.pluck(:id, :status, :current_age, :updated_at), Turn.count, Session.count ]).to eq(before_rows)
      expect(response.headers["Set-Cookie"]).to be_nil
    end

    it "他のセッションからは見えない（{game: null}。404 にしない）" do
      other_token = issue_session_token(ip: "198.51.100.88")
      allow(CoreHostClient).to receive(:replay)

      get_game(token: other_token)

      expect(json_response).to eq("game" => nil)
      expect(CoreHostClient).not_to have_received(:replay)
    end

    it "ホストが応答できなければ、そのエラー（503）を JSON で返す" do
      allow(CoreHostClient).to receive(:replay).and_raise(ApiError.new(:service_unavailable))

      get_game(token: token)

      expect(response).to have_http_status(:service_unavailable)
      expect(error_code).to eq("service_unavailable")
    end
  end
end
