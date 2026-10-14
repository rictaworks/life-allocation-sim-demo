# 現在のセッション（Cookie の値から、期限内のものを探す）。URL・クエリ・本体からは読まない。
# ゲームの検索は、必ずこのセッションの関連（session.game）を通す（他人のゲームは存在を明かさない）。
module CurrentSession
  extend ActiveSupport::Concern

  private

  # 期限切れ・一致しない値・Cookie が無い場合は nil。
  def current_session
    return @current_session if defined?(@current_session)

    @current_session = Session.find_active(cookies[ApiSettings.cookie_name])
  end

  # セッションが無ければ 401（ゲームの操作は、必ずセッションの中で行う）。
  def require_session!
    session = current_session
    raise ApiError.new(:session_required) if session.nil?

    session
  end
end
