# 保存 API の JSON の本体を読む。検査の順番：Content-Type（415）→ 本体のサイズ（413）→ JSON の形（400）。
# 読むのは許可リストのキーだけにする。未知のキーは無視する。
class JsonRequestBody
  class << self
    def read!(request)
      raise ApiError.new(:unsupported_media_type) unless json_content_type?(request.content_type)

      parse!(read_within_limit!(request))
    end

    def permit(body, keys)
      body.slice(*keys)
    end

    # application/json のあと、charset=utf-8 のパラメータだけを許す（大文字小文字は問わない）。
    def json_content_type?(value)
      media_type, *parameters = value.to_s.split(";", -1).map(&:strip)
      return false unless media_type&.casecmp?(ApiSettings.json_media_type)

      parameters.all? { |parameter| parameter.match?(charset_pattern) }
    end

    private

    def charset_pattern
      @charset_pattern ||= /\Acharset=(["]?)#{Regexp.escape(ApiSettings.json_charset)}\1\z/i
    end

    # 申告された Content-Length と、実際に読んだ長さの両方で上限を検査する。
    def read_within_limit!(request)
      limit = ApiSettings.body_limit_bytes
      declared = request.get_header(ApiSettings.content_length_env_key)
      if declared
        length = Integer(declared, exception: false)
        raise ApiError.new(:bad_request) if length.nil? || length.negative?
        raise ApiError.new(:payload_too_large) if length > limit
      end

      input = request.body
      raw = input ? input.read(limit + 1).to_s : ""
      input&.rewind
      raise ApiError.new(:payload_too_large) if raw.bytesize > limit

      raw
    end

    def parse!(raw)
      text = raw.dup.force_encoding(Encoding::UTF_8)
      raise ApiError.new(:bad_request) unless text.valid_encoding?

      data = JSON.parse(text)
      raise ApiError.new(:bad_request) unless data.is_a?(Hash)

      data
    rescue JSON::ParserError
      raise ApiError.new(:bad_request)
    end
  end
end
