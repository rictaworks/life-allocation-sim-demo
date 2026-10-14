// PR 15 のユーザーテストの共通部品。実装（開発用サーバー・Rails の API・画面）を実際に呼び出して検査する。
// 期待する文言・URL・API の形は expectations.json から読む。
const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { expect, request } = require('@playwright/test');
const cfg = require('./expectations.json');

const EMAIL_PATTERN = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g;
const RGB_PATTERN = /rgba?\(([^)]+)\)/;

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// 同じセッションの保存・ゲーム開始は 1 秒 1 件の上限がある。要求の間隔を空ける。
function waitForRateWindow() {
  return sleep(cfg.timeouts.rateLimitWaitMs);
}

function absoluteUrl(pathname) {
  return new URL(pathname, cfg.baseUrl).toString();
}

// 結果の画像を置く場所（WORK/inspect の下。ローカル専用）。
function artifactPath(fileName) {
  const directory = path.resolve(__dirname, cfg.artifactsDir);
  fs.mkdirSync(directory, { recursive: true });
  return path.join(directory, fileName);
}

function readRepoFile(relativeToThisDirectory) {
  return fs.readFileSync(path.resolve(__dirname, relativeToThisDirectory), 'utf8');
}

// 外部への通信を止める。同じオリジン以外は中断し、GA4 の読み込みだけは空の応答を返す。
// blockUnity のときは Unity のローダーを中断し、「ビルドが無い」状態を作る（ファイルは消さない）。
async function isolateContext(context, { blockUnity }) {
  const externalRequests = [];
  const gtmRequests = [];
  await context.route('**/*', (route) => {
    const url = route.request().url();
    if (url.startsWith(cfg.baseUrl)) {
      return route.continue();
    }
    if (url.startsWith(cfg.consent.gtmPrefix)) {
      gtmRequests.push(url);
      return route.fulfill({ status: 200, contentType: 'text/javascript; charset=utf-8', body: '' });
    }
    externalRequests.push(url);
    return route.abort();
  });
  if (blockUnity) {
    await context.route(absoluteUrl(cfg.unity.loaderPath), (route) => route.abort());
  }
  return { externalRequests, gtmRequests };
}

// ブラウザの中で同一オリジンの fetch を行う。Origin と Cookie はブラウザが付ける（利用者の操作と同じ）。
async function browserFetch(page, method, pathname, options = {}) {
  return page.evaluate(
    async ({ method, pathname, body, headers }) => {
      const init = { method, credentials: 'same-origin', headers };
      if (body !== undefined) {
        init.body = body;
      }
      const response = await fetch(pathname, init);
      const text = await response.text();
      return { status: response.status, text, retryAfter: response.headers.get('Retry-After') };
    },
    { method, pathname, body: options.body, headers: options.headers ?? {} }
  );
}

// Playwright の APIRequestContext で要求する。既定の Origin は開発用サーバーの URL（APP_ORIGIN と同じ）。
async function apiRequest(apiContext, method, pathname, options = {}) {
  const headers = { Origin: cfg.baseUrl, ...(options.headers ?? {}) };
  const init = { method, headers, failOnStatusCode: false, timeout: cfg.timeouts.expectMs };
  if (options.json !== undefined) {
    headers['Content-Type'] = 'application/json';
    init.data = JSON.stringify(options.json);
  }
  if (options.rawBody !== undefined) {
    init.data = options.rawBody;
  }
  const response = await apiContext.fetch(absoluteUrl(pathname), init);
  return {
    status: response.status(),
    text: await response.text(),
    headers: response.headers(),
    headersArray: await response.headersArray(),
  };
}

// Cookie を持たない新しい要求の文脈（新規セッションの発行や、古い Cookie の検査に使う）。
function newApiContext() {
  return request.newContext({ baseURL: cfg.baseUrl });
}

// 開発用サーバー経由で Rails に届くか確かめる。届かなければ、前提が足りないことを示して失敗させる。
async function assertServersRunning() {
  const apiContext = await newApiContext();
  let response;
  try {
    response = await apiContext.get(absoluteUrl(cfg.api.paths.game), {
      failOnStatusCode: false,
      timeout: cfg.timeouts.expectMs,
    });
  } catch (error) {
    throw new Error(`${cfg.precondition}（${error.message}）`);
  } finally {
    await apiContext.dispose();
  }
  if (response.status() !== 200) {
    throw new Error(`${cfg.precondition}（GET ${cfg.api.paths.game} の応答は ${response.status()} でした）`);
  }
}

// 応答の seed を、JSON の数値の精度を落とさずに文字列で取り出す。
function seedOf(text) {
  const match = /"seed":(\d+)/.exec(text);
  expect(match, '応答に整数の seed がある').not.toBeNull();
  return match[1];
}

function randomCookieValue() {
  return crypto.randomBytes(32).toString('base64url');
}

function readLocalStorage(page, key) {
  return page.evaluate((storageKey) => window.localStorage.getItem(storageKey), key);
}

// gtag.js が読む dataLayer の中身を、配列の配列として読む。
function readDataLayer(page) {
  return page.evaluate(() => (window.dataLayer || []).map((entry) => Array.from(entry)));
}

// 「rgb(r, g, b)」の文字列を [r, g, b] にする。
function parseRgb(computed) {
  const match = RGB_PATTERN.exec(computed);
  expect(match, `色の形式（${computed}）`).not.toBeNull();
  return match[1].split(',').slice(0, 3).map((part) => Number(part.trim()));
}

// WCAG 2.2 のコントラスト比（相対輝度の定義どおり）。
function contrastRatio(foreground, background) {
  const luminance = ([red, green, blue]) => {
    const linear = (channel) => {
      const scaled = channel / 255;
      return scaled <= 0.03928 ? scaled / 12.92 : ((scaled + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue);
  };
  const [lighter, darker] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (lighter + 0.05) / (darker + 0.05);
}

// 画像（PNG のバイト列）の明るい画素の割合（輝度 128 超）を返す。ページの中で復号し、160×90 に縮めて数える。
// Unity のスプラッシュは暗く（割合は約 0）、タイトル画面は明るい背景（割合は約 1）になる（校正は WORK/inspect/）。
async function brightPixelFraction(page, pngBuffer) {
  return page.evaluate(async (base64) => {
    const bytes = Uint8Array.from(atob(base64), (character) => character.charCodeAt(0));
    const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
    const canvas = document.createElement('canvas');
    canvas.width = 160;
    canvas.height = 90;
    const context = canvas.getContext('2d');
    context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
    const data = context.getImageData(0, 0, canvas.width, canvas.height).data;
    let bright = 0;
    for (let index = 0; index < data.length; index += 4) {
      const luminance = 0.2126 * data[index] + 0.7152 * data[index + 1] + 0.0722 * data[index + 2];
      if (luminance > 128) {
        bright += 1;
      }
    }
    return bright / (data.length / 4);
  }, pngBuffer.toString('base64'));
}

// フッターのリンクが、別の要素（相談ボタンなど）に隠れていないことを、その中心の点で確かめる。
async function expectFooterLinksClickable(page) {
  const footer = page.getByRole('contentinfo');
  for (const item of cfg.page.footerLinks) {
    const link = footer.getByRole('link', { name: item.text, exact: true });
    await link.scrollIntoViewIfNeeded();
    const reachable = await link.evaluate((element) => {
      const rect = element.getBoundingClientRect();
      const top = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
      return top !== null && element.contains(top);
    });
    expect(reachable, `フッターの「${item.text}」が別の要素に隠れている`).toBe(true);
  }
}

module.exports = {
  cfg,
  sleep,
  waitForRateWindow,
  absoluteUrl,
  artifactPath,
  readRepoFile,
  isolateContext,
  browserFetch,
  apiRequest,
  newApiContext,
  assertServersRunning,
  seedOf,
  randomCookieValue,
  readLocalStorage,
  readDataLayer,
  parseRgb,
  contrastRatio,
  brightPixelFraction,
  expectFooterLinksClickable,
  EMAIL_PATTERN,
};
