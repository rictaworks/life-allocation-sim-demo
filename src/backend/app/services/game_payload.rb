# GAME の応答の形（契約 2）。状態・履歴・評価は、再計算ホストの出力をそのまま使う（クライアントの値は信用しない）。
module GamePayload
  class << self
    def build(game, state:, turns:, evaluation:)
      {
        seed: game.seed,
        difficulty: game.difficulty,
        status: state.fetch("status"),
        current_age: state.fetch("age"),
        state: state,
        turns: turns,
        evaluation: evaluation
      }
    end
  end
end
