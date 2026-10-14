// U01〜U10：画面の共通要素（タイトル・バナー・ナビ・相談ボタン・フッター・/legal・テーマ・外部通信・キーボード）。
// Unity は読み込まない（この節の手順は画面の殻だけを見る）。Unity の起動は unity.spec.js で確かめる。
const { test, expect } = require('@playwright/test');
const {
  cfg,
  absoluteUrl,
  isolateContext,
  parseRgb,
  contrastRatio,
  expectFooterLinksClickable,
  EMAIL_PATTERN,
} = require('./helpers');

let isolation;

test.beforeEach(async ({ context }) => {
  isolation = await isolateContext(context, { blockUnity: true });
});

test('U01 タイトル：日本語のタイトルと見出しがある', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(cfg.page.title);
  await expect(page.locator('html')).toHaveAttribute('lang', cfg.page.lang);
  await expect(page.getByRole('heading', { level: 1, name: cfg.page.h1 })).toBeVisible();
});

test('U02 ページ最上部のアンバーバナーに決められた文言があり、文字は背景に対して読める', async ({ page }) => {
  await page.goto('/');
  const banner = page.locator('.demo-banner');
  await expect(banner).toHaveText(cfg.page.banner);
  const colors = await banner.evaluate((element) => {
    const style = getComputedStyle(element);
    return { background: style.backgroundColor, text: style.color };
  });
  const background = parseRgb(colors.background);
  expect(background).toEqual(cfg.page.bannerBackgroundRgb);
  expect(contrastRatio(parseRgb(colors.text), background)).toBeGreaterThanOrEqual(cfg.page.minimumContrastRatio);
  const isAtTop = await page.evaluate(() => {
    const element = document.elementFromPoint(4, 4);
    return element !== null && element.closest('.demo-banner') !== null;
  });
  expect(isAtTop, 'ページの左上の点がバナーの中にない（バナーが最上部にない）').toBe(true);
});

test('U03 ナビの右端に「← デモ一覧へ」があり、デモ一覧へのリンクになっている', async ({ page }) => {
  await page.goto('/');
  const nav = page.getByRole('navigation', { name: cfg.page.navLabel });
  const back = nav.getByRole('link', { name: cfg.page.navBackText, exact: true });
  await expect(back).toHaveAttribute('href', cfg.page.navBackHref);
  const rights = await nav.locator('a').evaluateAll((links) => links.map((link) => link.getBoundingClientRect().right));
  const backRight = await back.evaluate((link) => link.getBoundingClientRect().right);
  expect(backRight).toBe(Math.max(...rights));
});

test('U04 幅 1280px 以上では、右下に固定の「ご相談はこちら」があり、フッターのリンクを隠さない', async ({ page }) => {
  await page.setViewportSize(cfg.viewports.desktop);
  await page.goto('/');
  const consult = page.getByRole('link', { name: cfg.page.consultText, exact: true });
  await expect(consult).toHaveAttribute('href', cfg.page.consultHref);
  expect(await consult.evaluate((element) => getComputedStyle(element).position)).toBe('fixed');
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  const box = await consult.boundingBox();
  const { width, height } = cfg.viewports.desktop;
  const { gutterPx, tolerancePx } = cfg.layout;
  expect(Math.abs(box.x + box.width - (width - gutterPx))).toBeLessThanOrEqual(tolerancePx);
  expect(Math.abs(box.y + box.height - (height - gutterPx))).toBeLessThanOrEqual(tolerancePx);
  await expectFooterLinksClickable(page);
});

test('U05 フッターの「利用規約」「免責事項」「連絡先」が、/legal の各節へ移る', async ({ page }) => {
  for (const item of cfg.page.footerLinks) {
    await page.goto('/');
    await page.getByRole('contentinfo').getByRole('link', { name: item.text, exact: true }).click();
    await expect(page).toHaveURL(absoluteUrl(item.href));
    await expect(page.getByRole('heading', { level: 1, name: cfg.legal.h1 })).toBeVisible();
    await expect(page.locator(item.target)).toBeInViewport();
  }
});

test('U06 /legal に利用規約・免責事項・データの取り扱い・連絡先があり、連絡先のメールは窓口の 1 件だけ', async ({ page }) => {
  const response = await page.goto(cfg.legal.path);
  expect(response.status()).toBe(200);
  await expect(page).toHaveTitle(cfg.legal.title);
  await expect(page.getByRole('heading', { level: 1, name: cfg.legal.h1 })).toBeVisible();
  for (const id of cfg.legal.sectionIds) {
    await expect(page.locator(`#${id}`)).toHaveCount(1);
  }
  const mainText = await page.locator('main').innerText();
  for (const phrase of cfg.legal.mustContain) {
    expect(mainText).toContain(phrase);
  }
  const emails = [...new Set(mainText.match(EMAIL_PATTERN) ?? [])];
  expect(emails).toEqual([cfg.legal.contactEmail]);
  await expect(page.locator(`a[href="mailto:${cfg.legal.contactEmail}"]`)).toHaveCount(1);
});

test('U07 利回り・イベント・物価が架空のモデル値である旨が、トップに常時表示されている', async ({ page }) => {
  await page.goto('/');
  const notice = page.locator('p.notice');
  await expect(notice).toBeVisible();
  await expect(notice).toContainText(cfg.page.notice);
});

test('U08 テーマはライト固定（OS がダークでも白地。color-scheme は light）', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await page.goto('/');
  await expect(page.locator('meta[name="color-scheme"]')).toHaveAttribute('content', 'light');
  const style = await page.evaluate(() => ({
    background: getComputedStyle(document.body).backgroundColor,
    scheme: getComputedStyle(document.documentElement).colorScheme,
  }));
  expect(parseRgb(style.background)).toEqual([255, 255, 255]);
  expect(style.scheme).toBe('light');
});

test('U09 絵文字がなく、alert・confirm・prompt を使わず、外部への通信は GA4 の読み込みだけ', async ({ page }) => {
  const dialogs = [];
  page.on('dialog', (dialog) => {
    dialogs.push(dialog.type());
    dialog.dismiss();
  });
  const emojiPattern = /\p{Extended_Pictographic}/u;
  for (const pagePath of ['/', cfg.legal.path]) {
    await page.goto(pagePath);
    const text = await page.locator('body').innerText();
    expect(emojiPattern.test(text), `${pagePath} に絵文字がある`).toBe(false);
  }
  expect(dialogs, 'ネイティブのダイアログが出た').toEqual([]);
  expect(isolation.externalRequests, '同じオリジン以外への通信').toEqual([]);
});

test('U10 キーボードだけで操作できる：Tab でスキップリンクが出て、Enter で本文へ移り、フォーカスの枠が消えていない', async ({ page }) => {
  await page.goto('/');
  await page.keyboard.press('Tab');
  const skip = page.getByRole('link', { name: cfg.page.skipLinkText, exact: true });
  await expect(skip).toBeFocused();
  const box = await skip.boundingBox();
  expect(box.y, 'スキップリンクが画面の外にある').toBeGreaterThanOrEqual(0);
  expect(await skip.evaluate((element) => getComputedStyle(element).outlineStyle)).not.toBe('none');
  await page.keyboard.press('Enter');
  await expect(page.locator('#main')).toBeFocused();
});
