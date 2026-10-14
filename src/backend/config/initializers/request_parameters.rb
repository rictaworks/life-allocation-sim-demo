# JSON の本体を Rails の params に載せない。
# 理由：(1) 解析に失敗したとき Rails が本体の内容をデバッグログに出すため、(2) 正常時も
# 本体がログの Parameters に出るため。本体は JsonRequestBody だけが読み、許可リストのキーだけを使う。
# form・multipart は Rack が解析するため、この設定では止まらない。入口（config/initializers/api_write_guard.rb）で
# 解析の前に 415 で拒否する。
ActionDispatch::Request.parameter_parsers = ActionDispatch::Request.parameter_parsers.except(Mime[:json].symbol)
