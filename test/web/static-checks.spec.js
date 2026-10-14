// @ts-check
// ソースの静的な検査。文字列の分離・ネイティブのダイアログ・絵文字・ライト固定・外部読み込みの範囲・GA4 の ID を確かめる。
const fs = require('node:fs');
const path = require('node:path');
const { test, expect } = require('@playwright/test');
const support = require('./support');

const JAPANESE = /[　-ヿ㐀-鿿豈-﫿＀-￯]/;
const EMOJI = /\p{Extended_Pictographic}/u;
const CONFIG_FILE_NAME = 'config.js';
const CONFIG_MODULE_FILE_NAMES = ['config.js', 'dev-server.config.mjs'];
const ALLOWED_RESOURCE_HOSTS = ['www.googletagmanager.com', 'cdnjs.cloudflare.com'];
const MEASUREMENT_ID = 'G-C04W1XKS16';

/** @param {string} file */
function relative(file) {
  return path.relative(support.REPO_ROOT, file);
}

/** @param {string[]} files */
function jsFiles(files) {
  return files.filter((file) => /\.(m?js)$/.test(file));
}

test.describe('文字列リテラルの分離（JavaScript の文言は設定ファイルへ）', () => {
  test('設定ファイル以外の JavaScript の文字列リテラルに日本語を書かない', () => {
    const targets = jsFiles(support.listWebSourceFiles()).filter(
      (file) => !CONFIG_MODULE_FILE_NAMES.includes(path.basename(file)),
    );
    expect(targets.length).toBeGreaterThan(0);
    /** @type {string[]} */
    const offenders = [];
    for (const file of targets) {
      const { literals } = support.scanJavaScript(fs.readFileSync(file, 'utf8'));
      for (const literal of literals) {
        if (JAPANESE.test(literal)) offenders.push(`${relative(file)}: ${literal}`);
      }
    }
    expect(offenders).toEqual([]);
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
    const css = fs.readFileSync(path.join(support.WEB_ROOT, 'css', 'site.css'), 'utf8');
    expect(css).toMatch(/color-scheme:\s*light\s*;/);
    for (const page of ['index.html', 'legal.html']) {
      const html = fs.readFileSync(path.join(support.WEB_ROOT, page), 'utf8');
      expect(html, page).toMatch(/<meta name="color-scheme" content="light">/);
    }
  });

  test('フォーカスの見た目（outline）をブラウザの既定から消さない', () => {
    const css = fs.readFileSync(path.join(support.WEB_ROOT, 'css', 'site.css'), 'utf8');
    expect(css).not.toMatch(/outline\s*:\s*(none|0)\b/);
  });
});

test.describe('外部から読み込む範囲', () => {
  test('HTML の script と stylesheet は同一オリジンか許可したホストだけにする', () => {
    for (const page of ['index.html', 'legal.html']) {
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
    const browserDir = path.join(support.WEB_ROOT, 'js');
    const browserFiles = jsFiles(support.listWebSourceFiles()).filter((file) => path.dirname(file) === browserDir);
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
    for (const page of ['index.html', 'legal.html']) {
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
    const parsed = JSON.parse(fs.readFileSync(configFile, 'utf8'));
    expect(parsed.cleanUrls).toBe(true);
  });
});
