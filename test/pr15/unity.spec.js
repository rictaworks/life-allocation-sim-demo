// U20〜U21：Unity の起動（ビルドがある場合）と、ビルドが読めないときに理由を表示すること。
// U20 は実際のビルド（src/web/unity/Build/）を読み込み、起動して描画されるまで待つ。時間がかかる（既定で最大 4 分）。
const { test, expect } = require('@playwright/test');
const { cfg, absoluteUrl, isolateContext, artifactPath, brightPixelFraction } = require('./helpers');

test('U20 Unity の画面が起動し、タイトル画面の描画が出る（ビルドがある場合）', async ({ page, context }) => {
  test.setTimeout(cfg.timeouts.unityTestMs);
  await isolateContext(context, { blockUnity: false });

  const pageErrors = [];
  const buildStatuses = new Map();
  const apiCalls = [];
  const apiPrefix = absoluteUrl(cfg.api.prefix);
  page.on('pageerror', (error) => pageErrors.push(error.message));
  page.on('response', (response) => {
    const url = response.url();
    const buildFile = cfg.unity.buildFiles.find((file) => url === absoluteUrl(file));
    if (buildFile !== undefined) {
      buildStatuses.set(buildFile, response.status());
    }
    if (url.startsWith(apiPrefix)) {
      apiCalls.push({ call: `${response.request().method()} ${new URL(url).pathname}`, status: response.status() });
    }
  });

  await page.goto('/');
  await page.waitForFunction(() => typeof window.createUnityInstance === 'function', undefined, {
    timeout: cfg.timeouts.unityStartMs,
  });
  await page.waitForFunction(
    (fragment) => {
      const status = document.getElementById('unity-status');
      return status.hidden || !status.textContent.includes(fragment);
    },
    cfg.unity.loadingFragment,
    { timeout: cfg.timeouts.unityStartMs }
  );

  const status = await page.locator('#unity-status').evaluate((element) => ({
    hidden: element.hidden,
    text: element.textContent,
  }));
  expect(status, '起動の後に、読み込みの状態や失敗の文言が残っている').toEqual({ hidden: true, text: '' });

  const canvas = page.locator('#unity-canvas');
  const box = await canvas.boundingBox();
  expect(box.width, '枠の幅').toBeGreaterThan(0);
  expect(box.height, '枠の高さ').toBeGreaterThan(0);

  // 起動の直後は Unity のスプラッシュ（暗い）が出る。タイトル画面（明るい背景）に変わるまで待つ（起動の上限まで）。
  await expect
    .poll(async () => brightPixelFraction(page, await canvas.screenshot()), {
      timeout: cfg.timeouts.unityStartMs,
      intervals: [1000],
    })
    .toBeGreaterThan(cfg.unity.minBrightPixelFraction);
  await canvas.screenshot({ path: artifactPath('pr15-unity-canvas.png') });

  for (const file of cfg.unity.buildFiles) {
    expect(buildStatuses.get(file), `${file} の読み込み`).toBe(200);
  }
  expect(pageErrors, 'ページのエラー').toEqual([]);
  expect(apiCalls.filter((entry) => entry.status >= 500), '起動中の保存 API の 5xx 応答').toEqual([]);
  const summary = apiCalls.map((entry) => `${entry.call} ${entry.status}`);
  console.log(`[U20] Unity が起動時に呼んだ保存 API: ${summary.length === 0 ? 'なし' : summary.join(' / ')}`);
  await page.screenshot({ path: artifactPath('pr15-unity-started.png') });
});

test('U21 ビルドが読み込めないときは、黙って失敗せず、理由を画面に表示する', async ({ page, context }) => {
  await isolateContext(context, { blockUnity: true });
  await page.goto('/');
  const status = page.locator('#unity-status');
  await expect(status).toBeVisible();
  await expect(status).toHaveText(cfg.unity.missingBuildText);
});
