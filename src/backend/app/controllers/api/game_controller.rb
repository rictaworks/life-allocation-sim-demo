module Api
  # 現在のゲームの取得（GET /api/game）。セッションもゲームも無ければ {"game": null}。
  # 有効なセッションの読み取りは、再計算ホストを呼ぶ前に頻度を数える（超過は 429。ホストは呼ばない）。
  # 数えるのは頻度の記録（request_events の 1 行）だけで、ゲームの状態は変えない。
  class GameController < ApplicationController
    include CurrentSession

    def show
      owner = current_session
      return render(json: { game: nil }, status: :ok) if owner.nil?

      RequestRateLimit.consume!(kind: :game_read, key: owner.id)
      game = owner.game
      render json: { game: game && GameSnapshot.call(game) }, status: :ok
    end
  end
end
