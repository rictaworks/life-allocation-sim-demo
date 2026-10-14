// U17〜U19：スマホ幅（375px）。横スクロール・左右の余白・相談ボタンの位置・画面の枠の収まり。
// Unity は読み込まない（枠の大きさは CSS で決まる。起動の確認は unity.spec.js）。
const { test, expect } = require('@playwright/test');
const { cfg, isolateContext, artifactPath, expectFooterLinksClickable } = require('./helpers');

const { gutterPx, tolerancePx } = cfg.layout;
const mobileWidth = cfg.viewports.mobile.width;

test.use({ viewport: cfg.viewports.mobile });

let isolation;

test.beforeEach(async ({ context }) => {
  isolation = await isolateContext(context, { blockUnity: true });
});

for (const pagePath of ['/', cfg.legal.path]) {
  test(`U17 スマホ幅で ${pagePath} に横スクロールが出ず、左右の余白は 16px`, async ({ page }) => {
    await page.goto(pagePath);
    const widths = await page.evaluate(() => ({
      scroll: document.documentElement.scrollWidth,
      client: document.documentElement.clientWidth,
    }));
    expect(widths.scroll, '横スクロールが出ている').toBeLessThanOrEqual(widths.client);
    const heading = await page.locator('h1').first().boundingBox();
    expect(Math.abs(heading.x - gutterPx), '左の余白').toBeLessThanOrEqual(tolerancePx);
    expect(heading.x + heading.width, '右の余白').toBeLessThanOrEqual(mobileWidth - gutterPx + tolerancePx);
  });
}

test('U18 スマホ幅で相談ボタンは本文の流れの末尾にあり、本文を隠さず、フッターのリンクも押せる', async ({ page }) => {
  await page.goto('/');
  const consult = page.getByRole('link', { name: cfg.page.consultText, exact: true });
  await expect(consult).toHaveAttribute('href', cfg.page.consultHref);
  expect(await consult.evaluate((element) => getComputedStyle(element).position), '相談ボタンが固定されている').not.toBe(
    'fixed'
  );
  const footer = await page.getByRole('contentinfo').boundingBox();
  const consultBox = await consult.boundingBox();
  expect(consultBox.y, '相談ボタンがフッターより上にある').toBeGreaterThanOrEqual(footer.y + footer.height - tolerancePx);
  await expectFooterLinksClickable(page);
  await page.screenshot({ path: artifactPath('pr15-mobile-full.png'), fullPage: true });
  expect(isolation.externalRequests).toEqual([]);
});

test('U19 スマホ幅で画面の枠（canvas）が幅に収まり、左右 16px の中にある', async ({ page }) => {
  await page.goto('/');
  const canvas = await page.locator('#unity-canvas').boundingBox();
  expect(Math.abs(canvas.x - gutterPx), '枠の左の余白').toBeLessThanOrEqual(tolerancePx);
  expect(canvas.x + canvas.width, '枠の右の余白').toBeLessThanOrEqual(mobileWidth - gutterPx + tolerancePx);
  await page.screenshot({ path: artifactPath('pr15-mobile-top.png') });
});
