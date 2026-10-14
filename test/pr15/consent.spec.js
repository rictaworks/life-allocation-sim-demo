// U11〜U16：同意バナーと GA4（Consent Mode v2）。各テストは新しいブラウザ文脈で行い、前回の選択を持ち越さない。
const { test, expect } = require('@playwright/test');
const { cfg, isolateContext, readLocalStorage, readDataLayer } = require('./helpers');

let isolation;

test.beforeEach(async ({ context }) => {
  isolation = await isolateContext(context, { blockUnity: true });
});

const banner = (page) => page.locator('#consent-banner');
const choice = (page, label) => banner(page).getByRole('button', { name: label, exact: true });
const reopenButton = (page) => page.getByRole('button', { name: cfg.consent.reopenLabel, exact: true });
// バナーの題名は見出し（h2）ではなく段落（p）にしてある（見出しの順序を直すため）。題名の要素は id で探す。
const bannerHeading = (page) => banner(page).locator('#consent-title');
const mainHeading = (page) => page.getByRole('heading', { level: 1, name: cfg.page.h1 });

test('U11 初回は同意バナーが出る。「許可」と「拒否」は同じ強さのボタンで、既定は拒否', async ({ page }) => {
  await page.goto('/');
  await expect(banner(page)).toBeVisible();
  await expect(bannerHeading(page)).toBeVisible();
  const granted = choice(page, cfg.consent.grantedLabel);
  const denied = choice(page, cfg.consent.deniedLabel);
  await expect(granted).toBeVisible();
  await expect(denied).toBeVisible();
  const appearance = (locator) =>
    locator.evaluate((element) => {
      const style = getComputedStyle(element);
      const rect = element.getBoundingClientRect();
      return {
        background: style.backgroundColor,
        color: style.color,
        border: style.borderTopColor,
        weight: style.fontWeight,
        width: Math.round(rect.width),
        height: Math.round(rect.height),
      };
    });
  expect(await appearance(granted)).toEqual(await appearance(denied));
  expect(await readLocalStorage(page, cfg.consent.storageKey)).toBeNull();
  expect(await readDataLayer(page)).toContainEqual([
    'consent',
    'default',
    { ad_storage: 'denied', ad_user_data: 'denied', ad_personalization: 'denied', analytics_storage: 'denied' },
  ]);
});

test('U12 「拒否」を選ぶとバナーが閉じ、選択が保存され、再読み込み後も閉じたままになる', async ({ page }) => {
  await page.goto('/');
  await choice(page, cfg.consent.deniedLabel).click();
  await expect(banner(page)).toBeHidden();
  await expect(reopenButton(page)).toBeVisible();
  expect(await readLocalStorage(page, cfg.consent.storageKey)).toBe(cfg.consent.deniedValue);
  expect(await readDataLayer(page)).toContainEqual(['consent', 'update', { analytics_storage: cfg.consent.deniedValue }]);
  await page.reload();
  await expect(banner(page)).toBeHidden();
  await expect(mainHeading(page)).toBeVisible();
});

test('U13 「許可」を選ぶと保存され、次の訪問では分析の同意が最初から反映される', async ({ page }) => {
  await page.goto('/');
  await choice(page, cfg.consent.grantedLabel).click();
  await expect(banner(page)).toBeHidden();
  expect(await readLocalStorage(page, cfg.consent.storageKey)).toBe(cfg.consent.grantedValue);
  expect(await readDataLayer(page)).toContainEqual(['consent', 'update', { analytics_storage: cfg.consent.grantedValue }]);
  await page.reload();
  await expect(banner(page)).toBeHidden();
  expect(await readDataLayer(page)).toContainEqual(['consent', 'update', { analytics_storage: cfg.consent.grantedValue }]);
});

test('U14 GA4 は「許可」を選んだときだけ測定 ID（G-C04W1XKS16）で読み込まれ、未選択と「拒否」では Google への要求が 0 件', async ({ page }) => {
  // 未選択：バナーが出ている間は、gtag.js も Google への要求も出ず、計測の命令（config）も積まれません。
  await page.goto('/');
  await expect(banner(page)).toBeVisible();
  await page.waitForLoadState('networkidle');
  expect(isolation.gtmRequests).toEqual([]);
  expect(await readDataLayer(page)).not.toContainEqual(['config', cfg.consent.measurementId]);
  // 拒否：同意の更新だけを積みます。開き直しても読み込みません。
  await choice(page, cfg.consent.deniedLabel).click();
  await expect(banner(page)).toBeHidden();
  await page.reload();
  await expect(banner(page)).toBeHidden();
  await page.waitForLoadState('networkidle');
  expect(isolation.gtmRequests).toEqual([]);
  // 許可：この選択で初めて読み込み、測定 ID の config を送ります。
  await reopenButton(page).click();
  await choice(page, cfg.consent.grantedLabel).click();
  await expect(banner(page)).toBeHidden();
  await expect.poll(() => isolation.gtmRequests.length).toBeGreaterThan(0);
  expect(isolation.gtmRequests.some((url) => url.includes(`id=${cfg.consent.measurementId}`))).toBe(true);
  expect(await readDataLayer(page)).toContainEqual(['config', cfg.consent.measurementId]);
});

test('U15 「アクセス解析の設定を変更」でバナーを開き直せ、見出しにフォーカスが移り、選び直せる', async ({ page }) => {
  await page.goto('/');
  await choice(page, cfg.consent.deniedLabel).click();
  await reopenButton(page).click();
  await expect(banner(page)).toBeVisible();
  await expect(bannerHeading(page)).toBeFocused();
  await choice(page, cfg.consent.grantedLabel).click();
  await expect(banner(page)).toBeHidden();
  expect(await readLocalStorage(page, cfg.consent.storageKey)).toBe(cfg.consent.grantedValue);
});

test('U16 保存や読み取りができない環境でも画面は止まらず、理由をコンソールに出す', async ({ page, context }) => {
  await context.addInitScript(() => {
    const blocked = () => {
      throw new DOMException('blocked', 'SecurityError');
    };
    Storage.prototype.getItem = blocked;
    Storage.prototype.setItem = blocked;
  });
  const consoleTexts = [];
  page.on('console', (message) => consoleTexts.push(message.text()));
  await page.goto('/');
  await expect(banner(page), '読み取れないときも、バナーは出る').toBeVisible();
  await choice(page, cfg.consent.grantedLabel).click();
  await expect(banner(page), '保存できなくても、選択でバナーは閉じる').toBeHidden();
  await expect(mainHeading(page)).toBeVisible();
  expect(consoleTexts.some((text) => text.includes(cfg.consent.readFailedText))).toBe(true);
  expect(consoleTexts.some((text) => text.includes(cfg.consent.saveFailedText))).toBe(true);
});
