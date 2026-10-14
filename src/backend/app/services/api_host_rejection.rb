# Host が APP_ORIGIN のホストと一致しないときの応答（ActionDispatch::HostAuthorization の response_app）。
# Rails の既定（空の text/html）ではなく、エラーの形式（JSON）で返す。
# 拒否した Host は応答には出さず、運用のためにログに出す（Vercel の転送で届く Host の確認に使う）。
class ApiHostRejection
  def self.call(env)
    # 値は引用符で囲んでから出す（改行などを含む値でも、ログの 1 行を壊さないため）。
    blocked_hosts = Array(env["action_dispatch.blocked_hosts"]).map(&:inspect).join(", ")
    Rails.logger.error("[api] blocked host: #{blocked_hosts}")

    # Host 用の code は無いため、origin_forbidden（403）を使う。
    ApiError.new(:origin_forbidden).to_rack_response
  end
end
