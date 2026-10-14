# 全コントローラの共通。エラーは rescue_from で {error: {code, message}} の形にそろえる。
# 例外の内容・スタックトレース・SQL は応答に出さない（ログには出す）。
# rescue_from が捕まえるのはコントローラの中の例外だけ。中間層の例外は config/application.rb の方針に従う。
class ApplicationController < ActionController::API
  include ActionController::Cookies

  # JSON の本体は Rails の params に載せない（読むのは JsonRequestBody だけ）。
  wrap_parameters false

  # 後から書いたものほど先に照合される。ApiError を最後に書き、個別の応答を優先させる。
  rescue_from StandardError, with: :render_unexpected_error
  rescue_from ActionController::BadRequest, with: :render_bad_request
  rescue_from ApiError, with: :render_api_error

  private

  def render_api_error(error)
    response.headers[ApiSettings.retry_after_header] = error.retry_after.to_s if error.retry_after
    render json: error.body, status: error.status
  end

  def render_bad_request(_exception)
    render_api_error(ApiError.new(:bad_request))
  end

  def render_unexpected_error(exception)
    Rails.logger.error(InternalMessages.text(:api, :unexpected_error, class_name: exception.class.name,
                                                                      controller: controller_path, action: action_name))
    Rails.logger.error(Array(exception.backtrace).first(30).join("\n"))
    render_api_error(ApiError.new(:internal_error))
  end
end
