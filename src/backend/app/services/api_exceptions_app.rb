# config.exceptions_app。コントローラの外で起きた例外（経路が無い・ミドルウェアの例外など）も、
# {error: {code, message}} の形にそろえる。例外の内容は応答に出さない。
class ApiExceptionsApp
  CODES_BY_STATUS = {
    400 => :bad_request,
    404 => :not_found,
    413 => :payload_too_large,
    415 => :unsupported_media_type
  }.freeze

  # 対応表にない例外は、内部エラー（500）として扱う。
  def self.call(env)
    exception = env.fetch(ApiSettings.exception_env_key)
    status = ActionDispatch::ExceptionWrapper.status_code_for_exception(exception.class.name)

    ApiError.new(CODES_BY_STATUS.fetch(status, :internal_error)).to_rack_response
  end
end
