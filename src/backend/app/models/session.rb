# 匿名の保存セッション。Cookie の値そのものは保存せず、そのハッシュ（SHA-256）だけを保存する。
class Session < ApplicationRecord
  has_one :game, dependent: :destroy

  class << self
    # 新しいセッションを作る。Cookie に入れる値（token）も返す。
    def issue!
      token = SecureRandom.urlsafe_base64(ApiSettings.token_bytes)
      now = Time.current
      session = create!(token_digest: digest(token), created_at: now, expires_at: now + ApiSettings.session_ttl)
      [ session, token ]
    end

    # Cookie の値から、期限内のセッションを探す。値が無い・期限切れなら nil。
    def find_active(token)
      return nil if token.blank?

      where(token_digest: digest(token)).where("expires_at > ?", Time.current).first
    end

    def digest(token)
      Digest::SHA256.hexdigest(token)
    end
  end
end
