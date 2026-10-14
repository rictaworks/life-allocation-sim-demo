// @ts-check
// 存在しないパスの 404 ページ（src/web/404.html）の検査。開発用サーバーは Vercel と同じく、404 の応答として 404.html を返す。
const { test, expect } = require('@playwright/test');
const support = require('./support');

const MISSING_PATH = '/no-such-page';
const NESTED_MISSING_PATH = '/no/such/deep/page';
const BANNER_TEXT = 'これはデモ版です。データはサーバー再起動時にリセットされる場合があります。';
const CONSENT_REGION_NAME = 'アクセス解析の利用について';
const DEMO_LIST_URL = 'https://rictaworks.jp/#demos';
const CONSULT_URL = 'https://rictaworks.jp/';
// 色の値は src/web/css/site.css の --color-banner-bg（#ffbf00）と同じ
const BANNER_RGB = 'rgb(255, 191, 0)';
const GTM_PATTERN = /^https:\/\/www\.googletagmanager\.com\//;

test.describe('404 ページ（存在しないパス）', () => {
  /** @type {{ url: string, close: () => Promise<void> }} */
  let dev;

  test.beforeAll(async () => {
    dev = await support.startDevServer();
  });

  test.afterAll(async () => {
    await dev.close();
  });

  test.beforeEach(async ({ page }) => {
    // 外部の計測スクリプトは実際には読み込まない（GA4 の読み込みは analytics-consent.spec.js で検査する）
    await page.route(GTM_PATTERN, (route) => route.fulfill({
      status: 200,
      contentType: 'text/javascript',
      body: '/* 検査用のスタブ */',
    }));
  });

  test('存在しないパスは 404 の応答で 404 ページを返し、スタックトレースや内部パスを含まない', async ({ request }) => {
    const res = await request.get(`${dev.url}${MISSING_PATH}`);
    expect(res.status()).toBe(404);
    expect(res.headers()['content-type']).toContain('text/html');
    const body = await res.text();
    expect(body).toContain('<html lang="ja">');
    expect(body).not.toMatch(/Error|\bat\s|\/home\/|\\Users\\/);
  });

  test('404 ページは全ページ共通の要素を備える（バナー・デモ一覧へ・相談・フッター・同意）', async ({ page }) => {
    const response = await page.goto(`${dev.url}${MISSING_PATH}`);
    expect(response).not.toBeNull();
    expect(response?.status()).toBe(404);
    await expect(page.locator('html')).toHaveAttribute('lang', 'ja');
    await expect(page).toHaveTitle(/ページが見つかりません/);
    const scheme = await page.evaluate(() => getComputedStyle(document.documentElement).colorScheme);
    expect(scheme).toBe('light');

    await expect(page.locator('.demo-banner')).toHaveText(BANNER_TEXT);
    const back = page.getByRole('navigation', { name: '主要' }).getByRole('link', { name: '← デモ一覧へ' });
    await expect(back).toHaveAttribute('href', DEMO_LIST_URL);
    await expect(page.getByRole('link', { name: 'ご相談はこちら' })).toHaveAttribute('href', CONSULT_URL);

    const footer = page.getByRole('contentinfo');
    await expect(footer.getByRole('link', { name: '利用規約' })).toHaveAttribute('href', '/legal#terms');
    await expect(footer.getByRole('link', { name: '免責事項' })).toHaveAttribute('href', '/legal#disclaimer');
    await expect(footer.getByRole('link', { name: '連絡先' })).toHaveAttribute('href', '/legal#contact');

    await expect(page.getByRole('heading', { level: 1 })).toHaveText('ページが見つかりません');
    await expect(page.getByRole('main').getByRole('link', { name: '人生配分シミュレーションのトップページへ' })).toHaveAttribute('href', '/');
    await expect(page.getByRole('region', { name: CONSENT_REGION_NAME })).toBeVisible();
  });

  test('深いパスの 404 でも、スタイルと本体のスクリプトを読み込む（相対パスにしない）', async ({ page }) => {
    for (const pathname of [MISSING_PATH, NESTED_MISSING_PATH]) {
      const response = await page.goto(`${dev.url}${pathname}`);
      expect(response?.status(), pathname).toBe(404);
      const color = await page.locator('.demo-banner').evaluate((el) => getComputedStyle(el).backgroundColor);
      expect(color, pathname).toBe(BANNER_RGB);
      await expect(page.getByRole('region', { name: CONSENT_REGION_NAME }), pathname).toBeVisible();
    }
  });

  for (const width of [320, 375]) {
    test(`404 ページはスマホ幅（${width}px）で横スクロールが出ない`, async ({ page }) => {
      await page.setViewportSize({ width, height: 700 });
      await page.goto(`${dev.url}${MISSING_PATH}`);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(0);
    });
  }
});
