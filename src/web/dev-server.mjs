// 開発用サーバー。src/web の静的ファイルを配り、/api を Rails（既定 http://localhost:3000）へ転送する。
// APP_ENV が development のときだけ起動する。それ以外は起動を拒否する（fail closed）。
// 転送のとき、X-Real-IP は接続元のアドレスで付け直し、クライアントが送った X-Real-IP と X-Forwarded-* は捨てる。
import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
import { pipeline } from 'node:stream/promises';
import { fileURLToPath } from 'node:url';
import { config } from './dev-server.config.mjs';

/**
 * 開発用の起動条件を確かめる。development 以外（未設定・大文字・空白を含む値を含む）は例外にする。
 * @param {NodeJS.ProcessEnv} env
 */
export function assertDevelopmentEnvironment(env) {
  if (env[config.appEnvName] !== config.appEnvDevelopment) {
    throw new Error(config.messages.refused);
  }
}

/**
 * URL のパスを、src/web の中の安全なファイルパスにする。
 * 範囲の外に出る・ドットで始まる要素を含む・区切り文字が混じるときは null を返す。
 * 不正なパーセント符号化は URIError を投げる（呼び出し側で 400 にする）。
 * @param {string} root 配信の起点（絶対パス）
 * @param {string} rawPath パーセント符号化のままの URL のパス
 * @returns {string | null}
 */
export function resolveStaticPath(root, rawPath) {
  const decoded = decodeURIComponent(rawPath);
  if (decoded.includes('\0') || decoded.includes('\\')) return null;
  const segments = decoded.split('/').filter((segment) => segment !== '');
  if (segments.some((segment) => segment.startsWith('.'))) return null;
  const target = path.join(root, ...segments);
  if (target !== root && !target.startsWith(`${root}${path.sep}`)) return null;
  return target;
}

/**
 * 通常のファイルなら、そのパスを返す。無い・ディレクトリのときは null を返す。
 * @param {string} file
 * @returns {Promise<string | null>}
 */
async function statRegularFile(file) {
  try {
    const stats = await fs.promises.stat(file);
    return stats.isFile() ? file : null;
  } catch (error) {
    if (error.code === 'ENOENT' || error.code === 'ENOTDIR') return null;
    throw error;
  }
}

/**
 * 配信するファイルを探す。「/」は index.html、拡張子の無いパスは .html を試す（Vercel の cleanUrls と同じ扱い）。
 * @param {string} root
 * @param {string} pathname
 * @returns {Promise<string | null>}
 */
async function locateStaticFile(root, pathname) {
  const target = resolveStaticPath(root, pathname);
  if (target === null) return null;
  if (pathname.endsWith('/')) {
    return statRegularFile(path.join(target, config.indexFileName));
  }
  const direct = await statRegularFile(target);
  if (direct !== null) return direct;
  if (path.extname(target) === '') {
    return statRegularFile(`${target}${config.cleanUrlExtension}`);
  }
  return null;
}

/**
 * 本文を付けずにテキストの応答を返す（エラーの応答に例外・内部パスを含めない）。
 * @param {http.ServerResponse} res
 * @param {number} status
 * @param {string} message
 * @param {Record<string, string>} [headers]
 */
function sendText(res, status, message, headers = {}) {
  res.writeHead(status, {
    'Content-Type': 'text/plain; charset=utf-8',
    'X-Content-Type-Options': 'nosniff',
    ...headers,
  });
  res.end(`${message}\n`);
}

/**
 * ファイルを本文として返す。
 * @param {http.IncomingMessage} req
 * @param {http.ServerResponse} res
 * @param {string} file
 * @param {number} status 応答の状態（通常のファイルは 200、存在しないパスの 404.html は 404）
 */
async function sendStaticFile(req, res, file, status) {
  const stats = await fs.promises.stat(file);
  const extension = path.extname(file);
  // 対応していない拡張子は、既定の型（バイト列）で返す
  const contentType = Object.hasOwn(config.mimeTypes, extension)
    ? config.mimeTypes[extension]
    : config.defaultMimeType;
  res.writeHead(status, {
    'Content-Type': contentType,
    'Content-Length': stats.size,
    'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff',
  });
  if (req.method === 'HEAD') {
    res.end();
    return;
  }
  await pipeline(fs.createReadStream(file), res);
}

/**
 * 接続元のアドレスを、X-Real-IP に入れる形にする（IPv4 射影の接頭辞を外す）。
 * @param {string | undefined} address
 * @returns {string | null}
 */
function normalizeAddress(address) {
  if (typeof address !== 'string' || address === '') return null;
  return address.startsWith('::ffff:') ? address.slice('::ffff:'.length) : address;
}

/**
 * 転送のときに、クライアントから受け取らない・そのまま渡さないヘッダーかどうか。
 * @param {string} name 小文字のヘッダー名
 * @returns {boolean}
 */
function isDroppedRequestHeader(name) {
  return config.hopByHopHeaders.includes(name)
    || name === 'x-real-ip'
    || name === 'forwarded'
    || name.startsWith('x-forwarded-');
}

/**
 * /api の要求を Rails へ転送する。Host・Origin・Cookie・本体・クエリは変えない。
 * @param {http.IncomingMessage} req
 * @param {http.ServerResponse} res
 * @param {{ rails: { host: string, port: number }, upstreamTimeoutMs: number }} ctx
 */
function proxyToRails(req, res, ctx) {
  const clientAddress = normalizeAddress(req.socket.remoteAddress);
  if (clientAddress === null) {
    sendText(res, 502, config.messages.clientAddressMissing);
    return;
  }
  const headers = {};
  for (const [name, value] of Object.entries(req.headers)) {
    if (value === undefined || isDroppedRequestHeader(name)) continue;
    headers[name] = value;
  }
  headers['x-real-ip'] = clientAddress;

  const upstream = http.request({
    host: ctx.rails.host,
    port: ctx.rails.port,
    method: req.method,
    path: req.url,
    headers,
    timeout: ctx.upstreamTimeoutMs,
  });
  let timedOut = false;
  upstream.on('timeout', () => {
    timedOut = true;
    upstream.destroy(new Error('upstream timeout'));
  });
  upstream.on('response', (upstreamRes) => {
    const responseHeaders = {};
    for (const [name, value] of Object.entries(upstreamRes.headers)) {
      if (value !== undefined && !config.hopByHopHeaders.includes(name)) responseHeaders[name] = value;
    }
    if (typeof upstreamRes.statusCode !== 'number') {
      upstreamRes.resume();
      sendText(res, 502, config.messages.upstreamUnavailable);
      return;
    }
    res.writeHead(upstreamRes.statusCode, responseHeaders);
    upstreamRes.pipe(res);
  });
  upstream.on('error', (error) => {
    if (res.headersSent || res.destroyed) {
      res.destroy();
      return;
    }
    // 接続の失敗はエラーコード（例：ECONNREFUSED）を表示する。コードが無いときはメッセージを表示する。
    const detail = typeof error.code === 'string' ? error.code : error.message;
    console.error(config.log.upstreamFailed, detail);
    if (timedOut) {
      sendText(res, 504, config.messages.upstreamTimeout);
    } else {
      sendText(res, 502, config.messages.upstreamUnavailable);
    }
  });
  res.on('close', () => {
    if (!res.writableFinished) upstream.destroy();
  });
  req.pipe(upstream);
}

/**
 * @param {http.IncomingMessage} req
 * @param {http.ServerResponse} res
 * @param {{ root: string, rails: { host: string, port: number }, upstreamTimeoutMs: number }} ctx
 */
async function handleRequest(req, res, ctx) {
  if (typeof req.url !== 'string') {
    sendText(res, 400, config.messages.badRequest);
    return;
  }
  let pathname;
  try {
    pathname = new URL(req.url, config.requestUrlBase).pathname;
  } catch {
    sendText(res, 400, config.messages.badRequest);
    return;
  }
  if (pathname === config.apiPathPrefix || pathname.startsWith(`${config.apiPathPrefix}/`)) {
    proxyToRails(req, res, ctx);
    return;
  }
  if (req.method !== 'GET' && req.method !== 'HEAD') {
    sendText(res, 405, config.messages.methodNotAllowed, { Allow: 'GET, HEAD' });
    return;
  }
  let file;
  try {
    file = await locateStaticFile(ctx.root, pathname);
  } catch (error) {
    if (error instanceof URIError) {
      sendText(res, 400, config.messages.badRequest);
      return;
    }
    throw error;
  }
  if (file === null) {
    // 存在しないパスは 404 の応答で 404.html を返す（Vercel と同じ。本文に内部情報を含めない）
    await sendStaticFile(req, res, path.join(ctx.root, config.notFoundFileName), 404);
    return;
  }
  await sendStaticFile(req, res, file, 200);
}

/**
 * @param {{ root: string, rails: { host: string, port: number }, upstreamTimeoutMs: number }} ctx
 * @returns {http.Server}
 */
function createServer(ctx) {
  return http.createServer((req, res) => {
    handleRequest(req, res, ctx).catch((error) => {
      console.error(error);
      if (res.headersSent) {
        res.destroy();
      } else {
        sendText(res, 500, config.messages.internalError);
      }
    });
  });
}

/**
 * 転送先の URL を、ホストとポートにする。
 * @param {string} origin
 * @returns {{ host: string, port: number }}
 */
function parseRailsOrigin(origin) {
  const url = new URL(origin);
  if (url.protocol !== 'http:') {
    throw new Error(config.messages.railsOriginInvalid);
  }
  return {
    host: url.hostname.replace(/^\[|\]$/g, ''),
    port: url.port === '' ? 80 : Number(url.port),
  };
}

/**
 * @param {http.Server} server
 * @param {number} port
 * @param {string} host
 * @returns {Promise<void>}
 */
function listen(server, port, host) {
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(port, host, () => {
      server.off('error', reject);
      resolve();
    });
  });
}

/**
 * @param {http.Server} server
 * @returns {Promise<void>}
 */
function closeServer(server) {
  return new Promise((resolve, reject) => {
    server.close((error) => (error ? reject(error) : resolve()));
    server.closeIdleConnections();
  });
}

/**
 * 開発用サーバーを起動する。APP_ENV が development でなければ、起動せずに例外を投げる。
 * @param {{ env?: NodeJS.ProcessEnv, host?: string, port?: number, root?: string, railsOrigin?: string }} [options]
 * @returns {Promise<{ url: string, close: () => Promise<void> }>}
 */
export async function startDevServer(options = {}) {
  const env = options.env === undefined ? process.env : options.env;
  assertDevelopmentEnvironment(env);
  const defaultRoot = path.dirname(fileURLToPath(import.meta.url));
  const root = options.root === undefined ? defaultRoot : options.root;
  const railsOrigin = options.railsOrigin === undefined ? config.railsOrigin : options.railsOrigin;
  const ctx = {
    root: path.resolve(root),
    rails: parseRailsOrigin(railsOrigin),
    upstreamTimeoutMs: config.upstreamTimeoutMs,
  };
  const host = options.host === undefined ? config.host : options.host;
  const port = options.port === undefined ? config.port : options.port;
  const server = createServer(ctx);
  await listen(server, port, host);
  const address = server.address();
  if (address === null || typeof address === 'string') {
    throw new Error(config.messages.internalError);
  }
  return {
    url: config.localUrlTemplate.replace('{host}', host).replace('{port}', String(address.port)),
    close: () => closeServer(server),
  };
}

/** コマンドラインからの起動。条件に合わないときは案内を出して終了コード 1 で終わる。 */
async function runCli() {
  let server;
  try {
    server = await startDevServer({ env: process.env });
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
    return;
  }
  console.log(`${config.messages.started} ${server.url}`);
  const stop = () => {
    server.close().finally(() => process.exit(0));
  };
  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);
}

if (process.argv[1] !== undefined && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runCli();
}
