require "rails_helper"

# 実際の再計算ホスト（dotnet CoreHost.dll）を呼ぶ統合テスト（3 件に限る）。
# 事前に dotnet build src/core-host/CoreHost -c Release を実行し、CORE_HOST_DLL がそのファイルを指していること。
RSpec.describe "再計算ホストの実物との統合（POST /api/games・GET /api/game）", type: :request do
  include CoreHostFixtures

  before do
    https!
    dll = ENV.fetch("CORE_HOST_DLL")
    raise "再計算ホストが見つかりません（#{dll}）。dotnet build src/core-host/CoreHost -c Release を実行してください" unless File.file?(dll)
  end

  it "POST の初期状態（実物の init）と、GET の再計算（実物の replay）が一致する" do
    token = issue_session_token
    headers = write_headers("Cookie" => cookie_header(token))

    post "/api/games", params: '{"difficulty":"normal"}', headers: headers
    expect(response).to have_http_status(:created)
    created = json_response.fetch("game")
    expect(created.fetch("state")).to eq(initial_state)

    get "/api/game", headers: { "Cookie" => cookie_header(token) }
    expect(response).to have_http_status(:ok)
    expect(json_response.fetch("game")).to eq(created)
  end

  it "保存された履歴は、実物の replay で再計算される（保存された年次の値は使わない）" do
    token = issue_session_token
    session = Session.find_active(token)
    game = Game.create!(session: session, seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)
    Turn.create!(game: game, session: session, turn_no: 1, age: 22, living_pct: 60, saving_pct: 20, invest_pct: 20,
                 savings_after: 0, invest_after: 0, satisfaction_after: 0)

    get "/api/game", headers: { "Cookie" => cookie_header(token) }

    fetched = json_response.fetch("game")
    expect(fetched.fetch("turns")).to eq(one_year_replay_output["turns"])
    expect(fetched.fetch("state")).to eq(one_year_replay_output["state"])
    expect(fetched.fetch("current_age")).to eq(23)
  end

  it "不明な難易度と未指定は、実物の理由コードで 422 になり、旧ゲームは残る" do
    token = issue_session_token
    session = Session.find_active(token)
    old = Game.create!(session: session, seed: 1, difficulty: "easy", status: "playing", current_age: 30)
    headers = write_headers("Cookie" => cookie_header(token))

    post "/api/games", params: '{"difficulty":"no-such-difficulty"}', headers: headers
    expect(response).to have_http_status(:unprocessable_entity)
    expect(error_code).to eq("difficulty_unknown")

    # 1 秒に 1 件の制限（セッションごと）を避けるため、2 件目は 3 秒後に送る
    travel_to(3.seconds.from_now) { post "/api/games", params: "{}", headers: headers }
    expect(response).to have_http_status(:unprocessable_entity)
    expect(error_code).to eq("difficulty_missing")

    expect(Game.sole.id).to eq(old.id)
  end
end
