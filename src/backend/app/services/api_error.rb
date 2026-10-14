# 応答に出すエラー。コードは STATUSES に定義したものだけ。文言は config/locales/ja.yml の api_errors から取る。
# 例外の内容・スタックトレース・SQL は含めない（ログに出すのは rescue_from の側）。
class ApiError < StandardError
  STATUSES = {
    bad_request: 400,
    session_required: 401,
    origin_forbidden: 403,
    not_found: 404,
    game_not_found: 404,
    game_finished: 409,
    turn_conflict: 409,
    turn_allocation_mismatch: 409,
    payload_too_large: 413,
    unsupported_media_type: 415,
    difficulty_missing: 422,
    difficulty_unknown: 422,
    allocation_negative: 422,
    allocation_step_mismatch: 422,
    allocation_sum_mismatch: 422,
    rate_limited: 429,
    internal_error: 500,
    service_unavailable: 503
  }.freeze

  attr_reader :code, :retry_after

  # retry_after は 429 のときだけ使う（秒）。Retry-After ヘッダーに入る。
  def initialize(code, retry_after: nil)
    raise ArgumentError, "unknown ApiError code: #{code.inspect}" unless STATUSES.key?(code)

    @code = code
    @retry_after = retry_after
    super(code.to_s)
  end

  def status
    STATUSES.fetch(code)
  end

  def body
    { error: { code: code.to_s, message: I18n.t(code, scope: :api_errors, locale: :ja, raise: true) } }
  end

  # Rack の応答（ステータス・ヘッダー・本文）。コントローラの外（ミドルウェア・exceptions_app）でも、同じ形にそろえる。
  def to_rack_response
    headers = { "Content-Type" => "#{ApiSettings.json_media_type}; charset=#{ApiSettings.json_charset}" }
    headers["Retry-After"] = retry_after.to_s if retry_after
    [ status, headers, [ JSON.generate(body) ] ]
  end
end
