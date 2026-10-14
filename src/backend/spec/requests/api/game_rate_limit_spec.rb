require "rails_helper"

# GET /api/game は状態を変えない読み取りのため、頻度を数えない（429 にならず、request_events にも残さない）。
# 再計算ホストの負荷は、同時実行の上限と制限時間で守る（config/core_host.yml）。
# GET を何度送っても、POST の頻度の制限（ゲームの開始・保存・新規セッション）は従来どおり働く。
RSpec.describe "GET /api/game は頻度を数えない（429 にならず、記録も残さない）", type: :request do
  include CoreHostFixtures

  let(:start) { Time.utc(2026, 10, 14, 12, 0, 0) }

  before do
    https!
    allow(CoreHostClient).to receive(:init).and_return(init_output)
    allow(CoreHostClient).to receive(:replay).and_return(one_year_replay_output)
  end

  # ゲームを 1 件持つセッションを作り、Cookie の値を返す（配分の履歴は空）。
  def token_with_game(ip: ApiRequestHelpers::TEST_CLIENT_IP)
    token = travel_to(start) { issue_session_token(ip: ip) }
    Game.create!(session: Session.find_active(token), seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)
    token
  end

  def get_game(token = nil)
    get "/api/game", headers: token ? { "Cookie" => cookie_header(token) } : {}
  end

  def post_game(token)
    post "/api/games", params: '{"difficulty":"normal"}', headers: write_headers("Cookie" => cookie_header(token))
  end

  def post_turn(token, turn_no)
    body = JSON.generate({ turn_no: turn_no, living_pct: 60, saving_pct: 20, invest_pct: 20, website: "" })
    post "/api/games/turns", params: body, headers: write_headers("Cookie" => cookie_header(token))
  end

  describe "読み取り（GET）は数えない" do
    it "1 秒に 5 件読んでも、どれも 200（429 にならない）。ホストは読んだ件数だけ呼ぶ" do
      token = token_with_game

      travel_to(start) do
        5.times do
          get_game(token)
          expect(response).to have_http_status(:ok)
        end
      end

      expect(CoreHostClient).to have_received(:replay).exactly(5).times
    end

    it "1 分に 61 件読んでも、どれも 200（上限は設けない）" do
      token = token_with_game

      travel_to(start) do
        61.times do |index|
          get_game(token)
          expect(response).to have_http_status(:ok), "件目=#{index + 1}"
        end
      end
    end

    it "読み取りでは request_events に 1 行も書かない（ゲームがあっても、無くても）" do
      token = token_with_game
      empty_token = travel_to(start) { issue_session_token(ip: "198.51.100.90") }

      expect do
        travel_to(start) do
          3.times { get_game(token) }
          3.times { get_game(empty_token) }
        end
      end.not_to change(RequestEvent, :count)
    end

    it "ゲームが無くても、有効なセッションの読み取りは 429 にならず、{game: null} を返す" do
      token = travel_to(start) { issue_session_token(ip: "198.51.100.91") }

      travel_to(start) do
        5.times do
          get_game(token)
          expect(response).to have_http_status(:ok)
          expect(json_response).to eq("game" => nil)
        end
      end

      expect(CoreHostClient).not_to have_received(:replay)
    end

    it "Cookie が無い・期限切れの読み取りも数えない（{game: null}。ホストは呼ばない）" do
      expired = travel_to(8.days.ago) { issue_session_token(ip: "198.51.100.92") }

      travel_to(start) do
        5.times do
          get_game
          expect(json_response).to eq("game" => nil)
          get_game(expired)
          expect(json_response).to eq("game" => nil)
        end
      end

      expect(CoreHostClient).not_to have_received(:replay)
    end
  end

  describe "POST の頻度の制限は、読み取りがあっても従来どおり働く" do
    it "ゲームの開始は 1 秒 1 件のまま（GET は枠を使わず、2 件目は 429）" do
      token = travel_to(start) { issue_session_token }

      travel_to(start) do
        5.times { get_game(token) }
        post_game(token)
        expect(response).to have_http_status(:created)

        5.times { get_game(token) }
        post_game(token)
        expect(response).to have_http_status(:too_many_requests)
        expect(error_code).to eq("rate_limited")
      end

      expect(CoreHostClient).to have_received(:init).once
    end

    it "保存は 1 秒 1 件のまま（GET は枠を使わず、2 件目は 429。保存は 1 件だけ）" do
      token = token_with_game

      travel_to(start) do
        5.times { get_game(token) }
        post_turn(token, 1)
        expect(response).to have_http_status(:created)

        5.times { get_game(token) }
        post_turn(token, 1)
        expect(response).to have_http_status(:too_many_requests)
        expect(error_code).to eq("rate_limited")
      end

      expect(Turn.count).to eq(1)
    end

    it "新規の発行は IP あたり 1 分 10 件のまま（読み取りは数に入らない。11 件目は 429）" do
      travel_to(start) do
        token = issue_session_token
        20.times { get_game(token) }

        9.times { issue_session_token }
        post_session
        expect(response).to have_http_status(:too_many_requests)
        expect(error_code).to eq("rate_limited")
      end
    end
  end
end
