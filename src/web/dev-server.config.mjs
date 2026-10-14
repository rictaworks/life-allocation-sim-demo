// 開発用サーバーの設定と文言（Node で読む）。公開される画面には含まれない。
export const config = Object.freeze({
  host: '127.0.0.1',
  port: 8080,
  railsOrigin: 'http://localhost:3000',
  // 要求の URL からパスを取り出すための基準（パスの解析にだけ使う。接続先ではない）
  requestUrlBase: 'http://localhost',
  // 起動したときに表示する URL の形（{host} と {port} を置き換える）
  localUrlTemplate: 'http://{host}:{port}',
  localUrlPlaceholders: Object.freeze({
    host: '{host}',
    port: '{port}',
  }),
  upstreamTimeoutMs: 10000,
  // 転送先の URL のプロトコル（http だけを受け付ける）
  upstreamProtocol: 'http:',
  // IPv4 を射影した IPv6 アドレスの接頭辞（接続元のアドレスから外す）
  ipv4MappedPrefix: '::ffff:',
  // HTTP のメソッド名（受け付けるのは GET と HEAD だけ）
  methods: Object.freeze({
    get: 'GET',
    head: 'HEAD',
  }),
  // 受け取るヘッダー名（Node は小文字で渡すため、小文字で書きます）
  requestHeaders: Object.freeze({
    realIp: 'x-real-ip',
    forwarded: 'forwarded',
    forwardedPrefix: 'x-forwarded-',
  }),
  // 送るヘッダー名（送るときの表記で書きます）
  responseHeaders: Object.freeze({
    allow: 'Allow',
    cacheControl: 'Cache-Control',
    contentLength: 'Content-Length',
    contentType: 'Content-Type',
    contentTypeOptions: 'X-Content-Type-Options',
  }),
  // 送るヘッダーの値
  headerValues: Object.freeze({
    noStore: 'no-store',
    nosniff: 'nosniff',
  }),
  hopByHopHeaders: Object.freeze([
    'connection',
    'keep-alive',
    'proxy-authenticate',
    'proxy-authorization',
    'proxy-connection',
    'te',
    'trailer',
    'transfer-encoding',
    'upgrade',
  ]),
  // ファイルシステムのエラーコード（Node が返す固定の語）
  fsErrorCodes: Object.freeze({
    notFound: 'ENOENT',
    notDirectory: 'ENOTDIR',
  }),
  appEnvName: 'APP_ENV',
  appEnvDevelopment: 'development',
  apiPathPrefix: '/api',
  indexFileName: 'index.html',
  // 存在しないパスの応答に使うファイル（Vercel と同じく 404.html を 404 の応答として返す）
  notFoundFileName: '404.html',
  cleanUrlExtension: '.html',
  defaultMimeType: 'application/octet-stream',
  // エラーの本文（テキスト）の型
  plainTextMimeType: 'text/plain; charset=utf-8',
  mimeTypes: Object.freeze({
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.txt': 'text/plain; charset=utf-8',
    '.svg': 'image/svg+xml',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.ico': 'image/x-icon',
    '.wasm': 'application/wasm',
    '.data': 'application/octet-stream',
  }),
  messages: Object.freeze({
    refused: '開発用サーバーは APP_ENV が development のときだけ起動します。起動を取りやめました。',
    started: '開発用サーバーを起動しました。',
    railsOriginInvalid: 'API の転送先は http の URL で指定してください。',
    badRequest: 'リクエストが正しくありません。',
    methodNotAllowed: 'このパスではその操作を受け付けません。',
    clientAddressMissing: '接続元のアドレスを確認できないため、API へ転送できません。',
    upstreamUnavailable: 'API サーバー（Rails）に接続できません。Rails が起動しているか確認してください。',
    upstreamTimeout: 'API サーバー（Rails）の応答がタイムアウトしました。',
    internalError: '内部エラーが発生しました。',
  }),
  log: Object.freeze({
    upstreamFailed: 'API の転送に失敗しました（接続の状態のみを表示します）。',
    // 転送の時間切れを表す詳細（ログに出します。応答には出しません）
    upstreamTimeoutDetail: 'upstream timeout',
  }),
});
