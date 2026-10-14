# TURN の応答の形（契約 2）。値は、サーバーの再計算の結果（保存した行、またはホストの出力）だけを使う。
module TurnPayload
  KEYS = %w[turn_no age living_pct saving_pct invest_pct event_code savings_after invest_after satisfaction_after].freeze

  class << self
    # 保存済みのターン（Turn の行）から。
    def from_record(turn)
      KEYS.to_h { |key| [ key, turn.public_send(key) ] }
    end

    # ホストの TURN（replay の turns の要素）から。
    def from_host(turn)
      KEYS.to_h { |key| [ key, turn.fetch(key) ] }
    end
  end
end
