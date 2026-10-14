module Api
  # 現在のゲームの取得（GET /api/game）。セッションもゲームも無ければ {"game": null}。
  # 状態を変えない読み取りのため、頻度は数えず、記録（request_events）も残さない（CLAUDE.md：状態を変えるのは POST だけ）。
  # ゲームがあるときは、ホストの replay で中身を作る。ホストの負荷は、同時実行の上限と制限時間で守る（config/core_host.yml）。
  class GameController < ApplicationController
    include CurrentSession

    def show
      owner = current_session
      return render(json: { game: nil }, status: :ok) if owner.nil?

      game = owner.game
      render json: { game: game && GameSnapshot.call(game) }, status: :ok
    end
  end
end
