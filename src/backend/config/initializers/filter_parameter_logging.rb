# Be sure to restart your server when you modify this file.

# Configure parameters to be partially matched (e.g. passw matches password) and filtered from the log file.
# Use this to limit dissemination of sensitive information.
# See the ActiveSupport::ParameterFilter documentation for supported notations and behaviors.
#
# 保存 API では、Cookie（セッション ID を含む）と本体の値をログに出さない。
# 本体は params に載せない（JSON は config/initializers/request_parameters.rb、form・multipart は
# config/initializers/api_write_guard.rb が解析の前に拒否する）。それでも params に載った値は、
# キーに関係なくすべて [FILTERED] にする（要求ログの Parameters の行に値を出さないため）。
# 任意のキー（空のキーを含む）に一致する正規表現は、値の全体（ネストした値も含む）を置き換える。
# Proc は値を書き換えられないため使わない（ActiveSupport は Proc の戻り値を使わない）。
Rails.application.config.filter_parameters += [
  :passw, :email, :secret, :token, :_key, :crypt, :salt, :certificate, :otp, :ssn, :cvv, :cvc,
  :cookie, :session,
  /.*/
]
