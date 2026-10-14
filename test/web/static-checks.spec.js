// @ts-check
// ソースの静的な検査。文字列の分離・ネイティブのダイアログ・絵文字・ライト固定・外部読み込みの範囲・GA4 の ID・配信設定・テストの実行設定を確かめる。
const fs = require('node:fs');
const path = require('node:path');
const { test, expect } = require('@playwright/test');
const support = require('./support');

const JAPANESE = /[　-ヿ㐀-鿿豈-﫿＀-￯]/;
const NON_ASCII = /[^\x00-\x7F]/;
const LETTER = /\p{L}/u;
const URL_WITH_SCHEME = /[A-Za-z][A-Za-z0-9+.-]*:\/\//;
const EMOJI = /\p{Extended_Pictographic}/u;
const CONFIG_FILE_NAME = 'config.js';
const CONFIG_MODULE_FILE_NAMES = ['config.js', 'dev-server.config.mjs'];
const ALLOWED_RESOURCE_HOSTS = ['www.googletagmanager.com', 'cdnjs.cloudflare.com'];
const MEASUREMENT_ID = 'G-C04W1XKS16';
const BROWSER_DIR = path.join(support.WEB_ROOT, 'js');
const SITE_CSS = path.join(support.WEB_ROOT, 'css', 'site.css');

/**
 * js/ のブラウザ用 JavaScript で、文字を含んでよい技術的な値の許可リスト（値 → 理由）。
 * ここに無い文字列は、利用者に見える文言・パス・URL とみなし、js/config.js へ移す。
 * 要素の id は HTML の id と一致させる。値を変えるときは、HTML と JavaScript を両方直す。
 */
const TECHNICAL_LITERALS = Object.freeze({
  './config.js': 'モジュールの読み込み先（ファイル名）',
  './consent.js': 'モジュールの読み込み先（ファイル名）',
  './dom.js': 'モジュールの読み込み先（ファイル名）',
  './unity-launcher.js': 'モジュールの読み込み先（ファイル名）',
  'unity-canvas': '描画領域の要素の id（index.html）',
  'unity-status': '読み込み表示の要素の id（index.html）',
  'consent-banner': '同意バナーの要素の id（index.html・legal.html・404.html）',
  'consent-title': '同意バナーの題名の要素の id（同）',
  'consent-reopen': '「設定を変更」ボタンの要素の id（同）',
  main: '本文の要素の id（同）',
  '[data-consent-choice]': '同意の選択ボタンを探す属性のセレクタ（同）',
  script: '作る要素の種類（gtag.js と Unity のローダーを読み込む script 要素）',
  click: 'DOM のイベント名（ボタンの操作）',
  load: 'DOM のイベント名（読み込みの完了）',
  error: 'DOM のイベント名（読み込みの失敗）',
  function: 'typeof の結果（型名）',
  consent: 'gtag の命令名（Google の仕様。同意の既定と更新）',
  default: 'gtag の命令名（Google の仕様。同意の既定）',
  update: 'gtag の命令名（Google の仕様。同意の更新）',
  js: 'gtag の命令名（Google の仕様。計測の開始）',
  config: 'gtag の命令名（Google の仕様。測定 ID の設定）',
  '?id=': 'gtag.js の読み込み先に付ける問い合わせ名（Google の仕様。測定 ID を渡す）',
});

/**
 * src/web/dev-server.mjs で、英字を含んでよい技術的な値の許可リスト（値 → 理由）。
 * HTTP のヘッダー名・メソッド名・MIME の種類・エラーコード・URL の部品は dev-server.config.mjs に書きます。
 * ここには、設定へ移せない固定の語（import の指定・typeof の結果・Node のイベント名・シグナル名）だけを書きます。
 */
const DEV_SERVER_TECHNICAL_LITERALS = Object.freeze({
  'node:fs': 'Node の組み込みモジュールの名前（import 文の指定。静的な import には文字列の直書きが要る）',
  'node:http': 'Node の組み込みモジュールの名前（同上）',
  'node:path': 'Node の組み込みモジュールの名前（同上）',
  'node:stream/promises': 'Node の組み込みモジュールの名前（同上）',
  'node:url': 'Node の組み込みモジュールの名前（同上）',
  './dev-server.config.mjs': 'モジュールの読み込み先（ファイル名）',
  string: 'typeof の結果（JavaScript の言語で決まっている型名）',
  number: 'typeof の結果（JavaScript の言語で決まっている型名）',
  timeout: 'Node の HTTP 要求のイベント名（時間切れ）',
  response: 'Node の HTTP 要求のイベント名（転送先からの応答）',
  error: 'Node のイベント名（エラー。サーバー・要求・プロセスで使う）',
  close: 'Node のイベント名（応答やストリームの終了）',
  SIGINT: 'Node のプロセスのシグナル名（割り込み。Ctrl+C）',
  SIGTERM: 'Node のプロセスのシグナル名（終了の要求）',
});

/** @param {string} file */
function relative(file) {
  return path.relative(support.REPO_ROOT, file);
}

/** @param {string[]} files */
function jsFiles(files) {
  return files.filter((file) => /\.(m?js)$/.test(file));
}

/** 設定ファイル以外の JavaScript（開発用サーバーを含む）。 */
function nonConfigScripts() {
  return jsFiles(support.listWebSourceFiles()).filter((file) => !CONFIG_MODULE_FILE_NAMES.includes(path.basename(file)));
}

/** js/ のブラウザ用 JavaScript（設定ファイルを除く）。 */
function browserScripts() {
  return nonConfigScripts().filter((file) => path.dirname(file) === BROWSER_DIR);
}

/** 開発用サーバーなど、Node で動く JavaScript（js/ の外。設定ファイルを除く）。 */
function serverScripts() {
  return nonConfigScripts().filter((file) => path.dirname(file) !== BROWSER_DIR);
}

/**
 * 文字列リテラルの静的な部分を取り出す（テンプレート文字列の ${...} の部分を除く）。
 * @param {string} literal
 * @returns {string[]}
 */
function staticParts(literal) {
  return literal.split(/\$\{[^}]*\}/);
}

// 文字列の逃がし記号（\n のような表記。Unicode や 16 進の表記も含む）。
const ESCAPE_SEQUENCE = /\\(?:u\{([0-9a-fA-F]+)\}|u([0-9a-fA-F]{4})|x([0-9a-fA-F]{2})|([\s\S]))/g;
const CONTROL_ESCAPES = Object.freeze({ 0: '\0', b: '\b', f: '\f', n: '\n', r: '\r', t: '\t', v: '\v' });

/**
 * 文字列の逃がし記号を、表す文字に直します（英字の検査が \n の n などを英字と数えないようにします）。
 * @param {string} raw
 * @returns {string}
 */
function decodeEscapes(raw) {
  return raw.replace(ESCAPE_SEQUENCE, (_match, braced, unicode, hex, other) => {
    if (braced !== undefined) return String.fromCodePoint(Number.parseInt(braced, 16));
    if (unicode !== undefined) return String.fromCharCode(Number.parseInt(unicode, 16));
    if (hex !== undefined) return String.fromCharCode(Number.parseInt(hex, 16));
    return Object.hasOwn(CONTROL_ESCAPES, other) ? CONTROL_ESCAPES[other] : other;
  });
}

/** @param {string} file */
function readJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

/**
 * 英字（文字）を含む文字列リテラルの静的な部分を、許可リストと照らします。
 * 許可リストに無い部分は offenders に、使われた許可リストの値は used に入れます。
 * @param {string[]} files
 * @param {Readonly<Record<string, string>>} allowlist
 * @returns {{ offenders: string[], used: Set<string> }}
 */
function checkLetterLiterals(files, allowlist) {
  /** @type {string[]} */
  const offenders = [];
  /** @type {Set<string>} */
  const used = new Set();
  for (const file of files) {
    const { literals } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
    for (const literal of literals) {
      for (const part of staticParts(literal).map((raw) => decodeEscapes(raw))) {
        if (!LETTER.test(part)) continue;
        if (Object.hasOwn(allowlist, part)) {
          used.add(part);
        } else {
          offenders.push(`${relative(file)}: ${JSON.stringify(part)}`);
        }
      }
    }
  }
  return { offenders, used };
}

test.describe('文字列リテラルの分離（JavaScript の文言は設定ファイルへ）', () => {
  test('設定ファイル以外の JavaScript に、日本語などの非 ASCII の文字列を書かない', () => {
    const targets = nonConfigScripts();
    expect(targets.length).toBeGreaterThan(0);
    /** @type {string[]} */
    const offenders = [];
    for (const file of targets) {
      const { literals } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      for (const literal of literals) {
        if (NON_ASCII.test(literal)) offenders.push(`${relative(file)}: ${literal}`);
      }
    }
    expect(offenders).toEqual([]);
  });

  test('js/ のブラウザ用 JavaScript は、文字を含む文字列を許可リストの技術的な値だけにする（英語の文・文言・パス・URL を含む）', () => {
    const targets = browserScripts();
    expect(targets.length).toBeGreaterThan(0);
    const { offenders, used } = checkLetterLiterals(targets, TECHNICAL_LITERALS);
    expect(offenders, '許可リストに無い文字列（js/config.js へ移す。技術的な値なら許可リストに理由を書く）').toEqual([]);
    expect(Object.keys(TECHNICAL_LITERALS).filter((value) => !used.has(value)), '使われていない許可リストの値').toEqual([]);
  });

  test('開発用サーバー（src/web/dev-server.mjs）も、文字を含む文字列を許可リストの技術的な値だけにする（HTTP の名前・値・MIME は dev-server.config.mjs へ移す）', () => {
    const targets = serverScripts();
    expect(targets, relative(support.DEV_SERVER_PATH)).toContain(support.DEV_SERVER_PATH);
    const { offenders, used } = checkLetterLiterals(targets, DEV_SERVER_TECHNICAL_LITERALS);
    expect(offenders, '許可リストに無い文字列（dev-server.config.mjs へ移す。移せない固定の語なら許可リストに理由を書く）').toEqual([]);
    expect(Object.keys(DEV_SERVER_TECHNICAL_LITERALS).filter((value) => !used.has(value)), '使われていない許可リストの値').toEqual([]);
  });

  test('許可リストの値には、それぞれ理由を書く', () => {
    for (const list of [TECHNICAL_LITERALS, DEV_SERVER_TECHNICAL_LITERALS]) {
      for (const [value, reason] of Object.entries(list)) {
        expect(reason.trim(), JSON.stringify(value)).not.toBe('');
      }
    }
  });

  test('英字の検査は、文字列の逃がし記号を表す文字に直してから読む', () => {
    expect(decodeEscapes('\\n')).toBe('\n');
    expect(decodeEscapes('\\u0041')).toBe('A');
    expect(decodeEscapes('\\x41')).toBe('A');
    expect(decodeEscapes('\\u{41}')).toBe('A');
    expect(decodeEscapes('\\\\')).toBe('\\');
    expect(decodeEscapes('a\\0b')).toBe('a\0b');
  });

  test('設定ファイル以外の JavaScript（開発用サーバーを含む）に、URL（スキームと区切りを含む文字列）を直書きしない', () => {
    /** @type {string[]} */
    const offenders = [];
    for (const file of nonConfigScripts()) {
      const { literals } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      for (const literal of literals) {
        if (URL_WITH_SCHEME.test(literal)) offenders.push(`${relative(file)}: ${literal}`);
      }
    }
    expect(offenders, 'URL は js/config.js か dev-server.config.mjs に書く').toEqual([]);
  });

  test('画面の文言は js/config.js に日本語で集まっている', async () => {
    const config = await support.loadConfig();
    expect(config.texts.unity.missingBuild).toMatch(JAPANESE);
    expect(config.texts.consent.saveFailed).toMatch(JAPANESE);
    expect(config.texts.consent.readFailed).toMatch(JAPANESE);
  });
});

test.describe('禁止事項', () => {
  test('alert・confirm・prompt を JavaScript のコードで使わない', () => {
    for (const file of jsFiles(support.listWebSourceFiles())) {
      const { code } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      expect(code, relative(file)).not.toMatch(/\b(alert|confirm|prompt)\s*\(/);
    }
  });

  test('絵文字を使わない（HTML・CSS・JavaScript すべて）', () => {
    for (const file of support.listWebSourceFiles()) {
      const text = fs.readFileSync(file, 'utf8');
      expect(text, relative(file)).not.toMatch(EMOJI);
    }
  });

  test('トップレベルに var・let・const を置かない（設定ファイル以外）', () => {
    for (const file of jsFiles(support.listWebSourceFiles())) {
      if (CONFIG_MODULE_FILE_NAMES.includes(path.basename(file))) continue;
      const { code } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      expect(code, relative(file)).not.toMatch(/^(var|let|const)\s/m);
    }
  });

  test('window への代入は GA4 の dataLayer だけにする', () => {
    for (const file of jsFiles(support.listWebSourceFiles())) {
      const { code } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      const assigned = [...code.matchAll(/\bwindow\.([A-Za-z_$][\w$]*)\s*=(?!=)/g)].map((m) => m[1]);
      for (const name of assigned) {
        expect(['dataLayer', 'createUnityInstance'], `${relative(file)}: window.${name}`).toContain(name);
      }
    }
  });
});

test.describe('ライト固定', () => {
  test('color-scheme を light にし、prefers-color-scheme の分岐を作らない', () => {
    for (const file of support.listWebSourceFiles()) {
      const text = fs.readFileSync(file, 'utf8');
      expect(text, relative(file)).not.toMatch(/prefers-color-scheme/);
    }
    const css = fs.readFileSync(SITE_CSS, 'utf8');
    expect(css).toMatch(/color-scheme:\s*light\s*;/);
    for (const page of support.PAGE_FILES) {
      const html = fs.readFileSync(path.join(support.WEB_ROOT, page), 'utf8');
      expect(html, page).toMatch(/<meta name="color-scheme" content="light">/);
    }
  });

  test('フォーカスの見た目（outline）をブラウザの既定から消さない', () => {
    const css = fs.readFileSync(SITE_CSS, 'utf8');
    expect(css).not.toMatch(/outline\s*:\s*(none|0)\b/);
  });
});

test.describe('外部から読み込む範囲', () => {
  test('HTML の script と stylesheet は同一オリジンか許可したホストだけにする', () => {
    for (const page of support.PAGE_FILES) {
      const html = fs.readFileSync(path.join(support.WEB_ROOT, page), 'utf8');
      const sources = [
        ...html.matchAll(/<script\b[^>]*\ssrc="([^"]+)"/g),
        ...html.matchAll(/<link\b[^>]*\shref="([^"]+)"[^>]*>/g),
      ].map((m) => m[1]);
      for (const source of sources) {
        if (!/^https?:\/\//.test(source)) continue;
        const host = new URL(source).host;
        expect(ALLOWED_RESOURCE_HOSTS, `${page}: ${source}`).toContain(host);
      }
    }
  });

  test('ブラウザで動く JavaScript（js/ の下）が読み込む絶対 URL は、許可したホストだけにする', () => {
    const browserFiles = browserScripts();
    expect(browserFiles.length).toBeGreaterThan(0);
    for (const file of browserFiles) {
      const { literals } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      for (const literal of literals) {
        if (!/^https?:\/\//.test(literal)) continue;
        expect(ALLOWED_RESOURCE_HOSTS, `${relative(file)}: ${literal}`).toContain(new URL(literal).host);
      }
    }
  });

  test('アイコンの読み込み（Font Awesome）は使っていない', () => {
    for (const file of support.listWebSourceFiles()) {
      const text = fs.readFileSync(file, 'utf8');
      expect(text, relative(file)).not.toMatch(/font-?awesome/i);
    }
  });
});

test.describe('設定値（契約と一致すること）', () => {
  test('GA4 の測定 ID は js/config.js にだけ書く', async () => {
    const config = await support.loadConfig();
    expect(config.analytics.measurementId).toBe(MEASUREMENT_ID);
    for (const file of support.listWebSourceFiles()) {
      if (path.basename(file) === CONFIG_FILE_NAME) continue;
      const text = fs.readFileSync(file, 'utf8');
      expect(text, relative(file)).not.toContain(MEASUREMENT_ID);
    }
  });

  test('Unity の読み込み先は契約どおりの名前にする（unity/Build/ 配下）', async () => {
    const config = await support.loadConfig();
    expect(config.unity.loaderUrl).toBe('unity/Build/LifeAllocationSim.loader.js');
    expect(config.unity.dataUrl).toBe('unity/Build/LifeAllocationSim.data');
    expect(config.unity.frameworkUrl).toBe('unity/Build/LifeAllocationSim.framework.js');
    expect(config.unity.codeUrl).toBe('unity/Build/LifeAllocationSim.wasm');
    expect(config.unity.streamingAssetsUrl).toBe('unity/StreamingAssets');
    expect(config.unity.productName).toBe('LifeAllocationSim');
  });

  test('連絡先は info@rictaworks.jp だけを書く（個人のメールアドレスを書かない）', () => {
    for (const page of support.PAGE_FILES) {
      const html = fs.readFileSync(path.join(support.WEB_ROOT, page), 'utf8');
      const emails = [...new Set(html.match(/[\w.+-]+@[\w.-]+\.[a-z]+/gi) ?? [])];
      for (const email of emails) expect(email, page).toBe('info@rictaworks.jp');
    }
  });
});

test.describe('デプロイ設定（Vercel）', () => {
  test('src/web/vercel.json は cleanUrls を true にし、/legal で legal.html を配る', () => {
    const configFile = path.join(support.WEB_ROOT, 'vercel.json');
    expect(fs.existsSync(configFile), relative(configFile)).toBe(true);
    expect(fs.existsSync(path.join(support.WEB_ROOT, 'legal.html'))).toBe(true);
    const parsed = readJson(configFile);
    expect(parsed.cleanUrls).toBe(true);
  });

  test('存在しないパスの 404 は 404.html で返す（Vercel は /404.html を使う。rewrites で扱いを変えない）', () => {
    expect(fs.existsSync(path.join(support.WEB_ROOT, '404.html')), '404.html').toBe(true);
    const parsed = readJson(path.join(support.WEB_ROOT, 'vercel.json'));
    expect(parsed.rewrites, 'rewrites で 404 の扱いを変えない').toBeUndefined();
  });

  test('src/web/vercel.json は全パスに安全なヘッダーを付ける（nosniff・Referrer-Policy・X-Frame-Options）', () => {
    const parsed = readJson(path.join(support.WEB_ROOT, 'vercel.json'));
    expect(Array.isArray(parsed.headers), 'headers の規則').toBe(true);
    const rule = parsed.headers.find((/** @type {{ source: string }} */ entry) => entry.source === '/(.*)');
    expect(rule, '全パス（/(.*)）の規則').toBeDefined();
    const values = Object.fromEntries(rule.headers.map((/** @type {{ key: string, value: string }} */ header) => [header.key, header.value]));
    expect(values['X-Content-Type-Options']).toBe('nosniff');
    expect(values['Referrer-Policy']).toBe('strict-origin-when-cross-origin');
    expect(values['X-Frame-Options']).toBe('DENY');
  });
});

test.describe('テストの実行設定（test/package.json）', () => {
  test('npm の test は test/web だけを対象にし、開発用サーバーが要る pr15 は別の script にする', () => {
    const pkg = readJson(path.join(support.REPO_ROOT, 'test', 'package.json'));
    expect(pkg.scripts.test, 'test の script').toMatch(/\bweb\b/);
    expect(pkg.scripts.test, 'test から pr15 を外す').not.toMatch(/pr15/);
    expect(pkg.scripts['test:pr15'], 'pr15 の script').toMatch(/-c pr15\/playwright\.config\.js/);
  });

  test('HTML の検査（validate:html）は、配信するすべてのページを対象にする', () => {
    const pkg = readJson(path.join(support.REPO_ROOT, 'test', 'package.json'));
    for (const page of support.PAGE_FILES) {
      expect(pkg.scripts['validate:html'], page).toContain(page);
    }
  });
});

test.describe('CSS', () => {
  test('定義した変数（--）は、どこかで使う（使われていない変数を残さない）', () => {
    const css = fs.readFileSync(SITE_CSS, 'utf8');
    const defined = [...css.matchAll(/^\s*(--[\w-]+)\s*:/gm)].map((m) => m[1]);
    expect(defined.length).toBeGreaterThan(0);
    for (const name of defined) {
      expect(css, name).toContain(`var(${name})`);
    }
  });
});
