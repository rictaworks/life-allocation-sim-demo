require "rails_helper"

RSpec.describe "POST /api/games/turns", type: :request do
  include CoreHostFixtures

  before { https! }

  let(:start) { Time.utc(2026, 10, 14, 12, 0, 0) }

  # 再計算ホストの代わり（出力の形は src/core-host/CoreHost/Protocol.md の replay どおり）。
  # 年ごとの値は配分の履歴から決める。規則（範囲・刻み・合計）は見ない（規則は Domain Core の検査で扱う）。
  def fake_replay(history:, **)
    turns = history.each_with_index.map do |allocation, index|
      {
        "turn_no" => index + 1, "age" => 22 + index,
        "living_pct" => allocation.fetch(:living_pct), "saving_pct" => allocation.fetch(:saving_pct),
        "invest_pct" => allocation.fetch(:invest_pct), "event_code" => nil,
        "savings_after" => 1_000 * (index + 1), "invest_after" => 0, "satisfaction_after" => 60
      }
    end
    finished = 22 + history.size >= 65
    state = initial_state.merge(
      "age" => 22 + history.size, "savings_yen" => 1_000 * history.size,
      "status" => finished ? "completed" : "playing"
    )
    { "turns" => turns, "state" => state, "evaluation" => finished ? evaluation_output : nil }
  end

  def post_turn(body:, token:, headers: {})
    cookie = token ? { "Cookie" => cookie_header(token) } : {}
    post "/api/games/turns", params: body, headers: write_headers(cookie.merge(headers))
  end

  # 本体の JSON。website は空（ハニーポットに値を入れない）。
  def turn_body(turn_no:, living: 60, saving: 20, invest: 20, **extra)
    JSON.generate({ turn_no: turn_no, living_pct: living, saving_pct: saving, invest_pct: invest, website: "" }.merge(extra))
  end

  let!(:token) { issue_session_token }
  let!(:game) do
    Game.create!(session: Session.find_active(token), seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)
  end

  before do
    allow(CoreHostClient).to receive(:replay) { |**args| fake_replay(**args) }
  end

  describe "セッションとゲームの確認" do
    it "Cookie が無ければ 401（session_required）。ホストは呼ばず、保存もしない" do
      expect(CoreHostClient).not_to receive(:replay)

      post_turn(body: turn_body(turn_no: 1), token: nil)

      expect(response).to have_http_status(:unauthorized)
      expect(error_code).to eq("session_required")
      expect(Turn.count).to eq(0)
    end

    it "セッションはあるがゲームが無ければ 404（game_not_found）。ホストは呼ばない。他のセッションのゲームも変えない" do
      no_game_token = issue_session_token(ip: "198.51.100.21")
      expect(CoreHostClient).not_to receive(:replay)

      post_turn(body: turn_body(turn_no: 1), token: no_game_token)

      expect(response).to have_http_status(:not_found)
      expect(error_code).to eq("game_not_found")
      expect(Turn.count).to eq(0)
      expect(game.reload).to have_attributes(status: "playing", current_age: 22)
    end
  end

  describe "要求の形（400。ホストは呼ばない）" do
    it "必要なキーが無い・整数でない値（文字列・小数・真偽値・null）は 400（bad_request）" do
      expect(CoreHostClient).not_to receive(:replay)
      bodies = [
        '{"living_pct":60,"saving_pct":20,"invest_pct":20}',
        '{"turn_no":1,"saving_pct":20,"invest_pct":20}',
        '{"turn_no":"1","living_pct":60,"saving_pct":20,"invest_pct":20}',
        '{"turn_no":1,"living_pct":60.0,"saving_pct":20,"invest_pct":20}',
        '{"turn_no":1,"living_pct":60,"saving_pct":20,"invest_pct":true}',
        '{"turn_no":1,"living_pct":null,"saving_pct":20,"invest_pct":20}'
      ]

      bodies.each do |body|
        post_turn(body: body, token: token)

        expect(response).to have_http_status(:bad_request), body
        expect(error_code).to eq("bad_request")
      end
      expect(Turn.count).to eq(0)
    end

    it "配分が 32 ビット整数の範囲を超えるときは 400（ホストには渡さない）" do
      expect(CoreHostClient).not_to receive(:replay)

      post_turn(body: turn_body(turn_no: 1, living: 2**31), token: token)
      expect(response).to have_http_status(:bad_request)

      post_turn(body: turn_body(turn_no: 1, invest: -(2**31) - 1), token: token)
      expect(response).to have_http_status(:bad_request)
    end
  end

  describe "32 ビット整数の範囲の端（400 にせず、ホストに渡す。規則の判定はホストが行う）" do
    it "最大値（2**31 - 1）は、Rails では拒否せずホストに渡す" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1, living: 2**31 - 1), token: token) }

      expect(CoreHostClient).to have_received(:replay).with(
        seed: 12_345, difficulty: "normal", history: [ { living_pct: 2**31 - 1, saving_pct: 20, invest_pct: 20 } ]
      )
      expect(response).to have_http_status(:created)
    end

    it "最小値（-(2**31)）は、Rails では拒否せずホストに渡す" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1, invest: -(2**31)), token: token) }

      expect(CoreHostClient).to have_received(:replay).with(
        seed: 12_345, difficulty: "normal", history: [ { living_pct: 60, saving_pct: 20, invest_pct: -(2**31) } ]
      )
      expect(response).to have_http_status(:created)
    end
  end

  describe "許可リストのキーだけを読む" do
    it "未知のキー（seed・state・年齢・状態・年次の値）は無視し、保存と応答はサーバーの値だけにする" do
      body = turn_body(turn_no: 1, seed: 1, state: { "age" => 60 }, current_age: 60, status: "completed",
                       savings_after: 999_999, memo: "MARKER_UNKNOWN_KEY")

      post_turn(body: body, token: token)

      expect(response).to have_http_status(:created)
      expect(Turn.sole.savings_after).to eq(1_000)
      expect(game.reload).to have_attributes(status: "playing", current_age: 23)
      expect(CoreHostClient).to have_received(:replay).with(
        seed: 12_345, difficulty: "normal", history: [ { living_pct: 60, saving_pct: 20, invest_pct: 20 } ]
      )
    end
  end

  describe "新規の保存（201）" do
    it "1 年目を保存し、TURN と GAME を返す。ターンは 1 行、ゲームの年齢と状態を更新する" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:created)
      expect(response.headers["Set-Cookie"]).to be_nil
      expect(json_response.keys).to contain_exactly("turn", "game")
      expect(json_response.fetch("turn")).to eq(
        "turn_no" => 1, "age" => 22, "living_pct" => 60, "saving_pct" => 20, "invest_pct" => 20,
        "event_code" => nil, "savings_after" => 1_000, "invest_after" => 0, "satisfaction_after" => 60
      )
      expect(json_response.fetch("game")).to include(
        "seed" => 12_345, "difficulty" => "normal", "status" => "playing", "current_age" => 23,
        "state" => fake_replay(history: [ { living_pct: 60, saving_pct: 20, invest_pct: 20 } ])["state"]
      )
      expect(Turn.count).to eq(1)
      expect(Turn.sole).to have_attributes(
        turn_no: 1, age: 22, living_pct: 60, saving_pct: 20, invest_pct: 20, savings_after: 1_000,
        session_id: Session.find_active(token).id
      )
      expect(game.reload).to have_attributes(status: "playing", current_age: 23)
    end

    it "ホストには、ゲームの seed と難易度、保存済みの配分に今回の配分を足した履歴を渡す" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }
      travel_to(start + 2) { post_turn(body: turn_body(turn_no: 2, living: 50, saving: 30, invest: 20), token: token) }

      expect(response).to have_http_status(:created)
      expect(CoreHostClient).to have_received(:replay).with(
        seed: 12_345, difficulty: "normal",
        history: [ { living_pct: 60, saving_pct: 20, invest_pct: 20 }, { living_pct: 50, saving_pct: 30, invest_pct: 20 } ]
      )
      expect(Turn.order(:turn_no).pluck(:turn_no, :age)).to eq([ [ 1, 22 ], [ 2, 23 ] ])
    end

    it "ホストが完走を返したら、ゲームは completed になり、応答に評価を含める" do
      42.times do |index|
        Turn.create!(game: game, session: game.session, turn_no: index + 1, age: 22 + index, living_pct: 60, saving_pct: 20,
                     invest_pct: 20, savings_after: 0, invest_after: 0, satisfaction_after: 60)
      end
      game.update!(current_age: 64)

      travel_to(start) { post_turn(body: turn_body(turn_no: 43), token: token) }

      expect(response).to have_http_status(:created)
      expect(game.reload).to have_attributes(status: "completed", current_age: 65)
      expect(json_response.dig("game", "evaluation")).to eq(evaluation_output)
    end
  end

  describe "保存済みの番号の再送" do
    # 受け入れ条件 3（保存済みの番号の再送は 200）を、4（完走・破綻は 409）より優先する。4 は新しい番号の保存に当てる。
    it "同じ番号・同じ配分は保存せず、保存済みの結果を 200 で返す（1 秒以上あけた再送）" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }
      first_turn = json_response.fetch("turn")

      travel_to(start + 2) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:ok)
      expect(json_response.fetch("turn")).to eq(first_turn)
      expect(json_response.fetch("game").fetch("turns").size).to eq(1)
      expect(Turn.count).to eq(1)
    end

    it "同じ番号でも配分が違えば 409（turn_allocation_mismatch）。保存は変わらない" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

      travel_to(start + 2) do
        post_turn(body: turn_body(turn_no: 1, living: 70, saving: 10, invest: 20), token: token)
      end

      expect(response).to have_http_status(:conflict)
      expect(error_code).to eq("turn_allocation_mismatch")
      expect(Turn.sole).to have_attributes(living_pct: 60, saving_pct: 20, invest_pct: 20)
    end

    # 再送もホストを呼ぶ（GAME を得るため）。頻度は、再送も含めて保存と同じ枠（1 秒 1 件・1 分 20 件）で数える（受け入れ条件 9）。
    it "再送も保存と同じ枠を使う。1 秒以内の再送は 429（Retry-After）で、ホストは呼ばない" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

      travel_to(start + 0.1, with_usec: true) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:too_many_requests)
      expect(error_code).to eq("rate_limited")
      expect(response.headers["Retry-After"]).to eq("1")
      expect(CoreHostClient).to have_received(:replay).once
      expect(Turn.count).to eq(1)
    end

    it "再送を含めて 1 分に 20 件まで。21 件目は 429（Retry-After は最も古い記録が外れるまで）" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }
      expect(response).to have_http_status(:created)

      19.times do |index|
        travel_to(start + (3 * (index + 1))) { post_turn(body: turn_body(turn_no: 1), token: token) }
        expect(response).to have_http_status(:ok), "件目=#{index + 2}"
      end

      travel_to(start + 58) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:too_many_requests)
      expect(response.headers["Retry-After"]).to eq("2")
      expect(CoreHostClient).to have_received(:replay).exactly(20).times
      expect(Turn.count).to eq(1)
    end
  end

  describe "番号と終了の確認（409。ホストは呼ばない）" do
    it "飛び番（次の番号より後）は 409（turn_conflict）" do
      expect(CoreHostClient).not_to receive(:replay)

      travel_to(start) { post_turn(body: turn_body(turn_no: 3), token: token) }

      expect(response).to have_http_status(:conflict)
      expect(error_code).to eq("turn_conflict")
      expect(Turn.count).to eq(0)
    end

    it "範囲外（0・負の数・とても大きい数）は 409（turn_conflict）" do
      expect(CoreHostClient).not_to receive(:replay)

      [ 0, -1, 2**40 ].each do |turn_no|
        post_turn(body: turn_body(turn_no: turn_no), token: token)

        expect(response).to have_http_status(:conflict), "turn_no=#{turn_no}"
        expect(error_code).to eq("turn_conflict")
      end
      expect(Turn.count).to eq(0)
    end

    it "完走・破綻したゲームへの新しいターンは 409（game_finished）。ホストは呼ばない" do
      game.update!(status: "bankrupt")
      expect(CoreHostClient).not_to receive(:replay)

      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:conflict)
      expect(error_code).to eq("game_finished")
      expect(Turn.count).to eq(0)
    end

    # 受け入れ条件 4（完走・破綻は 409）は新しい番号の保存に当てる。保存済みの番号の再送は、受け入れ条件 3 に従い 200 にする（優先の理由は PR 本文）。
    it "完走・破綻したゲームでも、保存済みの番号を同じ配分で再送すると 200（保存済みの結果）" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }
      game.update!(status: "completed")

      travel_to(start + 2) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:ok)
      expect(Turn.count).to eq(1)
    end

    # 最後のターンで完走したあと、応答が届かなかった再試行。保存済みの結果（評価を含む GAME）を返す。
    it "完走で保存された最後の番号を再送すると（応答を失った再試行）、200 で評価を含む GAME を返す" do
      42.times do |index|
        Turn.create!(game: game, session: game.session, turn_no: index + 1, age: 22 + index, living_pct: 60, saving_pct: 20,
                     invest_pct: 20, savings_after: 0, invest_after: 0, satisfaction_after: 60)
      end
      game.update!(current_age: 64)
      travel_to(start) { post_turn(body: turn_body(turn_no: 43), token: token) }
      expect(response).to have_http_status(:created)

      travel_to(start + 2) { post_turn(body: turn_body(turn_no: 43), token: token) }

      expect(response).to have_http_status(:ok)
      expect(json_response.fetch("turn")).to include("turn_no" => 43, "age" => 64)
      expect(json_response.fetch("game")).to include("status" => "completed", "current_age" => 65)
      expect(json_response.dig("game", "evaluation")).to eq(evaluation_output)
      expect(Turn.count).to eq(43)
    end
  end

  describe "規則違反（ホストの理由コードを 422 にする）" do
    %w[allocation_negative allocation_step_mismatch allocation_sum_mismatch].each do |code|
      it "#{code} は 422 で、保存しない" do
        allow(CoreHostClient).to receive(:replay).and_raise(ApiError.new(code.to_sym))

        travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

        expect(response).to have_http_status(:unprocessable_entity)
        expect(error_code).to eq(code)
        expect(Turn.count).to eq(0)
        expect(game.reload).to have_attributes(status: "playing", current_age: 22)
      end
    end
  end

  describe "同じ番号の同時の要求（一意制約の衝突）" do
    # 別の要求が、この要求の再計算の間に同じ番号を先に保存した順番を再現する（同時実行の代わりに決まった順で行う）。
    def competing_save_during_replay(living:, saving:, invest:)
      calls = 0
      allow(CoreHostClient).to receive(:replay) do |**args|
        calls += 1
        if calls == 1
          Turn.create!(game: game, session: game.session, turn_no: 1, age: 22, living_pct: living, saving_pct: saving,
                       invest_pct: invest, savings_after: 1_000, invest_after: 0, satisfaction_after: 60)
        end
        fake_replay(**args)
      end
    end

    it "先に保存された同じ配分の結果を 200 で返し、保存は 1 件のまま" do
      competing_save_during_replay(living: 60, saving: 20, invest: 20)

      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:ok)
      expect(Turn.where(turn_no: 1).count).to eq(1)
      expect(json_response.fetch("turn")).to include("turn_no" => 1, "living_pct" => 60)
    end

    it "先に保存された配分が違えば 409（turn_allocation_mismatch）。保存は 1 件のまま" do
      competing_save_during_replay(living: 70, saving: 10, invest: 20)

      travel_to(start) { post_turn(body: turn_body(turn_no: 1), token: token) }

      expect(response).to have_http_status(:conflict)
      expect(error_code).to eq("turn_allocation_mismatch")
      expect(Turn.sole).to have_attributes(living_pct: 70)
    end
  end

  describe "ハニーポット（website に値があれば保存しない。応答は通常どおり）" do
    it "website に値があれば、通常と同じ 201 の形で返すが、保存はしない" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1, website: "http://spam.example"), token: token) }

      expect(response).to have_http_status(:created)
      expect(json_response.keys).to contain_exactly("turn", "game")
      expect(json_response.fetch("turn")).to include("turn_no" => 1, "living_pct" => 60)
      expect(Turn.count).to eq(0)
      expect(game.reload).to have_attributes(status: "playing", current_age: 22)
    end

    it "website が空文字列なら、通常どおり保存する" do
      travel_to(start) { post_turn(body: turn_body(turn_no: 1, website: ""), token: token) }

      expect(response).to have_http_status(:created)
      expect(Turn.count).to eq(1)
    end
  end

  describe "頻度の制限（セッションごと。超過は 429。ホストは呼ばない）" do
    it "1 秒に 1 件まで。2 件目は 429 と Retry-After。保存は 1 件、ホストは 1 回だけ" do
      travel_to(start) do
        post_turn(body: turn_body(turn_no: 1), token: token)
        expect(response).to have_http_status(:created)

        post_turn(body: turn_body(turn_no: 2), token: token)
      end

      expect(response).to have_http_status(:too_many_requests)
      expect(error_code).to eq("rate_limited")
      expect(response.headers["Retry-After"]).to eq("1")
      expect(CoreHostClient).to have_received(:replay).once
      expect(Turn.count).to eq(1)
    end

    it "1 分に 20 件まで。21 件目は 429（Retry-After は最も古い記録が外れるまで）" do
      20.times do |index|
        travel_to(start + (3 * index)) { post_turn(body: turn_body(turn_no: index + 1), token: token) }
        expect(response).to have_http_status(:created), "件目=#{index + 1}"
      end

      travel_to(start + 58) { post_turn(body: turn_body(turn_no: 21), token: token) }

      expect(response).to have_http_status(:too_many_requests)
      expect(response.headers["Retry-After"]).to eq("2")
      expect(Turn.count).to eq(20)
    end

    it "別のセッションは別に数える" do
      other_token = issue_session_token(ip: "198.51.100.23")
      Game.create!(session: Session.find_active(other_token), seed: 9, difficulty: "normal", status: "playing", current_age: 22)

      travel_to(start) do
        post_turn(body: turn_body(turn_no: 1), token: token)
        expect(response).to have_http_status(:created)

        post_turn(body: turn_body(turn_no: 1), token: other_token)
        expect(response).to have_http_status(:created)
      end
      expect(Turn.count).to eq(2)
    end
  end

  describe "入口の検査（既存の検査が効く）" do
    it "Origin が一致しなければ 403。保存もホストもしない" do
      expect(CoreHostClient).not_to receive(:replay)

      post_turn(body: turn_body(turn_no: 1), token: token, headers: { "Origin" => "https://evil.example" })

      expect(response).to have_http_status(:forbidden)
      expect(Turn.count).to eq(0)
    end

    it "form の本体は 415" do
      post_turn(body: "turn_no=1", token: token, headers: { "Content-Type" => "application/x-www-form-urlencoded" })

      expect(response).to have_http_status(:unsupported_media_type)
    end

    it "8192 バイトを超える本体は 413（ホストは呼ばない）" do
      expect(CoreHostClient).not_to receive(:replay)

      post_turn(body: padded_json(8193), token: token)

      expect(response).to have_http_status(:payload_too_large)
      expect(error_code).to eq("payload_too_large")
    end

    it "壊れた JSON は 400" do
      post_turn(body: "{", token: token)

      expect(response).to have_http_status(:bad_request)
    end
  end

  describe "ログと応答に出さないもの" do
    it "Cookie・セッション ID・本体の値はログに出さない" do
      allow(CoreHostClient).to receive(:replay).and_raise(ApiError.new(:allocation_step_mismatch))

      logs = capture_logs do
        post_turn(body: turn_body(turn_no: 1, memo: "MARKER_TURN_BODY"), token: token)
      end

      expect(response).to have_http_status(:unprocessable_entity)
      expect(logs).to include('Started POST "/api/games/turns"')
      expect(logs).not_to include(token)
      expect(logs).not_to include("MARKER_TURN_BODY")
    end

    it "エラーの応答は code と message だけ（文言は config/locales/ja.yml）" do
      post_turn(body: turn_body(turn_no: 1), token: nil)

      expect(json_response).to eq(
        "error" => { "code" => "session_required", "message" => I18n.t("api_errors.session_required", locale: :ja) }
      )
    end
  end
end
