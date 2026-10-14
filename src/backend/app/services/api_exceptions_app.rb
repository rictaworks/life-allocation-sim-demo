# config.exceptions_app。コントローラの外で起きた例外（経路が無い・ミドルウェアの例外など）も、
# {error: {code, message}} の形にそろえる。例外の内容は応答に出さない。
class ApiExceptionsApp
  # 状態ごとの code（ApiError と同じ形の応答）。
  CODES_BY_STATUS = {
    400 => :bad_request,
    404 => :not_found,
    413 => :payload_too_large,
    415 => :unsupported_media_type
  }.freeze

  # 個別の code を持たず、内部エラー（500）として返す状態。
  # 500 は、対応表に無い例外の既定の状態（ActionDispatch::ExceptionWrapper）。405・406・422・429・501 は、
  # Rails がこの API の中で出しうる状態で、個別の code は定めていない（既存の挙動のまま内部エラーにする）。
  INTERNAL_ERROR_STATUSES = [ 405, 406, 422, 429, 500, 501 ].freeze

  def self.call(env)
    exception = env.fetch(ApiSettings.exception_env_key)
    status = ActionDispatch::ExceptionWrapper.status_code_for_exception(exception.class.name)

    ApiError.new(code_for(status, exception)).to_rack_response
  end

  # 対応表にも内部エラーの一覧にも無い状態は、想定していない状態として扱う。内部エラーにし、状態と例外のクラス名をログに残す。
  def self.code_for(status, exception)
    return CODES_BY_STATUS.fetch(status) if CODES_BY_STATUS.key?(status)
    return :internal_error if INTERNAL_ERROR_STATUSES.include?(status)

    Rails.logger.error(InternalMessages.text(:api, :unmapped_status, status: status, class_name: exception.class.name))
    :internal_error
  end
  private_class_method :code_for
end
