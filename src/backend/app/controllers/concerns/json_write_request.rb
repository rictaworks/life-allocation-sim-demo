# 状態を変える POST の共通の検査（本体の読み取り）。
# Origin（403）と Content-Type（415）は、本体を読む前に入口（config/initializers/api_write_guard.rb）が検査する。
# ここでは、サイズ（413）と JSON（400）を検査する。読むのは許可リストのキーだけ（json_params）。
module JsonWriteRequest
  extend ActiveSupport::Concern

  included do
    before_action :read_json_body!
  end

  private

  def read_json_body!
    @json_body = JsonRequestBody.read!(request)
  end

  def json_params(*keys)
    JsonRequestBody.permit(@json_body, keys)
  end
end
