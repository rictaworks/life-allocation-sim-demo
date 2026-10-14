// @ts-check
// test/web の共通処理。開発用サーバーと設定は実装ファイルを直接読み込み、テストごとに起動と停止を行う。
const childProcess = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

const REPO_ROOT = path.resolve(__dirname, '..', '..');
const WEB_ROOT = path.join(REPO_ROOT, 'src', 'web');
const DEV_SERVER_PATH = path.join(WEB_ROOT, 'dev-server.mjs');
const LOCAL_HOST = '127.0.0.1';
// 接続先のない Rails の代わり（discard 用のポート。接続は拒否される）
const UNREACHABLE_ORIGIN = 'http://127.0.0.1:9';
// src/web の HTML ページ（Vercel に配るページと、存在しないパスの 404 ページ）
const PAGE_FILES = Object.freeze(['index.html', 'legal.html', '404.html']);

/**
 * dataLayer の内容を配列にして読む（arguments は配列にする）。
 * @param {import('@playwright/test').Page} page
 * @returns {Promise<any[][]>}
 */
function readDataLayer(page) {
  return page.evaluate(() => Array.from(window.dataLayer ?? [], (entry) => Array.from(entry)));
}

/**
 * src/web の ES モジュールを読み込む。
 * @param {string} relativePath src/web からの相対パス
 * @returns {Promise<any>}
 */
function importWebModule(relativePath) {
  return import(pathToFileURL(path.join(WEB_ROOT, relativePath)).href);
}

/**
 * 画面の文言と設定（src/web/js/config.js）を読み込む。
 * @returns {Promise<any>}
 */
async function loadConfig() {
  const module = await importWebModule('js/config.js');
  return module.config;
}

/**
 * APP_ENV=development で開発用サーバーを起動する。ポートは空きポート（0）を使う。
 * @param {{ railsOrigin?: string }} [options]
 * @returns {Promise<{ url: string, close: () => Promise<void> }>}
 */
async function startDevServer(options = {}) {
  const { startDevServer: start } = await importWebModule('dev-server.mjs');
  return start({
    env: { APP_ENV: 'development' },
    host: LOCAL_HOST,
    port: 0,
    root: WEB_ROOT,
    railsOrigin: options.railsOrigin ?? UNREACHABLE_ORIGIN,
  });
}

/**
 * 受け取ったリクエストを記録する Rails の代役。
 * @returns {Promise<{ origin: string, received: Array<{ method: string, url: string, headers: Record<string, string | string[] | undefined>, body: string }>, close: () => Promise<void> }>}
 */
function startFakeRails() {
  /** @type {Array<{ method: string, url: string, headers: Record<string, string | string[] | undefined>, body: string }>} */
  const received = [];
  const server = http.createServer((req, res) => {
    /** @type {Buffer[]} */
    const chunks = [];
    req.on('data', (chunk) => chunks.push(chunk));
    req.on('end', () => {
      const body = Buffer.concat(chunks).toString('utf8');
      received.push({ method: req.method ?? '', url: req.url ?? '', headers: req.headers, body });
      const status = req.method === 'POST' ? 201 : 200;
      res.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'X-Fake-Rails': 'yes' });
      res.end(JSON.stringify({ ok: true, method: req.method }));
    });
  });
  return new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(0, LOCAL_HOST, () => {
      const address = server.address();
      if (address === null || typeof address === 'string') {
        reject(new Error('fake rails address unavailable'));
        return;
      }
      resolve({
        origin: `http://${LOCAL_HOST}:${address.port}`,
        received,
        close: () => new Promise((done) => {
          server.close(() => done());
          server.closeIdleConnections();
        }),
      });
    });
  });
}

/**
 * 開発用サーバーの CLI を実行する（同期）。
 * @param {NodeJS.ProcessEnv} env
 * @returns {childProcess.SpawnSyncReturns<string>}
 */
function runDevServerCli(env) {
  return childProcess.spawnSync(process.execPath, [DEV_SERVER_PATH], {
    env,
    encoding: 'utf8',
    timeout: 15000,
  });
}

/**
 * src/web 配下のテキストファイル（unity/ の生成物を除く）を再帰的に列挙する。
 * @param {string} dir
 * @returns {string[]}
 */
function listWebSourceFiles(dir = WEB_ROOT) {
  /** @type {string[]} */
  const files = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === 'unity') continue;
      files.push(...listWebSourceFiles(full));
    } else if (entry.isFile()) {
      files.push(full);
    }
  }
  return files;
}

/**
 * JavaScript のソースから、コメントを除いたコードと文字列リテラルの中身を取り出す（簡易の字句解析）。
 * 正規表現リテラルは扱わない。テスト対象のコードでは使わない。
 * @param {string} source
 * @returns {{ code: string, literals: string[] }}
 */
function scanJavaScript(source) {
  /** @type {string[]} */
  const literals = [];
  let code = '';
  let i = 0;
  const n = source.length;
  while (i < n) {
    const ch = source[i];
    const next = source[i + 1];
    if (ch === '/' && next === '/') {
      const end = source.indexOf('\n', i);
      i = end === -1 ? n : end;
      continue;
    }
    if (ch === '/' && next === '*') {
      const end = source.indexOf('*/', i + 2);
      i = end === -1 ? n : end + 2;
      code += ' ';
      continue;
    }
    if (ch === '"' || ch === "'" || ch === '`') {
      let j = i + 1;
      let value = '';
      while (j < n && source[j] !== ch) {
        if (source[j] === '\\') {
          value += source[j] + (source[j + 1] ?? '');
          j += 2;
          continue;
        }
        if (ch !== '`' && source[j] === '\n') break;
        value += source[j];
        j += 1;
      }
      literals.push(value);
      code += `${ch}${value}${ch}`;
      i = j + 1;
      continue;
    }
    code += ch;
    i += 1;
  }
  return { code, literals };
}

module.exports = {
  REPO_ROOT,
  WEB_ROOT,
  DEV_SERVER_PATH,
  PAGE_FILES,
  importWebModule,
  loadConfig,
  startDevServer,
  startFakeRails,
  runDevServerCli,
  listWebSourceFiles,
  scanJavaScript,
  readDataLayer,
  UNREACHABLE_ORIGIN,
};
