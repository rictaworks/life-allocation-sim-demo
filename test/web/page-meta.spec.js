// @ts-check
// 共有時の表示（Open Graph）の検査。実際のページの meta を読み、値がページの title と description に合い、URL が公開ドメインだけであることを確かめる。
const { test, expect } = require('@playwright/test');
const support = require('./support');

const PUBLIC_ORIGIN = 'https://life-allocation.rictaworks.jp';
const SITE_NAME = '人生配分シミュレーション（デモ版）';
const GTM_PATTERN = /^https:\/\/www\.googletagmanager\.com\//;

/** @type {Array<{ path: string, url: string }>} 公開のページと、og:url（公開ドメインの URL） */
const PAGES = [
  { path: '/', url: `${PUBLIC_ORIGIN}/` },
  { path: '/legal', url: `${PUBLIC_ORIGIN}/legal` },
  // 存在しないパスの 404 ページも、共有されたときの表示はトップページの URL にする
  { path: '/no-such-page', url: `${PUBLIC_ORIGIN}/` },
];

test.describe('Open Graph（共有時の表示）', () => {
  /** @type {{ url: string, close: () => Promise<void> }} */
  let dev;

  test.beforeAll(async () => {
    dev = await support.startDevServer();
  });

  test.afterAll(async () => {
    await dev.close();
  });

  test.beforeEach(async ({ page }) => {
    await page.route(GTM_PATTERN, (route) => route.fulfill({
      status: 200,
      contentType: 'text/javascript',
      body: '/* 検査用のスタブ */',
    }));
  });

  for (const { path, url } of PAGES) {
    test(`${path} は og の基本項目を備え、値はページの title と description に合わせ、URL は公開ドメインにする`, async ({ page }) => {
      await page.goto(`${dev.url}${path}`);
      const og = await page.evaluate(() => Object.fromEntries(
        Array.from(document.querySelectorAll('meta[property^="og:"]'), (el) => [el.getAttribute('property'), el.getAttribute('content')]),
      ));
      expect(og['og:type'], 'og:type').toBe('website');
      expect(og['og:locale'], 'og:locale').toBe('ja_JP');
      expect(og['og:site_name'], 'og:site_name').toBe(SITE_NAME);
      expect(og['og:url'], 'og:url').toBe(url);
      expect(new URL(og['og:url']).host, 'og:url のホスト').toBe('life-allocation.rictaworks.jp');
      expect(og['og:title'], 'og:title は title と同じ').toBe(await page.title());
      const description = await page.locator('meta[name="description"]').getAttribute('content');
      expect(og['og:description'], 'og:description は description と同じ').toBe(description);
    });
  }
});
