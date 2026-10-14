// @ts-check
// Web 殻（src/web の画面）の検査。ブラウザで表示・操作・同意・Unity の読み込みの扱いを確かめる。
const { test, expect } = require('@playwright/test');
const support = require('./support');

const { readDataLayer } = support;
const BANNER_TEXT = 'これはデモ版です。データはサーバー再起動時にリセットされる場合があります。';
const FICTION_TEXT = '利回り・イベント・物価は、すべて架空のモデル値です。';
const CONSENT_REGION_NAME = 'アクセス解析の利用について';
const UNITY_BUILD_PATTERN = '**/unity/Build/**';
const LOADER_PATH_PATTERN = '**/unity/Build/LifeAllocationSim.loader.js';
const GTM_PATTERN = /^https:\/\/www\.googletagmanager\.com\//;

/**
 * "rgb(r, g, b)" 形式の色を [r, g, b] にする。
 * @param {string} text
 * @returns {number[]}
 */
function parseColor(text) {
  const match = text.match(/rgba?\(([^)]+)\)/);
  if (match === null) throw new Error(`色を解釈できません: ${text}`);
  return match[1].split(',').slice(0, 3).map((part) => Number(part.trim()));
}

/**
 * WCAG の相対輝度。
 * @param {number[]} rgb
 * @returns {number}
 */
function luminance(rgb) {
  const [r, g, b] = rgb.map((channel) => {
    const value = channel / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/**
 * WCAG のコントラスト比。
 * @param {number[]} a
 * @param {number[]} b
 * @returns {number}
 */
function contrastRatio(a, b) {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
}

/**
 * 2 つの矩形が重なるかどうか。
 * @param {{ x: number, y: number, width: number, height: number } | null} a
 * @param {{ x: number, y: number, width: number, height: number } | null} b
 * @returns {boolean}
 */
function intersects(a, b) {
  if (a === null || b === null) return false;
  return a.x < b.x + b.width && b.x < a.x + a.width && a.y < b.y + b.height && b.y < a.y + a.height;
}

/**
 * 要素の計算済みスタイルの一部を取り出す。
 * @param {import('@playwright/test').Locator} locator
 * @param {string[]} names
 * @returns {Promise<Record<string, string>>}
 */
function styleOf(locator, names) {
  return locator.evaluate((el, props) => {
    const style = getComputedStyle(el);
    return Object.fromEntries(props.map((name) => [name, style.getPropertyValue(name)]));
  }, names);
}

/**
 * ページ内の未処理の例外を集める。
 * @param {import('@playwright/test').Page} page
 * @returns {string[]}
 */
function collectPageErrors(page) {
  /** @type {string[]} */
  const errors = [];
  page.on('pageerror', (error) => errors.push(error.message));
  return errors;
}

/**
 * Unity の読み込み先（unity/Build/ 以下）への要求を 404 にして、ビルドが無い状態を再現する。
 * ビルドが置かれているかどうかには関係なく、この route が unity/Build/ 以下の要求を受け持つ。
 * パターンが外れると、ビルドの有無で結果が変わってしまう。そのため止めた要求の URL を返し、テストで確かめる。
 * @param {import('@playwright/test').Page} page
 * @returns {Promise<string[]>} 止めた要求の URL
 */
async function hideUnityBuild(page) {
  /** @type {string[]} */
  const blocked = [];
  await page.route(UNITY_BUILD_PATTERN, (route) => {
    blocked.push(route.request().url());
    return route.fulfill({ status: 404 });
  });
  return blocked;
}

/**
 * Unity の読み込み先を、動かないスタブに置き換える（実際のビルドは使わない）。
 * 先に unity/Build/ 全体を 404 にしてから、ローダーだけをスタブで返す（後から登録した route が先に動く）。
 * 置かれているビルドを読まないので、結果はビルドの有無で変わらない。
 * @param {import('@playwright/test').Page} page
 * @param {string} body
 */
async function stubUnityLoader(page, body) {
  await hideUnityBuild(page);
  await page.route(LOADER_PATH_PATTERN, (route) => route.fulfill({
    status: 200,
    contentType: 'text/javascript',
    body,
  }));
}

test.describe('Web 殻', () => {
  /** @type {{ url: string, close: () => Promise<void> }} */
  let dev;
  /** @type {any} */
  let config;

  test.beforeAll(async () => {
    dev = await support.startDevServer();
    config = await support.loadConfig();
  });

  test.afterAll(async () => {
    await dev.close();
  });

  test.beforeEach(async ({ page }) => {
    // 外部の計測スクリプトは実際には読み込まない（検査は読み込みの要求だけを見る）
    await page.route(GTM_PATTERN, (route) => route.fulfill({
      status: 200,
      contentType: 'text/javascript',
      body: '/* 検査用のスタブ */',
    }));
  });

  test('ページは日本語で、タイトルを持ち、ライト固定である', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await expect(page.locator('html')).toHaveAttribute('lang', 'ja');
    await expect(page).toHaveTitle(/人生配分シミュレーション/);
    const scheme = await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);
    expect(scheme).toBe('light');
    await page.emulateMedia({ colorScheme: 'dark' });
    const background = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(background).toBe('rgb(255, 255, 255)');
  });

  test('最上部にデモ版のバナーを出し、文字と背景は 4.5:1 以上のコントラストにする', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const banner = page.locator('.demo-banner');
    await expect(banner).toHaveText(BANNER_TEXT);
    const box = await banner.boundingBox();
    expect(box).not.toBeNull();
    expect(box.y).toBeLessThanOrEqual(1);
    const colors = await styleOf(banner, ['color', 'background-color']);
    expect(contrastRatio(parseColor(colors.color), parseColor(colors['background-color']))).toBeGreaterThanOrEqual(4.5);
  });

  test('ナビの右端に「← デモ一覧へ」を置き、リンク先は rictaworks.jp の一覧にする', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const nav = page.getByRole('navigation', { name: '主要' });
    const back = nav.getByRole('link', { name: '← デモ一覧へ' });
    await expect(back).toHaveAttribute('href', 'https://rictaworks.jp/#demos');
    await expect(nav.getByRole('link').last()).toHaveText('← デモ一覧へ');
    const navBox = await nav.boundingBox();
    const backBox = await back.boundingBox();
    expect(navBox).not.toBeNull();
    expect(backBox).not.toBeNull();
    // ナビの右の余白（16px）を除いた右端に寄せる
    expect(Math.abs(navBox.x + navBox.width - 16 - (backBox.x + backBox.width))).toBeLessThanOrEqual(1);
  });

  test('右下に固定の「ご相談はこちら」を置き、リンク先は rictaworks.jp にする', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await page.goto(`${dev.url}/`);
    const consult = page.getByRole('link', { name: 'ご相談はこちら' });
    await expect(consult).toHaveAttribute('href', 'https://rictaworks.jp/');
    const position = await consult.evaluate((el) => getComputedStyle(el).position);
    expect(position).toBe('fixed');
    const box = await consult.boundingBox();
    expect(box).not.toBeNull();
    expect(Math.abs(box.x + box.width - (1280 - 16))).toBeLessThanOrEqual(1);
    expect(Math.abs(box.y + box.height - (800 - 16))).toBeLessThanOrEqual(1);
  });

  test('スマホ幅でも、相談ボタンは本文の末尾（フッターのリンク）を隠さない（重なりの全位置の検査は consult-overlap.spec.js）', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto(`${dev.url}/`);
    await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
    const consultBox = await page.getByRole('link', { name: 'ご相談はこちら' }).boundingBox();
    const footerLinks = page.getByRole('contentinfo').locator('a:visible, button:visible');
    const count = await footerLinks.count();
    expect(count).toBeGreaterThanOrEqual(3);
    for (let i = 0; i < count; i += 1) {
      const box = await footerLinks.nth(i).boundingBox();
      expect(intersects(box, consultBox), `フッターの ${i} 番目の要素`).toBe(false);
    }
  });

  test('ランドマーク（header・nav・main・footer）を備える', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await expect(page.getByRole('banner')).toHaveCount(1);
    await expect(page.getByRole('main')).toHaveCount(1);
    await expect(page.getByRole('contentinfo')).toHaveCount(1);
    expect(await page.getByRole('navigation').count()).toBeGreaterThanOrEqual(2);
  });

  test('見出しは h1 から始まり、レベルを飛ばさない（/ と /legal。非表示の同意バナーも対象）', async ({ page }) => {
    for (const pagePath of ['/', '/legal']) {
      await page.goto(`${dev.url}${pagePath}`);
      // DOM の順序で見出しのレベルを読む（非表示の同意バナーも含む。後から表示されても順序は変わらないため）
      const levels = await page.evaluate(() => Array.from(
        document.querySelectorAll('h1, h2, h3, h4, h5, h6, [role="heading"]'),
        (el) => {
          const ariaLevel = el.getAttribute('aria-level');
          if (ariaLevel !== null) return Number(ariaLevel);
          if (/^H[1-6]$/.test(el.tagName)) return Number(el.tagName.slice(1));
          // role="heading" で aria-level を省いたときの既定値（ARIA の仕様）
          return 2;
        },
      ));
      expect(levels.length, `${pagePath} に見出しがあること`).toBeGreaterThan(0);
      expect(levels[0], `${pagePath} の最初の見出しは h1 であること`).toBe(1);
      for (let i = 1; i < levels.length; i += 1) {
        expect(levels[i] - levels[i - 1], `${pagePath} の ${i + 1} 番目の見出しでレベルを飛ばしていないこと`).toBeLessThanOrEqual(1);
      }
    }
  });

  test('スキップリンクで本文へ移動できる', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await page.keyboard.press('Tab');
    const skip = page.getByRole('link', { name: '本文へ移動' });
    await expect(skip).toBeFocused();
    await expect(skip).toBeVisible();
    await page.keyboard.press('Enter');
    await expect(page.locator('#main')).toBeFocused();
  });

  test('本文に Unity の描画領域（canvas）を置き、キーボードで選べる', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const canvas = page.locator('main canvas#unity-canvas');
    await expect(canvas).toBeVisible();
    await expect(canvas).toHaveAttribute('tabindex', '0');
  });

  test('Unity のビルドが無いときは、理由を画面に表示する（黙って失敗しない）', async ({ page }) => {
    const errors = collectPageErrors(page);
    const blocked = await hideUnityBuild(page);
    await page.goto(`${dev.url}/`);
    await expect(page.locator('#unity-status')).toHaveText(config.texts.unity.missingBuild);
    await expect(page.locator('canvas#unity-canvas')).toBeVisible();
    // loader の要求が route で実際に止まったことを確かめる（止まらなければ、ビルドの有無で結果が変わる）
    const loaderBlocked = blocked.some((url) => url.endsWith(`/${config.unity.loaderUrl}`));
    expect(loaderBlocked, 'loader の要求が route で止まっていません').toBe(true);
    expect(errors).toEqual([]);
  });

  test('Unity のビルドがあるときは契約どおりの設定で起動し、読み込みの表示を消す', async ({ page }) => {
    const errors = collectPageErrors(page);
    // ローダーはスタブに置き換え、置かれているビルドは読まない（ビルドの有無で結果は変わらない）
    await stubUnityLoader(page, `window.createUnityInstance = function (canvas, unityConfig) {
      window.__unityLaunch = { canvasId: canvas.id, config: unityConfig };
      return Promise.resolve({ Quit: function () { return Promise.resolve(); } });
    };`);
    await page.goto(`${dev.url}/`);
    await page.waitForFunction(() => window.__unityLaunch !== undefined);
    const launch = await page.evaluate(() => window.__unityLaunch);
    expect(launch.canvasId).toBe('unity-canvas');
    expect(launch.config.dataUrl).toBe(config.unity.dataUrl);
    expect(launch.config.frameworkUrl).toBe(config.unity.frameworkUrl);
    expect(launch.config.codeUrl).toBe(config.unity.codeUrl);
    expect(launch.config.streamingAssetsUrl).toBe(config.unity.streamingAssetsUrl);
    expect(launch.config.productName).toBe(config.unity.productName);
    await expect(page.locator('#unity-status')).toBeHidden();
    expect(errors).toEqual([]);
  });

  test('Unity の起動に失敗したときは、理由を画面に表示する', async ({ page }) => {
    const errors = collectPageErrors(page);
    await stubUnityLoader(page, 'window.createUnityInstance = function () { return Promise.reject(new Error("検査用の失敗")); };');
    await page.goto(`${dev.url}/`);
    await expect(page.locator('#unity-status')).toHaveText(config.texts.unity.loadFailed);
    expect(errors).toEqual([]);
  });

  test('利回り・イベント・物価が架空のモデル値である旨を、本文に常時表示する', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await expect(page.locator('main').getByText(FICTION_TEXT)).toBeVisible();
  });

  test('フッターの「利用規約」「免責事項」「連絡先」は /legal の各節へリンクする', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const footer = page.getByRole('contentinfo');
    await expect(footer.getByRole('link', { name: '利用規約' })).toHaveAttribute('href', '/legal#terms');
    await expect(footer.getByRole('link', { name: '免責事項' })).toHaveAttribute('href', '/legal#disclaimer');
    await expect(footer.getByRole('link', { name: '連絡先' })).toHaveAttribute('href', '/legal#contact');
    await footer.getByRole('link', { name: '免責事項' }).click();
    await expect(page).toHaveURL(/\/legal#disclaimer$/);
    await expect(page.locator('#disclaimer h2')).toHaveText('免責事項');
  });

  test('/legal には規約・免責・データの取り扱い・連絡先があり、必須要素を備える', async ({ page }) => {
    await page.goto(`${dev.url}/legal`);
    await expect(page).toHaveTitle(/利用規約/);
    for (const heading of ['利用規約', '免責事項', 'データの取り扱い', '連絡先']) {
      await expect(page.getByRole('heading', { level: 2, name: heading })).toBeVisible();
    }
    const main = page.locator('main');
    await expect(main).toContainText('投資の助言ではありません');
    await expect(main).toContainText('架空');
    await expect(main).toContainText('毎日 JST 03:00 に');
    await expect(main).toContainText('個人情報');
    await expect(main).toContainText('Cookie');
    await expect(main).toContainText('アクセス解析');
    await expect(page.getByRole('link', { name: 'info@rictaworks.jp' })).toHaveAttribute('href', 'mailto:info@rictaworks.jp');
    await expect(page.locator('.demo-banner')).toHaveText(BANNER_TEXT);
    await expect(page.getByRole('link', { name: '← デモ一覧へ' })).toHaveAttribute('href', 'https://rictaworks.jp/#demos');
    await expect(page.getByRole('link', { name: 'ご相談はこちら' })).toHaveAttribute('href', 'https://rictaworks.jp/');
    const text = await page.locator('body').innerText();
    expect(text).not.toMatch(/である|だ。/);
  });

  test('同意バナーの「許可」と「拒否」は同じ強さのボタンにする', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const allow = page.getByRole('button', { name: '許可' });
    const deny = page.getByRole('button', { name: '拒否' });
    await expect(allow).toBeVisible();
    await expect(deny).toBeVisible();
    const names = ['background-color', 'color', 'border-top-width', 'border-top-color', 'font-size', 'font-weight', 'padding-top', 'padding-left'];
    expect(await styleOf(deny, names)).toEqual(await styleOf(allow, names));
  });

  test('「許可」を選ぶと保存され、次の表示では同意バナーを出さない', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const banner = page.getByRole('region', { name: CONSENT_REGION_NAME });
    await expect(banner).toBeVisible();
    await page.getByRole('button', { name: '許可' }).click();
    await expect(banner).toBeHidden();
    const stored = await page.evaluate((key) => window.localStorage.getItem(key), config.analytics.consentStorageKey);
    expect(stored).toBe('granted');
    const entries = await readDataLayer(page);
    expect(entries.some((entry) => entry[0] === 'consent' && entry[1] === 'update' && entry[2].analytics_storage === 'granted')).toBe(true);
    await page.reload();
    const reopen = page.getByRole('button', { name: 'アクセス解析の設定を変更' });
    await expect(reopen).toBeVisible();
    await expect(banner).toBeHidden();
  });

  test('「拒否」を選ぶと拒否として保存し、分析の同意は与えない', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    const banner = page.getByRole('region', { name: CONSENT_REGION_NAME });
    await expect(banner).toBeVisible();
    await page.getByRole('button', { name: '拒否' }).click();
    await expect(banner).toBeHidden();
    const stored = await page.evaluate((key) => window.localStorage.getItem(key), config.analytics.consentStorageKey);
    expect(stored).toBe('denied');
    const entries = await readDataLayer(page);
    expect(entries.some((entry) => entry[0] === 'consent' && entry[1] === 'update' && entry[2].analytics_storage === 'granted')).toBe(false);
    expect(entries.some((entry) => entry[0] === 'consent' && entry[1] === 'update' && entry[2].analytics_storage === 'denied')).toBe(true);
  });

  test('保存した選択は、後から「アクセス解析の設定を変更」で変えられる', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await page.getByRole('button', { name: '拒否' }).click();
    const reopen = page.getByRole('button', { name: 'アクセス解析の設定を変更' });
    await expect(reopen).toBeVisible();
    await reopen.click();
    const banner = page.getByRole('region', { name: CONSENT_REGION_NAME });
    await expect(banner).toBeVisible();
    await page.getByRole('button', { name: '許可' }).click();
    const stored = await page.evaluate((key) => window.localStorage.getItem(key), config.analytics.consentStorageKey);
    expect(stored).toBe('granted');
  });

  test('保存できないときも画面は止まらず、その旨をコンソールに出す', async ({ page }) => {
    const errors = collectPageErrors(page);
    /** @type {string[]} */
    const warnings = [];
    page.on('console', (message) => {
      if (message.type() === 'warning') warnings.push(message.text());
    });
    await page.addInitScript(() => {
      const blocked = () => {
        throw new Error('検査用に保存を止めています');
      };
      Storage.prototype.getItem = blocked;
      Storage.prototype.setItem = blocked;
    });
    await page.goto(`${dev.url}/`);
    const banner = page.getByRole('region', { name: CONSENT_REGION_NAME });
    await expect(banner).toBeVisible();
    await page.getByRole('button', { name: '許可' }).click();
    await expect(banner).toBeHidden();
    await expect.poll(() => warnings.some((text) => text.includes(config.texts.consent.readFailed))).toBe(true);
    await expect.poll(() => warnings.some((text) => text.includes(config.texts.consent.saveFailed))).toBe(true);
    expect(errors).toEqual([]);
  });

  for (const path of ['/', '/legal']) {
    for (const width of [320, 375]) {
      test(`スマホ幅（${width}px）で横スクロールが出ず、左右の余白は 16px（${path}）`, async ({ page }) => {
        await page.setViewportSize({ width, height: 700 });
        await page.goto(`${dev.url}${path}`);
        const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
        expect(overflow).toBeLessThanOrEqual(0);
        const box = await page.locator('main h1').boundingBox();
        expect(box).not.toBeNull();
        expect(box.x).toBe(16);
      });
    }
  }
});
