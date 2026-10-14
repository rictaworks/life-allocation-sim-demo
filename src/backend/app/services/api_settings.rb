# config/api.yml の値を読む。値が無ければ例外になる（既定値では補わない）。
module ApiSettings
  RateLimitRule = Data.define(:window, :limit)

  class << self
    def cookie_name
      fetch(:session, :cookie_name)
    end

    def session_ttl
      fetch(:session, :ttl_days).days
    end

    def token_bytes
      fetch(:session, :token_bytes)
    end

    def client_ip_header
      fetch(:client_ip, :header)
    end

    def body_limit_bytes
      fetch(:request, :body_limit_bytes)
    end

    def json_media_type
      fetch(:request, :json_media_type)
    end

    def json_charset
      fetch(:request, :json_charset)
    end

    def request_digest_salt
      fetch(:request_digest, :key_salt)
    end

    # 種類ごとの制限（期間と件数）。未定義の種類は例外になる。
    def rate_limit_rules(kind)
      fetch(:rate_limits, kind).map do |rule|
        RateLimitRule.new(window: rule.fetch(:window_seconds).seconds, limit: rule.fetch(:limit))
      end
    end

    private

    def fetch(*path)
      path.reduce(settings) { |node, key| node.fetch(key) }
    end

    def settings
      @settings ||= Rails.application.config_for(:api)
    end
  end
end
