// PR 15 のユーザーテスト（非エンジニア向けの手順を自動化したもの）。対象は開発用サーバー（APP_ENV=development）。
// 実行の前に、README の「ローカルでの起動」の手順 1〜3 で、再計算ホスト・Rails・開発用サーバーを起動する。
// 次のコマンドを test/ で実行する。実行の出力は、呼び出し側が WORK/inspect/ にテキストで保存する:
//   npx playwright test -c pr15/playwright.config.js
// 並列にしない（workers: 1）。新規セッションの発行は IP あたり 1 分 10 件までのため。
const { defineConfig, devices } = require('@playwright/test');
const cfg = require('./expectations.json');

module.exports = defineConfig({
  testDir: '.',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  forbidOnly: Boolean(process.env.CI),
  timeout: cfg.timeouts.testMs,
  expect: { timeout: cfg.timeouts.expectMs },
  reporter: [['list']],
  use: {
    baseURL: cfg.baseUrl,
    trace: 'off',
    screenshot: 'off',
    video: 'off',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
