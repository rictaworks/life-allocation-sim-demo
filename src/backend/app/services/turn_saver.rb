# 1 年ぶんの配分の保存（POST /api/games/turns の中身）。
# 順番：ゲームの確認（404）→ 保存済みの番号の再送（頻度の確認 turn_save を通してから、保存済みの結果を返す。
#   保存はしない。完走・破綻の判定より先に見る）→ 終了の確認（409。新しい番号だけ）→ 番号の確認（409）
#   → 頻度の確認（429。turn_save。ホストは呼ばない）→ 再計算（ホスト）→ 保存。
# 頻度は、再送も新しい保存も同じ枠（turn_save：1 秒 1 件・1 分 20 件）で数える（受け入れ条件 9）。
# ホストの呼び出し（最大 5 秒）は、書き込みの transaction の中で行わない（GameStarter と同じ）。
# 年次の値は、ホストの結果だけを保存する。クライアントの値は、ホストに渡す配分の 1 年ぶんとしてだけ使う。
class TurnSaver
  # turn は TURN、game は GAME（どちらも応答の形）。created は新規の保存なら true（201）、保存済みの再送なら false（200）。
  Result = Data.define(:turn, :game, :created)

  class << self
    def call(session:, turn_request:)
      game = session.game
      raise ApiError.new(:game_not_found) if game.nil?

      saved = saved_turns(game)
      return resend(session, game, saved, turn_request) if turn_request.turn_no.between?(1, saved.size)

      raise ApiError.new(:game_finished) unless game.playing?
      raise ApiError.new(:turn_conflict) unless turn_request.turn_no == saved.size + 1

      # 頻度の確認は、ホストを呼ぶ前に行う（超過は 429。ホストは呼ばない）。
      RequestRateLimit.consume!(kind: :turn_save, key: session.id)
      replayed = CoreHostClient.replay(seed: game.seed, difficulty: game.difficulty, history: history_for(saved, turn_request))
      host_turn = new_turn_from(replayed, turn_request.turn_no)

      # ハニーポットに値がある要求は、通常と同じ応答を返すが、保存はしない。
      return created_result(game, replayed, host_turn) if turn_request.honeypot_filled

      save(session, game, turn_request, replayed, host_turn)
    end

    private

    def saved_turns(game)
      game.turns.order(:turn_no).to_a
    end

    # 保存済みの番号の再送。保存はしないが、GAME を得るためにホストを呼ぶ。頻度は新しい保存と同じ枠（turn_save）で数え、
    # 超過は 429（ホストは呼ばない）。ゲームの終了は見ない（完走のあとの再試行でも、保存済みの結果を返す）。
    def resend(session, game, saved, turn_request)
      RequestRateLimit.consume!(kind: :turn_save, key: session.id)
      saved_result(game, saved, turn_request)
    end

    # 保存済みの番号の再送の中身。配分が同じなら保存済みの TURN を返し、違えば 409。GAME は現在の履歴をホストで再計算して返す。
    def saved_result(game, saved, turn_request)
      turn = saved.find { |record| record.turn_no == turn_request.turn_no }
      internal_failure!("saved turn is missing for a requested replay") if turn.nil?
      raise ApiError.new(:turn_allocation_mismatch) unless turn.same_allocation?(turn_request.allocation)

      Result.new(turn: TurnPayload.from_record(turn), game: GameSnapshot.call(game), created: false)
    end

    # ホストに渡す履歴：保存済みの配分の後ろに、今回の配分を 1 年ぶん足す。
    def history_for(saved, turn_request)
      saved.map(&:allocation) + [ turn_request.allocation ]
    end

    # ホストの replay は履歴の全年を返す。今回の年は最後の 1 件で、件数と番号が一致しなければ内部エラー。
    def new_turn_from(replayed, turn_no)
      turns = replayed.fetch("turns")
      last = turns.last
      return last if turns.size == turn_no && last.fetch("turn_no") == turn_no

      internal_failure!("replay result does not match the requested turn")
    end

    def created_result(game, replayed, host_turn)
      Result.new(
        turn: TurnPayload.from_host(host_turn),
        game: GamePayload.build(game, state: replayed.fetch("state"), turns: replayed.fetch("turns"),
                                      evaluation: replayed.fetch("evaluation")),
        created: true
      )
    end

    # 保存する。一意制約の衝突（同じ番号が先に保存された）は、保存済みの結果として扱う（配分が同じなら 200、違えば 409）。
    def save(session, game, turn_request, replayed, host_turn)
      write_turn!(session, game, turn_request, replayed, host_turn)
      created_result(game, replayed, host_turn)
    rescue ActiveRecord::RecordNotUnique
      current = Game.find_by(id: game.id, session_id: session.id)
      raise ApiError.new(:game_not_found) if current.nil?

      saved_result(current, saved_turns(current), turn_request)
    end

    # ゲームが残っていることを確かめ、ターンを 1 行追加し、ゲームの状態（status・current_age）を更新する。1 つの transaction。
    def write_turn!(session, game, turn_request, replayed, host_turn)
      state = replayed.fetch("state")
      Turn.transaction do
        current = Game.find_by(id: game.id, session_id: session.id)
        raise ApiError.new(:game_not_found) if current.nil?

        Turn.create!(
          game: current,
          session: session,
          turn_no: turn_request.turn_no,
          age: host_turn.fetch("age"),
          living_pct: host_turn.fetch("living_pct"),
          saving_pct: host_turn.fetch("saving_pct"),
          invest_pct: host_turn.fetch("invest_pct"),
          event_code: host_turn.fetch("event_code"),
          savings_after: host_turn.fetch("savings_after"),
          invest_after: host_turn.fetch("invest_after"),
          satisfaction_after: host_turn.fetch("satisfaction_after")
        )
        current.update!(status: state.fetch("status"), current_age: state.fetch("age"))
      end
    end

    # 内部の不整合。ログには操作の名前と結果だけを出し、応答は内部エラーにする（詳細は出さない）。
    def internal_failure!(message)
      Rails.logger.error("[turn] #{message}")
      raise ApiError.new(:internal_error)
    end
  end
end
