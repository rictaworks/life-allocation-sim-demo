# 1 年ごとの記録。配分と年次の値は、再計算ホストの結果だけを保存する。
class Turn < ApplicationRecord
  belongs_to :game
  belongs_to :session

  # 再計算ホストへ渡す配分（生活費・貯蓄・投資の整数の %）。
  def allocation
    { living_pct: living_pct, saving_pct: saving_pct, invest_pct: invest_pct }
  end

  # 保存済みの配分と、与えられた配分が同じか（other は allocation と同じキーの Hash）。
  def same_allocation?(other)
    allocation == other.slice(:living_pct, :saving_pct, :invest_pct)
  end
end
