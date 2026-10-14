# 再計算ホストの出力の形（キーと型）。規則は見ない（規則は Domain Core とホストが決める）。
# 形は src/core-host/CoreHost/Protocol.md に合わせる。キーが足りない・型が違うと Mismatch（パスで場所を示し、値は示さない）。
module CoreHostShape
  class Mismatch < StandardError; end

  # null も許す型（Nullable.new(:string) など）。
  Nullable = Data.define(:type)

  STATE = {
    "age" => :integer,
    "income_yen" => :integer,
    "savings_yen" => :integer,
    "investment_yen" => :integer,
    "satisfaction" => :integer,
    "price_index_bp" => :integer,
    "status" => :string,
    "struggling" => :boolean,
    "raise_total_yen" => :integer,
    "occurred_once_events" => [ :string ]
  }.freeze

  TURN = {
    "turn_no" => :integer,
    "age" => :integer,
    "living_pct" => :integer,
    "saving_pct" => :integer,
    "invest_pct" => :integer,
    "event_code" => Nullable.new(:string),
    "savings_after" => :integer,
    "invest_after" => :integer,
    "satisfaction_after" => :integer
  }.freeze

  EVALUATION = {
    "total_assets_yen" => :integer,
    "average_satisfaction" => :integer,
    "is_bankrupt" => :boolean,
    "struggle_years" => :integer,
    "investment_sold_years" => :integer,
    "rank" => :string,
    "comments" => {
      "retirement" => :string,
      "quality" => :string,
      "preparedness" => :string
    }
  }.freeze

  SHAPES = {
    init: { "difficulty" => :string, "state" => STATE },
    replay: { "turns" => [ TURN ], "state" => STATE, "evaluation" => Nullable.new(EVALUATION) },
    error: { "error" => { "code" => :string, "message" => :string } }
  }.freeze

  class << self
    # value が name の形なら何もしない。違えば Mismatch。
    def check!(value, name)
      verify(value, SHAPES.fetch(name), name.to_s)
    end

    private

    def verify(value, type, path)
      case type
      when :integer
        raise Mismatch, path unless value.is_a?(Integer)
      when :string
        raise Mismatch, path unless value.is_a?(String)
      when :boolean
        raise Mismatch, path unless [ true, false ].include?(value)
      when Nullable
        verify(value, type.type, path) unless value.nil?
      when Array
        verify_array(value, type.first, path)
      when Hash
        verify_object(value, type, path)
      else
        raise ArgumentError, "unknown shape type"
      end
    end

    def verify_array(value, item_type, path)
      raise Mismatch, path unless value.is_a?(Array)

      value.each_with_index { |item, index| verify(item, item_type, "#{path}[#{index}]") }
    end

    def verify_object(value, shape, path)
      raise Mismatch, path unless value.is_a?(Hash)

      shape.each do |key, type|
        raise Mismatch, "#{path}.#{key}" unless value.key?(key)

        verify(value[key], type, "#{path}.#{key}")
      end
    end
  end
end
