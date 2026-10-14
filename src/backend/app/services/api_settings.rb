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

    def session_status(name)
      fetch(:response, :session_status, name)
    end

    def retry_after_header
      fetch(:http, :retry_after_header)
    end

    def content_type_header
      fetch(:http, :content_type_header)
    end

    # JSON の応答の Content-Type（例：application/json; charset=utf-8）。
    def json_content_type
      format(fetch(:http, :content_type_format), media_type: json_media_type, charset: json_charset)
    end

    def origin_env_key
      fetch(:http, :origin_env_key)
    end

    def content_length_env_key
      fetch(:http, :content_length_env_key)
    end

    def exception_env_key
      fetch(:http, :exception_env_key)
    end

    def blocked_hosts_env_key
      fetch(:http, :blocked_hosts_env_key)
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
