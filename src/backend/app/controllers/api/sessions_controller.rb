module Api
  # 匿名セッションの発行。新規は 201（Cookie を発行する）。有効な Cookie があれば 200（何も発行しない）。
  class SessionsController < ApplicationController
    include JsonWriteRequest

    STATUS_CREATED = "created".freeze
    STATUS_EXISTING = "existing".freeze

    def create
      # 新規でも既存でも、X-Real-IP が無い・不正なら 400（セッションは発行しない）。
      client_address = ClientAddress.from_request!(request)

      if Session.find_active(session_token)
        render json: { session: { status: STATUS_EXISTING } }, status: :ok
        return
      end

      # 新規の発行は IP あたり 1 分 10 件まで（超過は 429）。既存の Cookie による 200 は数えない。
      new_session, token = RequestRateLimit.perform(kind: :session_issue, key: client_address) { Session.issue! }
      write_session_cookie(token, new_session)
      render json: { session: { status: STATUS_CREATED } }, status: :created
    end

    private

    # Cookie だけを読む（URL・クエリ・本体からは読まない）。
    def session_token
      cookies[ApiSettings.cookie_name]
    end

    # __Host- 接頭辞のため、Secure・Path=/・Domain なし。HttpOnly・SameSite=Lax。環境によって緩めない。
    def write_session_cookie(token, new_session)
      cookies[ApiSettings.cookie_name] = {
        value: token,
        expires: new_session.expires_at,
        max_age: ApiSettings.session_ttl.to_i,
        path: "/",
        secure: true,
        httponly: true,
        same_site: :lax
      }
    end
  end
end
