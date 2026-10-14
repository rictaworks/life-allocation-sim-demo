require "rails_helper"

# 同じ番号の 2 件を、実際に並行に送る（別々のスレッド・別々の DB 接続・別々の要求の入れ物）。
# 別の接続で保存した行は、テストの transaction（use_transactional_fixtures）の中からは見えないため、
# このファイルだけ transaction を使わず、終わったあとに行を消す（テストの DB の中だけ）。
RSpec.describe "POST /api/games/turns の同時の要求（実際に並行に送る）", type: :request do
  include CoreHostFixtures

  self.use_transactional_tests = false

  before do
    allow(CoreHostClient).to receive(:replay) { |**args| fake_replay(**args) }
  end

  after do
    # 子の行から順に消す。別の接続で残した行をそろえて戻す（ほかのテストに残さない）。
    Turn.delete_all
    Game.delete_all
    Session.delete_all
    RequestEvent.delete_all
  end

  # 再計算ホストの代わり（replay の形どおり）。年の値は配分の履歴から決める（規則は見ない。規則は Domain Core の検査で扱う）。
  def fake_replay(history:, **)
    turns = history.each_with_index.map do |allocation, index|
      {
        "turn_no" => index + 1, "age" => 22 + index,
        "living_pct" => allocation.fetch(:living_pct), "saving_pct" => allocation.fetch(:saving_pct),
        "invest_pct" => allocation.fetch(:invest_pct), "event_code" => nil,
        "savings_after" => 1_000 * (index + 1), "invest_after" => 0, "satisfaction_after" => 60
      }
    end
    state = initial_state.merge("age" => 22 + history.size, "savings_yen" => 1_000 * history.size, "status" => "playing")
    { "turns" => turns, "state" => state, "evaluation" => nil }
  end

  # 2 件がそろうまで、再計算の中で待つ。どちらも「まだ保存されていない」と見てから、保存を競わせる。
  # 3 件目以降（保存済みの結果を返すときの GAME の再計算）は待たない。
  def stub_replay_with_barrier
    barrier = Concurrent::CyclicBarrier.new(2)
    arrivals = Concurrent::AtomicFixnum.new(0)
    allow(CoreHostClient).to receive(:replay) do |**args|
      if arrivals.increment <= 2
        raise "同時の要求がそろいませんでした" unless barrier.wait(5)
      end
      fake_replay(**args)
    end
  end

  def start_game_for_new_session
    token = issue_session_token
    Game.create!(session: Session.find_active(token), seed: 12_345, difficulty: "normal", status: "playing", current_age: 22)
    token
  end

  # 本体の JSON。website は空（ハニーポットに値を入れない）。
  def turn_body(turn_no:, living: 60, saving: 20, invest: 20)
    JSON.generate({ turn_no: turn_no, living_pct: living, saving_pct: saving, invest_pct: invest, website: "" })
  end

  # 各要求を別のスレッドと別の接続で送り、[ステータス, 本体] の配列を、本体の順に返す。
  def send_concurrently(token, bodies)
    threads = bodies.map do |body|
      Thread.new do
        ActiveRecord::Base.connection_pool.with_connection do
          client = ActionDispatch::Integration::Session.new(Rails.application)
          client.https!
          client.post("/api/games/turns", params: body, headers: write_headers("Cookie" => cookie_header(token)))
          [ client.response.status, JSON.parse(client.response.body) ]
        end
      end
    end
    threads.map(&:value)
  end

  describe "同じ番号・同じ配分を 2 件同時に送る" do
    it "頻度の制限を外して一意制約だけを見ても、保存は 1 件のまま（1 件は 201、もう 1 件は保存済みの結果の 200）" do
      token = start_game_for_new_session
      allow(RequestRateLimit).to receive(:consume!)
      stub_replay_with_barrier

      results = send_concurrently(token, [ turn_body(turn_no: 1), turn_body(turn_no: 1) ])

      expect(results.map(&:first).sort).to eq([ 200, 201 ])
      expect(results.map { |_status, body| body.fetch("turn") }.uniq.size).to eq(1)
      expect(Turn.where(turn_no: 1).count).to eq(1)
      expect(Game.sole).to have_attributes(current_age: 23, status: "playing")
    end

    it "頻度の制限が効いている状態でも、保存は 1 件のまま（2 件目は 429 か、保存済みの結果の 200）" do
      token = start_game_for_new_session

      results = send_concurrently(token, [ turn_body(turn_no: 1), turn_body(turn_no: 1) ])
      statuses = results.map(&:first)

      expect(statuses.count(201)).to eq(1)
      expect(statuses - [ 200, 201, 429 ]).to be_empty, "想定外のステータス: #{statuses.inspect}"
      expect(Turn.where(turn_no: 1).count).to eq(1)
    end
  end

  describe "同じ番号で配分が違う 2 件を同時に送る" do
    it "先に保存された配分だけが残り、後の 1 件は 409（turn_allocation_mismatch）になる" do
      token = start_game_for_new_session
      allow(RequestRateLimit).to receive(:consume!)
      stub_replay_with_barrier

      results = send_concurrently(
        token,
        [ turn_body(turn_no: 1, living: 60, saving: 20, invest: 20), turn_body(turn_no: 1, living: 70, saving: 10, invest: 20) ]
      )

      expect(results.map(&:first).sort).to eq([ 201, 409 ])
      loser = results.find { |status, _body| status == 409 }.last
      expect(loser.dig("error", "code")).to eq("turn_allocation_mismatch")
      winner = results.find { |status, _body| status == 201 }.last.fetch("turn")
      expect(Turn.where(turn_no: 1).count).to eq(1)
      expect(Turn.sole).to have_attributes(living_pct: winner.fetch("living_pct"), saving_pct: winner.fetch("saving_pct"),
                                           invest_pct: winner.fetch("invest_pct"))
    end
  end
end
