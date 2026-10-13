// @ts-check
// E2E テスト（Playwright）の設定。テストファイルは test/ の下に置く。
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
  testDir: '.',
  forbidOnly: Boolean(process.env.CI),
  use: {
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
