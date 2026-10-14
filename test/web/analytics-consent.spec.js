// @ts-check
// GA4（gtag.js）の読み込みの検査。読み込むのは、利用者が「許可」を選んだとき、または保存済みの「許可」で開いたときだけ。
// 未選択と「拒否」では、gtag.js も Google への要求も行わない（CC08：明確な同意の取得とオプトアウト）。
const { test, expect } = require('@playwright/test');
const support = require('./support');

const CONSENT_REGION_NAME = 'アクセス解析の利用について';
const ALLOW_LABEL = '許可';
const DENY_LABEL = '拒否';
const REOPEN_LABEL = 'アクセス解析の設定を変更';
const GTM_PATTERN = /^https:\/\/www\.googletagmanager\.com\//;
const GTM_STUB_BODY = '/* 検査用のスタブ */';

/**
 * 同じオリジン以外への要求を記録して止める。Google のタグの要求には、検査用の空に近いスクリプトを返す（Google へは行かない）。
 * @param {import('@playwright/test').Page} page
 * @param {string} origin 開発用サーバーの URL（同じオリジン）
 * @returns {Promise<{ external: string[], gtag: string[] }>}
 */
async function recordExternalRequests(page, origin) {
  /** @type {{ external: string[], gtag: string[] }} */
  const requests = { external: [], gtag: [] };
  await page.route('**/*', (route) => {
    const url = route.request().url();
    if (url.startsWith(origin)) return route.continue();
    requests.external.push(url);
    if (GTM_PATTERN.test(url)) {
      requests.gtag.push(url);
      return route.fulfill({ status: 200, contentType: 'text/javascript', body: GTM_STUB_BODY });
    }
    return route.abort();
  });
  return requests;
}

test.describe('GA4 の読み込み（同意のあとだけ）', () => {
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

  test('未選択のときは、GA4 を読み込まず、Google への要求は 0 件にする（同意の既定は拒否）', async ({ page }) => {
    const requests = await recordExternalRequests(page, dev.url);
    await page.goto(`${dev.url}/`);
    await expect(page.getByRole('region', { name: CONSENT_REGION_NAME })).toBeVisible();
    await page.waitForLoadState('networkidle');
    expect(requests.external, '同じオリジン以外への要求').toEqual([]);
    const entries = await support.readDataLayer(page);
    const defaults = entries.find((entry) => entry[0] === 'consent' && entry[1] === 'default')?.[2];
    expect(defaults, '同意の既定が dataLayer にある').toBeDefined();
    for (const key of ['ad_storage', 'ad_user_data', 'ad_personalization', 'analytics_storage']) {
      expect(defaults[key], key).toBe('denied');
    }
    expect(entries.some((entry) => entry[0] === 'config' || entry[0] === 'js'), '計測の命令を送っていない').toBe(false);
  });

  test('「拒否」を選んでも、Google への要求は 0 件のまま（同意の更新だけを送る）', async ({ page }) => {
    const requests = await recordExternalRequests(page, dev.url);
    await page.goto(`${dev.url}/`);
    await page.getByRole('button', { name: DENY_LABEL }).click();
    await expect(page.getByRole('button', { name: REOPEN_LABEL })).toBeVisible();
    await page.waitForLoadState('networkidle');
    expect(await page.evaluate((key) => window.localStorage.getItem(key), config.analytics.consentStorageKey)).toBe(config.analytics.consentDenied);
    expect(requests.external, '同じオリジン以外への要求').toEqual([]);
    const entries = await support.readDataLayer(page);
    expect(entries).toContainEqual(['consent', 'update', { analytics_storage: config.analytics.consentDenied }]);
    expect(entries.some((entry) => entry[0] === 'config'), '計測の設定を送っていない').toBe(false);
  });

  test('保存済みの「拒否」で開いたときも、Google への要求は 0 件のまま', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await page.evaluate(
      ([key, value]) => window.localStorage.setItem(key, value),
      [config.analytics.consentStorageKey, config.analytics.consentDenied],
    );
    const requests = await recordExternalRequests(page, dev.url);
    await page.reload();
    await expect(page.getByRole('button', { name: REOPEN_LABEL })).toBeVisible();
    await page.waitForLoadState('networkidle');
    expect(requests.external, '同じオリジン以外への要求').toEqual([]);
  });

  test('「許可」を選ぶと、gtag.js を測定 ID で読み込み、config を送る', async ({ page }) => {
    const requests = await recordExternalRequests(page, dev.url);
    await page.goto(`${dev.url}/`);
    await page.getByRole('button', { name: ALLOW_LABEL }).click();
    await expect(page.getByRole('button', { name: REOPEN_LABEL })).toBeVisible();
    await expect.poll(() => requests.gtag.length).toBe(1);
    expect(requests.gtag[0]).toBe(`${config.analytics.scriptUrl}?id=${config.analytics.measurementId}`);
    const entries = await support.readDataLayer(page);
    expect(entries).toContainEqual(['consent', 'update', { analytics_storage: config.analytics.consentGranted }]);
    expect(entries).toContainEqual(['config', config.analytics.measurementId]);
  });

  test('保存済みの「許可」で開いたときは、最初から gtag.js を読み込む', async ({ page }) => {
    await page.goto(`${dev.url}/`);
    await page.evaluate(
      ([key, value]) => window.localStorage.setItem(key, value),
      [config.analytics.consentStorageKey, config.analytics.consentGranted],
    );
    const requests = await recordExternalRequests(page, dev.url);
    await page.reload();
    await expect(page.getByRole('button', { name: REOPEN_LABEL })).toBeVisible();
    await expect.poll(() => requests.gtag.length).toBe(1);
    expect(requests.gtag[0]).toBe(`${config.analytics.scriptUrl}?id=${config.analytics.measurementId}`);
  });

  test('「拒否」から「設定を変更」で「許可」に変えると、そのときに読み込み、同じ表示で二度は読み込まない', async ({ page }) => {
    const requests = await recordExternalRequests(page, dev.url);
    await page.goto(`${dev.url}/`);
    await page.getByRole('button', { name: DENY_LABEL }).click();
    await page.getByRole('button', { name: REOPEN_LABEL }).click();
    await expect(page.getByRole('region', { name: CONSENT_REGION_NAME })).toBeVisible();
    expect(requests.gtag, '「拒否」の間は読み込まない').toEqual([]);
    await page.getByRole('button', { name: ALLOW_LABEL }).click();
    await expect.poll(() => requests.gtag.length).toBe(1);
    await page.getByRole('button', { name: REOPEN_LABEL }).click();
    await page.getByRole('button', { name: ALLOW_LABEL }).click();
    await page.waitForLoadState('networkidle');
    expect(requests.gtag, '二度目の「許可」で再び読み込まない').toHaveLength(1);
  });
});
