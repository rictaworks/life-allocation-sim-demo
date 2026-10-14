// U00：開発用サーバーは APP_ENV が development のときだけ起動する（未設定・不明な値は起動を拒否する）。
// 子プロセスには実装のスクリプトを、引数の配列で渡す（シェルを介さない）。拒否の場合は待ち受けを始めない。
const { test, expect } = require('@playwright/test');
const { execFile } = require('node:child_process');
const path = require('node:path');
const { cfg } = require('./helpers');

const devServerScript = path.resolve(__dirname, '..', '..', cfg.devServer.path);

function runDevServer(appEnvValue) {
  const env = { ...process.env };
  delete env.APP_ENV;
  if (appEnvValue !== undefined) {
    env.APP_ENV = appEnvValue;
  }
  return new Promise((resolve) => {
    execFile(
      process.execPath,
      [devServerScript],
      { env, timeout: cfg.timeouts.devServerSpawnMs },
      (error, stdout, stderr) => {
        resolve({
          exitCode: error ? error.code : 0,
          killed: error ? error.killed === true : false,
          stdout,
          stderr,
        });
      }
    );
  });
}

test('U00 開発用サーバーは APP_ENV が development のときだけ起動し、それ以外は起動を拒否する', async () => {
  for (const value of [undefined, 'production', 'Development', '']) {
    const label = value === undefined ? '（未設定）' : `"${value}"`;
    const result = await runDevServer(value);
    expect(result.killed, `APP_ENV=${label} で起動を続けた（拒否されなかった）`).toBe(false);
    expect(result.exitCode, `APP_ENV=${label} の終了コード`).toBe(1);
    expect(result.stderr).toContain(cfg.devServer.refusedText);
  }
});
