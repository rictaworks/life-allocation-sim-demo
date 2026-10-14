# 保存要求の値（POST /api/games/turns の本体のうち、許可リストのキーだけ）。
# Rails が見るのは、キーの有無と JSON の整数の型だけ。配分の範囲・刻み・合計は、再計算ホストが確かめる（規則は持たない）。
class TurnRequest < Data.define(:turn_no, :allocation, :honeypot_filled)
  # 本体から読むキー（これ以外は無視する）。
  KEYS = %w[turn_no living_pct saving_pct invest_pct website].freeze
  ALLOCATION_KEYS = %w[living_pct saving_pct invest_pct].freeze
  # 配分は再計算ホストが 32 ビット整数として読む。範囲を超える値は、ホストに渡すと内部エラーになるため、型の誤りとして拒否する。
  PERCENT_RANGE = (-(2**31))..(2**31 - 1)

  class << self
    # params は JsonRequestBody.permit の結果（文字列のキーの Hash）。
    def from_params(params)
      new(
        turn_no: integer_field!(params, "turn_no"),
        allocation: allocation_from!(params),
        honeypot_filled: honeypot_filled?(params)
      )
    end

    private

    def allocation_from!(params)
      ALLOCATION_KEYS.to_h do |key|
        value = integer_field!(params, key)
        raise ApiError.new(:bad_request) unless PERCENT_RANGE.cover?(value)

        [ key.to_sym, value ]
      end
    end

    # キーが無い・整数でない（文字列・小数・真偽値・null）は 400。
    def integer_field!(params, key)
      raise ApiError.new(:bad_request) unless params.key?(key)

      value = params.fetch(key)
      raise ApiError.new(:bad_request) unless value.is_a?(Integer)

      value
    end

    # website に空でない値があれば true（ハニーポット）。キーが無い・空文字列・null は false。
    def honeypot_filled?(params)
      value = params.fetch("website", nil)
      !(value.nil? || value == "")
    end
  end
end
