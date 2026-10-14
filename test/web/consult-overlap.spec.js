// @ts-check
// 相談ボタンと本文の重なりを、スクロール位置ごとに数える（受け入れ条件 4：スマホで本文を隠さない）。
// 固定の配置は幅 1280px 以上だけに使い、それより狭い幅では本文の流れの末尾に置く。
const { test, expect } = require('@playwright/test');
const support = require('./support');

const PAGES = ['/', '/legal'];
// 固定の配置にする最小の幅（src/web/css/site.css の @media と同じ値にする）
const FIXED_MIN_WIDTH_PX = 1280;
const NARROW_WIDTHS_PX = [320, 375, 414, 768, 1024, 1279];
const WIDE_WIDTHS_PX = [1280, 1440];
const VIEWPORT_HEIGHT_PX = 667;
// 相談ボタンの高さ（44px）より小さい刻みなら、重なりの区間を取りこぼさない
const SCROLL_STEP_PX = 10;
const CONSULT_LINK_NAME = 'ご相談はこちら';
const DECLINE_BUTTON_NAME = '拒否';
const GTM_PATTERN = /^https:\/\/www\.googletagmanager\.com\//;

/**
 * ブラウザ内で実行する。スクロール位置ごとに、相談ボタンと重なる本文の要素を数える。
 * 対象は文字を持つ葉の要素と canvas。相談ボタン自身と、表示されていない要素は除く。
 * ブラウザに送って実行するため、外側の変数を使わない。
 * @param {number} step スクロールの刻み（px）
 * @returns {{ scrollMax: number, checkedPositions: number, overlapPositions: number, samples: Array<{ y: number, hits: string[] }> }}
 */
function countOverlapsPerScrollPosition(step) {
  const link = document.querySelector('.consult-link');
  if (link === null) throw new Error('相談ボタンが見つかりません');
  const leaves = Array.from(document.body.querySelectorAll('*')).filter((el) => {
    if (el === link || link.contains(el)) return false;
    if (el.children.length !== 0) return false;
    return el.tagName === 'CANVAS' || el.textContent.trim() !== '';
  });
  const scrollMax = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
  const positions = [];
  for (let y = 0; y < scrollMax; y += step) positions.push(y);
  positions.push(scrollMax);
  let overlapPositions = 0;
  /** @type {Array<{ y: number, hits: string[] }>} */
  const samples = [];
  for (const y of positions) {
    window.scrollTo(0, y);
    const c = link.getBoundingClientRect();
    /** @type {string[]} */
    const hits = [];
    for (const el of leaves) {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) continue;
      if (getComputedStyle(el).visibility === 'hidden') continue;
      if (r.left < c.right && c.left < r.right && r.top < c.bottom && c.top < r.bottom) {
        hits.push(`${el.tagName.toLowerCase()}: ${el.textContent.trim().slice(0, 16)}`);
      }
    }
    if (hits.length > 0) {
      overlapPositions += 1;
      if (samples.length < 3) samples.push({ y, hits: hits.slice(0, 3) });
    }
  }
  window.scrollTo(0, 0);
  return { scrollMax, checkedPositions: positions.length, overlapPositions, samples };
}

/**
 * 相談ボタンの配置（computed の position）と、スクロール位置ごとの重なりを測る。
 * @param {import('@playwright/test').Page} page
 */
async function measureConsultLink(page) {
  const position = await page.getByRole('link', { name: CONSULT_LINK_NAME }).evaluate((el) => getComputedStyle(el).position);
  const result = await page.evaluate(countOverlapsPerScrollPosition, SCROLL_STEP_PX);
  return { position, ...result };
}

test.describe('相談ボタンと本文の重なり（スクロール位置ごと）', () => {
  /** @type {{ url: string, close: () => Promise<void> }} */
  let dev;

  test.beforeAll(async () => {
    dev = await support.startDevServer();
  });

  test.afterAll(async () => {
    await dev.close();
  });

  test.beforeEach(async ({ page }) => {
    // 外部の計測スクリプトは実際には読み込まない
    await page.route(GTM_PATTERN, (route) => route.fulfill({
      status: 200,
      contentType: 'text/javascript',
      body: '/* 検査用のスタブ */',
    }));
  });

  for (const pagePath of PAGES) {
    for (const width of [...NARROW_WIDTHS_PX, ...WIDE_WIDTHS_PX]) {
      const expectedPosition = width >= FIXED_MIN_WIDTH_PX ? 'fixed' : 'static';
      test(`幅 ${width}px の ${pagePath}：相談ボタンはどのスクロール位置でも本文と重ならない`, async ({ page }) => {
        await page.setViewportSize({ width, height: VIEWPORT_HEIGHT_PX });
        await page.goto(`${dev.url}${pagePath}`);
        const undecided = await measureConsultLink(page);
        await page.getByRole('button', { name: DECLINE_BUTTON_NAME }).click();
        const decided = await measureConsultLink(page);
        for (const [state, result] of [['同意の選択なし', undecided], ['同意を選択済み', decided]]) {
          expect(
            result.overlapPositions,
            `${state}：重なるスクロール位置 ${result.overlapPositions} / ${result.checkedPositions}。例：${JSON.stringify(result.samples)}`,
          ).toBe(0);
          expect(result.position, `${state}：相談ボタンの配置`).toBe(expectedPosition);
        }
      });
    }
  }
});
