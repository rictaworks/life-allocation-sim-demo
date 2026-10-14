# 1 セッションのゲーム（1 件まで）。seed はサーバーが生成したものだけを保存する。
# 状態の値（年齢・状態・履歴の結果）は再計算ホストの出力を使い、この表の status と current_age は保存時の写し。
class Game < ApplicationRecord
  belongs_to :session
  has_many :turns, dependent: :destroy

  # プレイ中か。完走・破綻のあとは、ターンを追加できない（status はホストの state.status の写し）。
  def playing?
    status == CoreHostSettings.game_status(:playing)
  end
end
