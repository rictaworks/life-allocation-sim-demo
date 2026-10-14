module Api
  # 1 年ぶんの配分の保存（POST /api/games/turns）。配分の規則の検査と年次の計算は、再計算ホストに任せる。
  # 新規の保存は 201。保存済みの番号を同じ配分で再送したときは、保存せず 200（保存済みの結果）。
  class TurnsController < ApplicationController
    include JsonWriteRequest
    include CurrentSession

    def create
      owner = require_session!
      turn_request = TurnRequest.from_params(json_params(*TurnRequest::KEYS))
      result = TurnSaver.call(session: owner, turn_request: turn_request)

      render json: { turn: result.turn, game: result.game }, status: result.created ? :created : :ok
    end
  end
end
