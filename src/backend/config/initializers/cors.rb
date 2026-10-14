# CORS は付けない。保存 API は同一オリジン（Vercel の rewrites）だけで使う。
# Rack::Cors は入れていない（Gemfile にも無い）。ヘッダーを足す変更は設計の変更なので、spec が検査する。
