require "ipaddr"

# 信頼するプロキシ（Vercel の rewrites）が付けた X-Real-IP だけから、接続元のアドレスを取る。
# X-Forwarded-For など、クライアントが書けるヘッダーは見ない。
class ClientAddress
  # 文字の種類と長さを先に絞る（"/" や "%" を含む値は IPAddr に渡さない）。
  CHARACTERS = /\A[0-9A-Fa-f:.]{2,45}\z/

  class << self
    def from_request!(request)
      parse!(request.headers[ApiSettings.client_ip_header])
    end

    # 正規化した文字列を返す。形式が不正なら bad_request。
    def parse!(value)
      raise ApiError.new(:bad_request) unless value.is_a?(String) && value.match?(CHARACTERS)

      IPAddr.new(value).to_s
    rescue IPAddr::InvalidAddressError
      raise ApiError.new(:bad_request)
    end
  end
end
