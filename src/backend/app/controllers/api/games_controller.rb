module Api
  # 新規ゲームの開始（POST /api/games）。既存のゲームは上書きする。難易度の一覧は持たず、ホストが確かめる。
  class GamesController < ApplicationController
    include JsonWriteRequest
    include CurrentSession

    def create
      owner = require_session!
      # 再計算ホストを呼ぶ前に、セッションごとの頻度を確かめて記録する（超過は 429。ホストは呼ばない）。
      RequestRateLimit.consume!(kind: :game_start, key: owner.id)
      started = GameStarter.call(owner: owner, difficulty: difficulty_param)

      render json: { game: GamePayload.build(started.game, state: started.state, turns: [], evaluation: nil) },
             status: :created
    end

    private

    # 難易度は文字列のままホストへ渡す。文字列でない値は、一覧にない難易度として拒否する。
    def difficulty_param
      value = json_params("difficulty")["difficulty"]
      raise ApiError.new(:difficulty_unknown) unless value.nil? || value.is_a?(String)

      value
    end
  end
end
