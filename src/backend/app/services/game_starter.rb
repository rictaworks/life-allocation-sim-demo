# 新規ゲームの開始（POST /api/games の中身）。
# 難易度の確認と初期状態は再計算ホスト（init）に任せる。ホストが拒否したら、既存のゲームには触れない。
# 旧ゲームと旧履歴は、新しいゲームの作成と同じトランザクションで消す（ホストを呼んだあとで、書き込みの枠を取る）。
class GameStarter
  # seed は 0 以上 2^63−1 以下の整数（再計算ホストの要件）。保存要求の値は使わない。
  SEED_LIMIT = 2**63

  Started = Data.define(:game, :state)

  class << self
    def call(owner:, difficulty:)
      initial = CoreHostClient.init(difficulty: difficulty)
      state = initial.fetch("state")
      seed = SecureRandom.random_number(SEED_LIMIT)

      game = Game.transaction do
        Turn.where(session_id: owner.id).delete_all
        Game.where(session_id: owner.id).delete_all
        Game.create!(
          session: owner,
          seed: seed,
          difficulty: initial.fetch("difficulty"),
          status: state.fetch("status"),
          current_age: state.fetch("age")
        )
      end

      Started.new(game: game, state: state)
    end
  end
end
