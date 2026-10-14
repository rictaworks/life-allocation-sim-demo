# 保存 API の入口の検査。本体を読む前（Rails の params を作る前）に、次を検査して拒否する。
#   1. POST は Origin が APP_ORIGIN と完全一致しなければ 403（Origin が無い場合も 403）。
#   2. form・multipart の本体は、どのメソッドでも解析させずに 415（Rack は params を作るときに本体を読み、
#      その値が要求ログの Parameters に出るため）。
#   3. POST は application/json（charset パラメータだけ可）以外を 415。
# 本体の大きさ（413）と JSON の形（400）は、コントローラの JsonRequestBody が読むときに検査する。
# 拒否の応答は ApiError の応答と同じ形にする。例外は投げない（投げても ShowExceptions が JSON の 500 にする）。
class ApiWriteGuard
  def initialize(app)
    @app = app
  end

  def call(env)
    request = Rack::Request.new(env)
    return ApiError.new(:origin_forbidden).to_rack_response if request.post? && !same_origin?(request)
    return ApiError.new(:unsupported_media_type).to_rack_response unless acceptable_body?(request)

    @app.call(env)
  end

  private

  def same_origin?(request)
    request.get_header(ApiSettings.origin_env_key) == AppSettings.origin
  end

  # Rack が本体を解析する media type（form・multipart・parseable）は、どのメソッドでも拒否する。
  # それ以外の POST は、JSON の media type だけを受け付ける。
  def acceptable_body?(request)
    return false if request.form_data? || request.parseable_data?
    return true unless request.post?

    JsonRequestBody.json_content_type?(request.content_type)
  end
end

# 例外の JSON 化（ShowExceptions）の内側に置く。そのため、ここで起きた例外も JSON の 500 になる。
# 要求ログ（ApiRequestLogger）は外側にあるので、拒否した要求もログに残る（接続元の IP と本体は出さない）。
Rails.application.config.middleware.insert_after ActionDispatch::ShowExceptions, ApiWriteGuard
