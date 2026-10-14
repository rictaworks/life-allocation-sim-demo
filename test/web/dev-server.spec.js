// @ts-check
// 開発用サーバー（src/web/dev-server.mjs）の検査。起動条件・静的配信・/api の転送を確かめる。
const { test, expect } = require('@playwright/test');
const support = require('./support');

test.describe('開発用サーバーの起動条件（APP_ENV）', () => {
  const refusedEnvs = [
    {},
    { APP_ENV: '' },
    { APP_ENV: 'production' },
    { APP_ENV: 'Development' },
    { APP_ENV: ' development' },
    { APP_ENV: 'test' },
  ];

  for (const env of refusedEnvs) {
    test(`APP_ENV が development でなければ起動を拒否する（${JSON.stringify(env)}）`, async () => {
      const { startDevServer } = await support.importWebModule('dev-server.mjs');
      /** @type {{ close: () => Promise<void> } | null} */
      let started = null;
      /** @type {unknown} */
      let refusal = null;
      try {
        started = await startDevServer({
          env,
          host: '127.0.0.1',
          port: 0,
          root: support.WEB_ROOT,
          railsOrigin: support.UNREACHABLE_ORIGIN,
        });
      } catch (error) {
        refusal = error;
      }
      if (started !== null) await started.close();
      expect(started).toBeNull();
      expect(refusal).toBeInstanceOf(Error);
    });
  }

  test('CLI は APP_ENV が未設定のとき、案内を出して終了コード 1 で終わる', async () => {
    const { config } = await support.importWebModule('dev-server.config.mjs');
    const env = { ...process.env };
    delete env.APP_ENV;
    const result = support.runDevServerCli(env);
    expect(result.status).toBe(1);
    expect(result.stderr).toContain(config.messages.refused);
  });

  test('CLI は APP_ENV が production のとき、案内を出して終了コード 1 で終わる', async () => {
    const { config } = await support.importWebModule('dev-server.config.mjs');
    const env = { ...process.env, APP_ENV: 'production' };
    const result = support.runDevServerCli(env);
    expect(result.status).toBe(1);
    expect(result.stderr).toContain(config.messages.refused);
  });
});

test.describe('静的ファイルの配信', () => {
  /** @type {{ url: string, close: () => Promise<void> }} */
  let dev;

  test.beforeAll(async () => {
    dev = await support.startDevServer();
  });

  test.afterAll(async () => {
    await dev.close();
  });

  test('トップページは index.html を返す', async ({ request }) => {
    const res = await request.get(`${dev.url}/`);
    expect(res.status()).toBe(200);
    expect(res.headers()['content-type']).toContain('text/html');
    expect(await res.text()).toContain('<html lang="ja">');
  });

  test('/legal は legal.html を返す（Vercel の cleanUrls と同じ扱い）', async ({ request }) => {
    const res = await request.get(`${dev.url}/legal`);
    expect(res.status()).toBe(200);
    expect(await res.text()).toContain('<title>利用規約・免責事項・連絡先');
  });

  test('CSS と JavaScript は正しい Content-Type で返す', async ({ request }) => {
    const css = await request.get(`${dev.url}/css/site.css`);
    expect(css.status()).toBe(200);
    expect(css.headers()['content-type']).toContain('text/css');
    const js = await request.get(`${dev.url}/js/config.js`);
    expect(js.status()).toBe(200);
    expect(js.headers()['content-type']).toContain('text/javascript');
  });

  test('HEAD は本文を返さない', async ({ request }) => {
    const res = await request.head(`${dev.url}/`);
    expect(res.status()).toBe(200);
    expect(await res.body()).toHaveLength(0);
  });

  test('GET と HEAD 以外の静的ファイルへの要求は 405 で拒否する', async ({ request }) => {
    const res = await request.post(`${dev.url}/`, { data: 'x' });
    expect(res.status()).toBe(405);
    expect(res.headers()['allow']).toBe('GET, HEAD');
  });

  test('存在しないパスは 404 で、スタックトレースや内部パスを含まない', async ({ request }) => {
    const res = await request.get(`${dev.url}/no-such-page`);
    expect(res.status()).toBe(404);
    const body = await res.text();
    expect(body).not.toMatch(/Error|\bat\s|\/home\/|\\Users\\/);
  });

  test('ドットで始まるファイル（設定ファイルを含む）は配信しない', async ({ request }) => {
    const res = await request.get(`${dev.url}/.htmlvalidate.json`);
    expect(res.status()).toBe(404);
  });

  test('エンコードされたパス区切りによる上位ディレクトリの参照は配信しない', async ({ request }) => {
    const res = await request.get(`${dev.url}/..%2F..%2Ftest%2Fpackage.json`);
    expect(res.status()).toBe(404);
  });

  test('不正なパーセント符号化は 400 で拒否する', async ({ request }) => {
    const res = await request.get(`${dev.url}/%E0%A4%A`);
    expect(res.status()).toBe(400);
  });

  test('ディレクトリの一覧は返さない', async ({ request }) => {
    const res = await request.get(`${dev.url}/js/`);
    expect(res.status()).toBe(404);
  });
});

test.describe('/api の転送（Rails への代理）', () => {
  /** @type {{ origin: string, received: Array<{ method: string, url: string, headers: Record<string, string | string[] | undefined>, body: string }>, close: () => Promise<void> }} */
  let rails;
  /** @type {{ url: string, close: () => Promise<void> }} */
  let dev;

  test.beforeAll(async () => {
    rails = await support.startFakeRails();
    dev = await support.startDevServer({ railsOrigin: rails.origin });
  });

  test.beforeEach(() => {
    rails.received.length = 0;
  });

  test.afterAll(async () => {
    await dev.close();
    await rails.close();
  });

  test('X-Real-IP は接続元で付け直し、クライアントの X-Real-IP と X-Forwarded-* は捨てる', async ({ request }) => {
    const res = await request.post(`${dev.url}/api/session`, {
      headers: {
        'Content-Type': 'application/json',
        'X-Real-IP': '203.0.113.9',
        'X-Forwarded-For': '198.51.100.7',
        'X-Forwarded-Host': 'evil.example',
        'X-Forwarded-Proto': 'https',
      },
      data: '{}',
    });
    expect(res.status()).toBe(201);
    expect(rails.received).toHaveLength(1);
    const forwarded = rails.received[0].headers;
    expect(forwarded['x-real-ip']).toBe('127.0.0.1');
    expect(forwarded['x-forwarded-for']).toBeUndefined();
    expect(forwarded['x-forwarded-host']).toBeUndefined();
    expect(forwarded['x-forwarded-proto']).toBeUndefined();
  });

  test('Origin・Cookie・本体・クエリを変えずに転送し、Rails の応答を返す', async ({ request }) => {
    const cookie = '__Host-lasd_session=example-value';
    const res = await request.post(`${dev.url}/api/games/turns?check=1`, {
      headers: {
        'Content-Type': 'application/json',
        Origin: dev.url,
        Cookie: cookie,
      },
      data: '{"turn_no":1}',
    });
    expect(res.status()).toBe(201);
    expect(res.headers()['x-fake-rails']).toBe('yes');
    expect(await res.json()).toEqual({ ok: true, method: 'POST' });
    const sent = rails.received[0];
    expect(sent.method).toBe('POST');
    expect(sent.url).toBe('/api/games/turns?check=1');
    expect(sent.headers['origin']).toBe(dev.url);
    expect(sent.headers['cookie']).toBe(cookie);
    expect(sent.body).toBe('{"turn_no":1}');
  });

  test('Rails に接続できないときは 502 と案内を返し、内部情報を含まない', async ({ request }) => {
    const deadRails = await support.startFakeRails();
    const deadOrigin = deadRails.origin;
    await deadRails.close();
    const broken = await support.startDevServer({ railsOrigin: deadOrigin });
    try {
      const res = await request.get(`${broken.url}/api/game`);
      expect(res.status()).toBe(502);
      const body = await res.text();
      expect(body).not.toMatch(/ECONNREFUSED|Error|\bat\s|\/home\//);
    } finally {
      await broken.close();
    }
  });
});
