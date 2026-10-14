require "rails_helper"

# 実際の再計算ホスト（dotnet CoreHost.dll）を呼ぶ統合テスト（POST /api/games/turns の 2 件に限る）。
# 事前に dotnet build src/core-host/CoreHost -c Release を実行し、CORE_HOST_DLL がそのファイルを指していること。
RSpec.describe "再計算ホストの実物との統合（POST /api/games/turns）", type: :request do
  include CoreHostFixtures

  before do
    https!
    dll = ENV.fetch("CORE_HOST_DLL")
    raise "再計算ホストが見つかりません（#{dll}）。dotnet build src/core-host/CoreHost -c Release を実行してください" unless File.file?(dll)
  end

  def post_turn_json(token, body)
    post "/api/games/turns", params: body, headers: write_headers("Cookie" => cookie_header(token))
  end

  it "1 年目の保存は、実物の replay の結果（TURN と GAME の状態）と一致する" do
    token = issue_session_token
    Game.create!(session: Session.find_active(token), seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)

    post_turn_json(token, '{"turn_no":1,"living_pct":60,"saving_pct":20,"invest_pct":20,"website":""}')

    expect(response).to have_http_status(:created)
    expect(json_response.fetch("turn")).to eq(one_year_replay_output["turns"].first)
    expect(json_response.fetch("game")).to include(
      "state" => one_year_replay_output["state"], "current_age" => 23, "turns" => one_year_replay_output["turns"]
    )
    expect(Turn.sole.savings_after).to eq(240_560)
  end

  it "実物の規則違反（合計が 100 でない）は 422（allocation_sum_mismatch）になり、保存しない" do
    token = issue_session_token
    Game.create!(session: Session.find_active(token), seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)

    post_turn_json(token, '{"turn_no":1,"living_pct":60,"saving_pct":20,"invest_pct":30,"website":""}')

    expect(response).to have_http_status(:unprocessable_entity)
    expect(error_code).to eq("allocation_sum_mismatch")
    expect(Turn.count).to eq(0)
  end
end
