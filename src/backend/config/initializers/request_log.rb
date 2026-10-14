# 要求ログ（Started POST ... for <IP> at ...）から接続元の IP を外す。
# Rails の標準の行は request.remote_ip を出すが、その値は X-Forwarded-For など、クライアントが書ける値から決まりうる。
# IP は件数の計算にだけ使い、保存もログにも出さない（件数は HMAC のダイジェストで数える）。
# 行の形は標準と同じで、接続元の部分（for ...）だけを外す。タグ（config.log_tags）は標準と同じものを使う。
class ApiRequestLogger < Rails::Rack::Logger
  private

  def started_request_message(request)
    sprintf('Started %s "%s" at %s', request.raw_request_method, request.filtered_path, Time.now)
  end
end

Rails.application.config.middleware.swap(Rails::Rack::Logger, ApiRequestLogger, Rails.application.config.log_tags)
