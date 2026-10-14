// U22〜U29：保存 API（セッション・ゲームの開始・ターンの保存・リセット後の表示・入口の検査）。
// U22〜U26 は同じブラウザ文脈で 1 つのゲームを進める（手順の順番に意味がある）。
// 新規セッションは IP あたり 1 分 10 件まで。このファイルは新規セッションを 3 件作る（U22 で 2 件、U27 で 1 件）。
// 保存とゲーム開始は 1 秒 1 件まで。要求の間は waitForRateWindow で間隔を空ける。
const { test, expect } = require('@playwright/test');
const {
  cfg,
  waitForRateWindow,
  isolateContext,
  browserFetch,
  apiRequest,
  newApiContext,
  assertServersRunning,
  seedOf,
  randomCookieValue,
  readRepoFile,
} = require('./helpers');

const paths = cfg.api.paths;
const errors = cfg.api.errors;
const difficulties = cfg.api.difficulties;
const JSON_HEADERS = { 'Content-Type': 'application/json' };
const shared = { context: undefined, page: undefined, seed: undefined, firstTurn: undefined };

function expectApiError(response, code) {
  const expected = errors[code];
  expect(response.status, `${code} の HTTP 状態`).toBe(expected.status);
  expect(JSON.parse(response.text)).toEqual({ error: { code, message: expected.message } });
}

// U24 以降は、U23 で作ったゲームが同じセッションに残っていることが前提。前の手順が失敗してワーカーが作り直されたときは、
// ここで前提の欠落を示す（Cookie の無い状態の 401 と取り違えないため）。
function requireStartedGame() {
  expect(shared.seed, '前の手順（U23）でゲームが作られている必要がある（前の手順の失敗の影響）').toBeDefined();
}

function turnRequest(turnNo, [livingPct, savingPct, investPct], extra = {}) {
  return { turn_no: turnNo, living_pct: livingPct, saving_pct: savingPct, invest_pct: investPct, website: '', ...extra };
}

function saveTurn(page, turnNo, allocation, extra) {
  return browserFetch(page, 'POST', paths.turns, {
    body: JSON.stringify(turnRequest(turnNo, allocation, extra)),
    headers: JSON_HEADERS,
  });
}

test.beforeAll(async ({ browser }) => {
  await assertServersRunning();
  shared.context = await browser.newContext();
  await isolateContext(shared.context, { blockUnity: true });
  shared.page = await shared.context.newPage();
  await shared.page.goto(cfg.legal.path);
});

test.afterAll(async () => {
  if (shared.context !== undefined) {
    await shared.context.close();
  }
});

test('U22 セッションの発行：新規は 201 で Cookie が付き、有効な Cookie があれば 200（新規は作らない）', async () => {
  const anonymous = await newApiContext();
  try {
    const created = await apiRequest(anonymous, 'POST', paths.session, { json: {} });
    expect(created.status).toBe(201);
    expect(JSON.parse(created.text)).toEqual({ session: { status: 'created' } });
    const setCookies = created.headersArray.filter((header) => header.name.toLowerCase() === 'set-cookie');
    expect(setCookies, 'Set-Cookie の数').toHaveLength(1);
    const [pair, ...attributes] = setCookies[0].value.split(';').map((part) => part.trim());
    const separator = pair.indexOf('=');
    const name = pair.slice(0, separator);
    const value = pair.slice(separator + 1);
    expect(name).toBe(cfg.api.cookieName);
    expect(value, 'Cookie の値（32 バイトの乱数を Base64URL にした 43 文字）').toMatch(new RegExp(cfg.api.cookieTokenPattern));
    const lowered = attributes.map((attribute) => attribute.toLowerCase());
    expect(lowered).toEqual(
      expect.arrayContaining(['httponly', 'secure', 'samesite=lax', 'path=/', `max-age=${cfg.api.cookieMaxAgeSeconds}`])
    );
    expect(lowered.some((attribute) => attribute.startsWith('domain=')), 'Domain の指定はない').toBe(false);

    const existing = await apiRequest(anonymous, 'POST', paths.session, {
      json: {},
      headers: { Cookie: `${name}=${value}` },
    });
    expect(existing.status, '有効な Cookie がある再要求').toBe(200);
    expect(JSON.parse(existing.text)).toEqual({ session: { status: 'existing' } });
    expect(existing.headersArray.some((header) => header.name.toLowerCase() === 'set-cookie')).toBe(false);
  } finally {
    await anonymous.dispose();
  }

  const created = await browserFetch(shared.page, 'POST', paths.session, { body: '{}', headers: JSON_HEADERS });
  expect(created.status).toBe(201);
  const cookie = (await shared.context.cookies(cfg.baseUrl)).find((item) => item.name === cfg.api.cookieName);
  expect(cookie, 'ブラウザに保存された Cookie').toBeDefined();
  expect(cookie.httpOnly).toBe(true);
  expect(cookie.secure).toBe(true);
  expect(cookie.sameSite).toBe('Lax');
  expect(cookie.path).toBe('/');
  expect(
    Math.abs(cookie.expires - Date.now() / 1000 - cfg.api.cookieMaxAgeSeconds),
    '有効期限は発行から 7 日'
  ).toBeLessThan(120);
  const again = await browserFetch(shared.page, 'POST', paths.session, { body: '{}', headers: JSON_HEADERS });
  expect(again.status, 'ブラウザが Cookie を送り、新規は作らない').toBe(200);
});

test('U23 ゲームの開始：難易度を選ぶと 201 で新しいゲームが返り、取得すると同じゲームが見える', async () => {
  await waitForRateWindow();
  const started = await browserFetch(shared.page, 'POST', paths.games, {
    body: JSON.stringify({ difficulty: difficulties.start }),
    headers: JSON_HEADERS,
  });
  expect(started.status).toBe(201);
  const game = JSON.parse(started.text).game;
  expect(game).toMatchObject({
    difficulty: difficulties.start,
    status: 'playing',
    current_age: 22,
    turns: [],
    evaluation: null,
  });
  expect(game.state).toMatchObject({ age: 22, savings_yen: 0, investment_yen: 0, struggling: false });
  shared.seed = seedOf(started.text);
  expect(BigInt(shared.seed) >= 0n && BigInt(shared.seed) <= BigInt(cfg.api.maxSeed), 'seed は 0 以上 2^63−1').toBe(true);

  const fetched = await browserFetch(shared.page, 'GET', paths.game);
  expect(fetched.status).toBe(200);
  expect(seedOf(fetched.text), '取得したゲームの seed は開始時と同じ').toBe(shared.seed);
});

test('U24 ゲームの開始の誤り：難易度が不正・無いときは 422。seed を送っても無視し、新しいゲームで上書きする', async () => {
  await waitForRateWindow();
  expectApiError(
    await browserFetch(shared.page, 'POST', paths.games, {
      body: JSON.stringify({ difficulty: difficulties.unknown }),
      headers: JSON_HEADERS,
    }),
    'difficulty_unknown'
  );
  await waitForRateWindow();
  expectApiError(await browserFetch(shared.page, 'POST', paths.games, { body: '{}', headers: JSON_HEADERS }), 'difficulty_missing');

  await waitForRateWindow();
  const restarted = await browserFetch(shared.page, 'POST', paths.games, {
    body: JSON.stringify({ difficulty: difficulties.restart, seed: 1 }),
    headers: JSON_HEADERS,
  });
  expect(restarted.status).toBe(201);
  expect(JSON.parse(restarted.text).game).toMatchObject({ difficulty: difficulties.restart, turns: [] });
  expect(seedOf(restarted.text), '要求の seed は無視される').not.toBe('1');
  expect(seedOf(restarted.text), '新しいゲームの seed は前のゲームと違う').not.toBe(shared.seed);
  shared.seed = seedOf(restarted.text);
});

test('U25 ターンの保存：新規は 201、同じ番号・同じ配分の再送は 200、違う配分は 409、規則違反は 422、ハニーポットは保存しない', async () => {
  requireStartedGame();
  await waitForRateWindow();
  const first = await saveTurn(shared.page, 1, [50, 30, 20]);
  expect(first.status).toBe(201);
  const body = JSON.parse(first.text);
  expect(body.turn).toMatchObject({ turn_no: 1, age: 22, living_pct: 50, saving_pct: 30, invest_pct: 20 });
  for (const key of ['savings_after', 'invest_after', 'satisfaction_after']) {
    expect(Number.isInteger(body.turn[key]), `${key} は整数（サーバーの計算値）`).toBe(true);
  }
  expect(body.turn).toHaveProperty('event_code');
  expect(body.game.turns).toHaveLength(1);
  expect(body.game.current_age, '次に迎える年齢').toBe(23);
  shared.firstTurn = body.turn;

  await waitForRateWindow();
  const resent = await saveTurn(shared.page, 1, [50, 30, 20]);
  expect(resent.status, '同じ番号・同じ配分の再送').toBe(200);
  expect(JSON.parse(resent.text).turn, '保存済みの結果が返る').toEqual(shared.firstTurn);
  expect(JSON.parse(resent.text).game.turns).toHaveLength(1);

  await waitForRateWindow();
  expectApiError(await saveTurn(shared.page, 1, [40, 40, 20]), 'turn_allocation_mismatch');
  await waitForRateWindow();
  expectApiError(await saveTurn(shared.page, 3, [50, 30, 20]), 'turn_conflict');
  await waitForRateWindow();
  expectApiError(await saveTurn(shared.page, 2, [50, 30, 30]), 'allocation_sum_mismatch');
  await waitForRateWindow();
  expectApiError(await saveTurn(shared.page, 2, [-5, 60, 45]), 'allocation_negative');
  await waitForRateWindow();
  expectApiError(await saveTurn(shared.page, 2, [52, 28, 20]), 'allocation_step_mismatch');

  await waitForRateWindow();
  const trap = await saveTurn(shared.page, 2, [50, 30, 20], { website: 'bot' });
  expect(trap.status, 'ハニーポットへの入力も、通常どおり 201 の形で返る').toBe(201);
  expect(JSON.parse(trap.text).turn, 'ハニーポットへの入力にも turn が返る').toHaveProperty('turn_no', 2);
  const afterTrap = await browserFetch(shared.page, 'GET', paths.game);
  expect(JSON.parse(afterTrap.text).game.turns, 'ハニーポットへの入力は保存されない（取得すると 1 件のまま）').toHaveLength(1);

  await waitForRateWindow();
  const second = await saveTurn(shared.page, 2, [50, 30, 20], { role: 'admin' });
  expect(second.status, '未知のキーは無視され、次の番号は保存される').toBe(201);
  expect(JSON.parse(second.text).game.turns).toHaveLength(2);
});

test('U26 連打の制限：同じ 1 秒の 2 件目は 429 になり、Retry-After が付く', async () => {
  requireStartedGame();
  await waitForRateWindow();
  const [a, b] = await Promise.all([saveTurn(shared.page, 3, [50, 30, 20]), saveTurn(shared.page, 3, [50, 30, 20])]);
  expect([a.status, b.status].sort(), '1 件は保存され、もう 1 件は 429').toEqual([201, 429]);
  const limited = a.status === 429 ? a : b;
  expectApiError(limited, 'rate_limited');
  expect(limited.retryAfter, 'Retry-After は 1 以上の整数（秒）').toMatch(/^[1-9]\d*$/);
  await waitForRateWindow();
  const fetched = await browserFetch(shared.page, 'GET', paths.game);
  expect(JSON.parse(fetched.text).game.turns, '保存は 1 件だけ増える').toHaveLength(3);
});

test('U27 リセット後の表示：ゲームが無いセッションからの保存は 404（game_not_found）の文言になる', async ({ browser }) => {
  const context = await browser.newContext();
  try {
    await isolateContext(context, { blockUnity: true });
    const page = await context.newPage();
    await page.goto(cfg.legal.path);
    const created = await browserFetch(page, 'POST', paths.session, { body: '{}', headers: JSON_HEADERS });
    expect(created.status).toBe(201);
    const fetched = await browserFetch(page, 'GET', paths.game);
    expect(JSON.parse(fetched.text)).toEqual({ game: null });
    expectApiError(await saveTurn(page, 1, [50, 30, 20]), 'game_not_found');
  } finally {
    await context.close();
  }
  expect(readRepoFile(cfg.unity.uiTextsPath), '404 のとき Unity が出す文言（ソースの文言を確認）').toContain(
    cfg.unity.resetNoticeText
  );
});

test('U28 リセット後に残る古い Cookie：保存は 401（session_required）、取得は game=null、開始は 401', async () => {
  const stale = { Cookie: `${cfg.api.cookieName}=${randomCookieValue()}` };
  const apiContext = await newApiContext();
  try {
    expectApiError(
      await apiRequest(apiContext, 'POST', paths.turns, { json: turnRequest(1, [50, 30, 20]), headers: stale }),
      'session_required'
    );
    const fetched = await apiRequest(apiContext, 'GET', paths.game, { headers: stale });
    expect(fetched.status).toBe(200);
    expect(JSON.parse(fetched.text)).toEqual({ game: null });
    expectApiError(
      await apiRequest(apiContext, 'POST', paths.games, { json: { difficulty: difficulties.start }, headers: stale }),
      'session_required'
    );
  } finally {
    await apiContext.dispose();
  }
});

test('U29 入口の検査：他の接続元の Origin は 403、JSON 以外は 415、大きすぎる本体は 413、壊れた JSON は 400、経路が無ければ 404', async () => {
  const apiContext = await newApiContext();
  try {
    expectApiError(
      await apiRequest(apiContext, 'POST', paths.session, { json: {}, headers: { Origin: cfg.api.foreignOrigin } }),
      'origin_forbidden'
    );
    expectApiError(
      await apiRequest(apiContext, 'POST', paths.session, { rawBody: '{}', headers: { 'Content-Type': 'text/plain' } }),
      'unsupported_media_type'
    );
    expectApiError(
      await apiRequest(apiContext, 'POST', paths.games, {
        rawBody: JSON.stringify({ difficulty: difficulties.start, padding: 'x'.repeat(cfg.api.maxBodyBytes) }),
        headers: JSON_HEADERS,
      }),
      'payload_too_large'
    );
    expectApiError(await apiRequest(apiContext, 'POST', paths.session, { rawBody: '{', headers: JSON_HEADERS }), 'bad_request');
    expectApiError(await apiRequest(apiContext, 'GET', paths.unknown), 'not_found');
    const foreign = await apiRequest(apiContext, 'GET', paths.game, { headers: { Origin: cfg.api.foreignOrigin } });
    expect(foreign.headers['access-control-allow-origin'], '他のオリジンへの CORS の許可はない').toBeUndefined();
  } finally {
    await apiContext.dispose();
  }
});
