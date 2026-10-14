require "openssl"

# 件数を DB で数え、超過した要求を 429（Retry-After 付き）にする。プロセス内の状態には頼らない。
# 判定と記録は同じトランザクションの中で行う（SQLite の既定は BEGIN IMMEDIATE で、書き込みを直列にする）。
# 古い行は、記録と同じ処理の中で消す。
class RequestRateLimit
  class << self
    # 制限内なら、その要求を記録するだけにする（処理は呼び出し側が、記録の後で行う）。超過なら ApiError(:rate_limited)。
    # 重い処理（再計算ホストの呼び出しなど）は、書き込みの枠を握ったまま行わないため、先にこちらを使う。
    # 状態を変える POST（ゲームの開始・保存）だけが使う。GET は数えないため、ここを通さない。
    def consume!(kind:, key:)
      perform(kind: kind, key: key) { nil }
    end

    # 制限内なら block を実行し、その要求を記録する。超過なら ApiError(:rate_limited) を投げ、何も記録しない。
    # key は数える単位（接続元 IP の正規化した文字列、セッションの ID など）。kind は config/api.yml の rate_limits の種類だけ
    # （規則の無い種類は KeyError。既定値では補わない）。
    def perform(kind:, key:)
      rules = ApiSettings.rate_limit_rules(kind)
      digest = digest_for(kind, key)

      RequestEvent.transaction do
        retry_after = retry_after_seconds(kind, digest, rules)
        raise ApiError.new(:rate_limited, retry_after: retry_after) if retry_after

        result = yield
        RequestEvent.create!(kind: kind.to_s, scope_digest: digest)
        prune!(kind, rules)
        result
      end
    end

    # 記録に使うダイジェスト（HMAC-SHA256。鍵は SECRET_KEY_BASE から導く）。元の値は保存しない。
    def digest_for(kind, key)
      OpenSSL::HMAC.hexdigest("SHA256", digest_key, "#{kind}:#{key}")
    end

    private

    def digest_key
      Rails.application.key_generator.generate_key(ApiSettings.request_digest_salt, 32)
    end

    # 超過している規則それぞれの待ち時間（秒）のうち最も長いもの。超過が無ければ nil。
    # 規則の件数に達するまでの最も古い記録が期間の外へ出るまでが待ち時間。
    def retry_after_seconds(kind, digest, rules)
      now = Time.current
      waits = rules.filter_map do |rule|
        recent = RequestEvent.where(kind: kind.to_s, scope_digest: digest)
                             .where("created_at > ?", now - rule.window)
                             .order(created_at: :desc)
                             .limit(rule.limit)
                             .pluck(:created_at)
        next if recent.size < rule.limit

        (recent.last + rule.window - now).ceil.clamp(1..)
      end
      waits.max
    end

    # 種類ごとの最長の期間より古い行は、どの判定にも使われないので消す。
    def prune!(kind, rules)
      longest = rules.map(&:window).max
      RequestEvent.where(kind: kind.to_s).where("created_at <= ?", Time.current - longest).delete_all
    end
  end
end
