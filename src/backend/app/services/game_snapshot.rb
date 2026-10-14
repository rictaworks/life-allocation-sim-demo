# 現在のゲームの状態（GET /api/game の中身）。保存された配分履歴を、再計算ホストの replay で読み直す。DB は変えない。
class GameSnapshot
  class << self
    def call(game)
      history = game.turns.order(:turn_no).map do |turn|
        { living_pct: turn.living_pct, saving_pct: turn.saving_pct, invest_pct: turn.invest_pct }
      end
      replay = CoreHostClient.replay(seed: game.seed, difficulty: game.difficulty, history: history)

      GamePayload.build(
        game,
        state: replay.fetch("state"),
        turns: replay.fetch("turns"),
        evaluation: replay.fetch("evaluation")
      )
    end
  end
end
