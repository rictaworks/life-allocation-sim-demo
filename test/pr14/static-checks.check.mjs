/**
 * PR #14（Issue #2 Domain Core：配分の検証・自動調整・乱数・マスタ定数）の軽い検査。
 * Domain Core の計算や規則は、ここでは書き直さない。xUnit のテストの実行・ビルド・ソースの字面・CI の結果だけを見る。
 *
 * 実行（リポジトリのルートで）:
 *   node --test --test-reporter=spec test/pr14/static-checks.check.mjs
 *
 * 前提:
 *  - .NET 8 SDK、Node.js 20 以上、gh（認証済み）。
 *
 * 検査すること:
 *  1. dotnet test src/core/DomainCore.Tests が成功し、合計が 171 件である（違えば、実際の件数を失敗の文言に出す）。
 *  2. dotnet build src/core/DomainCore -c Release が成功する。警告の件数を出す（0 件は求めない）。
 *     差分ビルドではコンパイルを飛ばした分の警告が出ないため、--no-incremental で作り直して数える。
 *  3. src/core/DomainCore/ の .cs に、float・double・decimal の型の使用が無い
 *     （型のキーワード、System.Single・Double・Decimal の型名、実数のリテラル）。
 *  4. src/core/DomainCore/ の .cs に、System.Random が無い（using System; の下で Random と書いたものを含む）。
 *     コメント行にあるものは対象外とし、場所だけを出す。
 *  5. src/core/DomainCore/Config/ の各 .cs の先頭 20 行に「架空」がある。
 *  6. src/core/DomainCore/ のうち Config/ の外の .cs に、日本語（ひらがな・カタカナ・漢字）を含む文字列リテラルが無い。
 *  7. gh pr checks 14 で、CI の core と backend が pass である。実行中なら --watch で完了を待つ。
 *
 * ソースの検査（3・4・6）の決まり:
 *  - 行単位の軽い検査。コメント行（// と /// で始まる行）は除く。
 *  - 行末のコメントとブロックコメントは除かない。そこに当たる字面があれば違反として出す（安全側に倒す）。
 *  - 6 は、コメント行以外の行に日本語があれば違反とする（複数行にわたるリテラルの途中の行も拾うため）。
 *  - bin/ と obj/（ビルド成果物）は見ない。
 *
 * 作りの決まり:
 *  - 子プロセスは execFile に引数の配列を渡して起動する（シェルを介さない）。すべてに timeout を付ける。
 *  - 期待値は、ファイルの位置と検査の内容だけで書く。Domain Core の数値や規則の値は書き写さない。
 *
 * 実行すると起きること:
 *  - dotnet test・dotnet build が、除外対象のビルド成果物（src/core の bin・obj）を書く。
 *  - gh で、PR の先頭コミットと CI の結果を GitHub に問い合わせる。
 */
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');

const PR_NUMBER = '14';
// PR #14 の dotnet test の合計として示された件数
const EXPECTED_TEST_TOTAL = 171;
const CI_WORKFLOW_NAME = 'CI';
const REQUIRED_CI_JOBS = ['core', 'backend'];
const WATCH_INTERVAL_SECONDS = '20';

const DOMAIN_CORE_DIR = 'src/core/DomainCore';
const DOMAIN_CORE_TESTS_DIR = 'src/core/DomainCore.Tests';
const CONFIG_DIR = 'src/core/DomainCore/Config';
const BUILD_OUTPUT_DIR_NAMES = ['bin', 'obj'];
const HEADER_LINE_COUNT = 20;
const FICTIONAL_MARK = '架空';

const TIMEOUTS_MS = { dotnetTest: 600000, dotnetBuild: 600000, gh: 60000, ghWatch: 1200000 };
// node:test の時間切れは、子プロセスの時間切れより後にする（子プロセスの時間切れの報告を優先する）
const TEST_TIMEOUT_MARGIN_MS = 30000;
const MAX_BUFFER = 64 * 1024 * 1024;
const TAIL_LINES = 40;
// 集計行を英語で読むため、dotnet の表示言語を固定する
const DOTNET_ENV = { DOTNET_CLI_UI_LANGUAGE: 'en', DOTNET_NOLOGO: '1', DOTNET_CLI_TELEMETRY_OPTOUT: '1' };

const COMMENT_LINE_PATTERN = /^\s*\/\//;
// 3：型のキーワードと型名、実数のリテラル（小数点・指数・f/d/m の接尾辞）
const FLOATING_TYPE_PATTERNS = [
  { label: '型のキーワード・型名', pattern: /\b(?:float|double|decimal|Single|Double|Decimal)\b/ },
  {
    label: '実数のリテラル',
    pattern: /(?<![\w.])(?:\d[\d_]*)?\.\d[\d_]*(?:[eE][+-]?\d[\d_]*)?[fFdDmM]?\b|\b\d[\d_]*[eE][+-]?\d[\d_]*[fFdDmM]?\b|\b\d[\d_]*[fFdDmM]\b/,
  },
];
// 4：語として独立した Random。System.Random と、using System; の下の Random に当たる（SeededRandom などの一部には当たらない）
const SYSTEM_RANDOM_PATTERN = /\bRandom\b/;
// 6：ひらがな・カタカナ・漢字
const JAPANESE_PATTERN = /[\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Han}]/u;

function repoPath(relativePath) {
  return path.join(REPO_ROOT, relativePath);
}

function toRepoRelative(absolutePath) {
  return path.relative(REPO_ROOT, absolutePath).split(path.sep).join('/');
}

function testTimeout(childTimeoutMs) {
  return childTimeoutMs + TEST_TIMEOUT_MARGIN_MS;
}

function tail(text) {
  return String(text).replace(/\s+$/, '').split('\n').slice(-TAIL_LINES).join('\n');
}

/**
 * 子プロセスを起動し、終了を待つ。シェルは介さない（execFile に引数の配列を渡す）。
 * 非 0 の終了・時間切れ・起動の失敗は例外にせず、結果として返す。判定は呼び出し側で明示的に行う。
 * 標準入力はすぐに閉じる（入力を待つ子プロセスが止まらないようにする）。
 */
function runCommand(file, args, { timeoutMs, env }) {
  if (!Number.isInteger(timeoutMs) || timeoutMs <= 0) {
    throw new Error(`timeoutMs が正の整数ではありません: ${timeoutMs}`);
  }
  return new Promise((resolve) => {
    let stdinError = null;
    const child = execFile(
      file,
      args,
      {
        cwd: REPO_ROOT,
        env: { ...process.env, ...env },
        timeout: timeoutMs,
        killSignal: 'SIGTERM',
        maxBuffer: MAX_BUFFER,
        windowsHide: true,
        encoding: 'utf8',
      },
      (error, stdout, stderr) => {
        const errorCode = error !== null && typeof error.code === 'string' ? error.code : null;
        resolve({
          command: [file, ...args].join(' '),
          timeoutMs,
          exitCode: error === null ? 0 : Number.isInteger(error.code) ? error.code : null,
          signal: error !== null && error.signal ? error.signal : null,
          timedOut: error !== null && error.killed === true && errorCode === null,
          errorCode,
          stdinError,
          stdout,
          stderr,
        });
      },
    );
    child.stdin.on('error', (error) => {
      stdinError = error;
    });
    child.stdin.end();
  });
}

function describeRun(result) {
  return [
    `コマンド: ${result.command}`,
    `終了コード: ${result.exitCode}・シグナル: ${result.signal}・時間切れ: ${result.timedOut}（上限 ${result.timeoutMs} ms）`,
    `起動などの失敗: ${result.errorCode}・標準入力の失敗: ${result.stdinError === null ? null : result.stdinError.message}`,
    `--- 標準出力（末尾 ${TAIL_LINES} 行）---`,
    tail(result.stdout),
    `--- 標準エラー出力（末尾 ${TAIL_LINES} 行）---`,
    tail(result.stderr),
  ].join('\n');
}

function assertExitZero(result, what) {
  assert.equal(result.exitCode, 0, `${what} が成功しませんでした。\n${describeRun(result)}`);
}

function parseJsonOutput(result, what) {
  try {
    return JSON.parse(result.stdout);
  } catch (error) {
    throw new Error(`${what} の出力が JSON として読めません: ${error.message}\n${describeRun(result)}`);
  }
}

/** dotnet test（既定の表示）の集計行「Passed!  - Failed: 0, Passed: N, Skipped: 0, Total: N, ...」を読む。 */
function readTestSummary(result) {
  const output = `${result.stdout}\n${result.stderr}`;
  const pattern = /^(Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+),\s+Total:\s+(\d+),/gm;
  const matches = [...output.matchAll(pattern)];
  if (matches.length !== 1) {
    throw new Error(`dotnet test の集計行が 1 行ではありません（${matches.length} 行）。\n${describeRun(result)}`);
  }
  const [, verdict, failed, passed, skipped, total] = matches[0];
  return { verdict, failed: Number(failed), passed: Number(passed), skipped: Number(skipped), total: Number(total) };
}

/** dotnet build の集計行（「N Warning(s)」「N Error(s)」）と、警告の行（重複を除く）を読む。 */
function readBuildSummary(result) {
  const output = `${result.stdout}\n${result.stderr}`;
  const warnings = [...output.matchAll(/^\s*(\d+) Warning\(s\)\s*$/gm)];
  const errors = [...output.matchAll(/^\s*(\d+) Error\(s\)\s*$/gm)];
  if (warnings.length !== 1 || errors.length !== 1) {
    throw new Error(
      `dotnet build の集計行が 1 行ずつではありません（Warning(s) ${warnings.length} 行・Error(s) ${errors.length} 行）。\n${describeRun(result)}`,
    );
  }
  const warningLines = [
    ...new Set(
      output
        .split(/\r?\n/)
        .filter((line) => /: warning [A-Z]+\d+:/.test(line))
        .map((line) => line.trim()),
    ),
  ];
  return { warnings: Number(warnings[0][1]), errors: Number(errors[0][1]), warningLines };
}

/** ディレクトリの下の .cs を集める（bin・obj と、excludeDirs に挙げたディレクトリを除く）。リポジトリからの相対パスで返す。 */
function listCsFiles(relativeDir, excludeDirs) {
  const excluded = new Set(excludeDirs);
  const found = [];
  const walk = (absoluteDir) => {
    for (const entry of readdirSync(absoluteDir, { withFileTypes: true })) {
      const absolutePath = path.join(absoluteDir, entry.name);
      if (entry.isDirectory()) {
        if (!BUILD_OUTPUT_DIR_NAMES.includes(entry.name) && !excluded.has(toRepoRelative(absolutePath))) {
          walk(absolutePath);
        }
      } else if (entry.isFile() && entry.name.endsWith('.cs')) {
        found.push(toRepoRelative(absolutePath));
      }
    }
  };
  walk(repoPath(relativeDir));
  if (found.length === 0) {
    throw new Error(`${relativeDir} の下に .cs がありません`);
  }
  return found.sort();
}

function sourceLines(relativePath) {
  return readFileSync(repoPath(relativePath), 'utf8')
    .split(/\r?\n/)
    .map((line, index) => ({ lineNumber: index + 1, line, isComment: COMMENT_LINE_PATTERN.test(line) }));
}

/** pattern に当たる行を集める。inComments が false ならコード行（コメント行以外）、true ならコメント行だけを見る。 */
function findLines(files, pattern, inComments) {
  const hits = [];
  for (const file of files) {
    for (const { lineNumber, line, isComment } of sourceLines(file)) {
      if (isComment === inComments && pattern.test(line)) {
        hits.push(`${file}:${lineNumber}: ${line.trim()}`);
      }
    }
  }
  return hits;
}

function hasFictionalMarkInHeader(relativePath) {
  return sourceLines(relativePath)
    .slice(0, HEADER_LINE_COUNT)
    .some(({ line }) => line.includes(FICTIONAL_MARK));
}

/** gh pr checks の結果のうち、CI の core と backend の行を返す。 */
async function readRequiredChecks() {
  const result = await runCommand('gh', ['pr', 'checks', PR_NUMBER, '--json', 'name,state,bucket,workflow,link'], {
    timeoutMs: TIMEOUTS_MS.gh,
    env: {},
  });
  // gh pr checks は、失敗があると 1、実行中があると 8 で終わる。どちらも JSON を出すため、判定は内容で行う
  if (![0, 1, 8].includes(result.exitCode)) {
    throw new Error(`gh pr checks が失敗しました。\n${describeRun(result)}`);
  }
  const checks = parseJsonOutput(result, 'gh pr checks');
  if (!Array.isArray(checks)) {
    throw new Error(`gh pr checks の出力が配列ではありません。\n${describeRun(result)}`);
  }
  return checks.filter((check) => check.workflow === CI_WORKFLOW_NAME && REQUIRED_CI_JOBS.includes(check.name));
}

describe(`1. dotnet test ${DOMAIN_CORE_TESTS_DIR}`, () => {
  let run = null;

  before(
    async () => {
      run = await runCommand('dotnet', ['test', DOMAIN_CORE_TESTS_DIR], { timeoutMs: TIMEOUTS_MS.dotnetTest, env: DOTNET_ENV });
    },
    { timeout: testTimeout(TIMEOUTS_MS.dotnetTest) },
  );

  test('成功する（失敗 0 件）', (t) => {
    assertExitZero(run, 'dotnet test');
    const summary = readTestSummary(run);
    t.diagnostic(`合計 ${summary.total} 件・合格 ${summary.passed} 件・失敗 ${summary.failed} 件・スキップ ${summary.skipped} 件`);
    assert.equal(summary.failed, 0, `失敗が ${summary.failed} 件あります。\n${describeRun(run)}`);
    assert.ok(summary.total > 0, 'テストが 1 件も実行されていません');
  });

  test(`合計が ${EXPECTED_TEST_TOTAL} 件である`, () => {
    const summary = readTestSummary(run);
    assert.equal(summary.total, EXPECTED_TEST_TOTAL, `合計は ${summary.total} 件です（期待は ${EXPECTED_TEST_TOTAL} 件）`);
  });
});

test(
  `2. dotnet build ${DOMAIN_CORE_DIR} -c Release が成功する（警告の件数を出す）`,
  { timeout: testTimeout(TIMEOUTS_MS.dotnetBuild) },
  async (t) => {
    const run = await runCommand('dotnet', ['build', DOMAIN_CORE_DIR, '-c', 'Release', '--no-incremental'], {
      timeoutMs: TIMEOUTS_MS.dotnetBuild,
      env: DOTNET_ENV,
    });
    assertExitZero(run, 'dotnet build');
    const summary = readBuildSummary(run);
    t.diagnostic(`警告 ${summary.warnings} 件・エラー ${summary.errors} 件`);
    for (const line of summary.warningLines) {
      t.diagnostic(`警告: ${line}`);
    }
    assert.equal(summary.errors, 0, `エラーが ${summary.errors} 件あります。\n${describeRun(run)}`);
  },
);

describe(`ソースの字面（${DOMAIN_CORE_DIR}/ の .cs。bin・obj を除く）`, () => {
  test('3. float・double・decimal の型の使用が無い（コメント行を除く）', (t) => {
    const files = listCsFiles(DOMAIN_CORE_DIR, []);
    t.diagnostic(`対象 ${files.length} ファイル`);
    const hits = FLOATING_TYPE_PATTERNS.flatMap(({ label, pattern }) => findLines(files, pattern, false).map((hit) => `${label}: ${hit}`));
    assert.deepEqual(hits, [], `float・double・decimal の型の使用があります:\n${hits.join('\n')}`);
  });

  test('4. System.Random が無い（コメント行を除く）', (t) => {
    const files = listCsFiles(DOMAIN_CORE_DIR, []);
    t.diagnostic(`対象 ${files.length} ファイル`);
    for (const hit of findLines(files, SYSTEM_RANDOM_PATTERN, true)) {
      t.diagnostic(`コメント行にあり（対象外）: ${hit}`);
    }
    const hits = findLines(files, SYSTEM_RANDOM_PATTERN, false);
    assert.deepEqual(hits, [], `System.Random の使用があります:\n${hits.join('\n')}`);
  });

  test(`5. ${CONFIG_DIR}/ の各 .cs の先頭 ${HEADER_LINE_COUNT} 行に「${FICTIONAL_MARK}」がある`, (t) => {
    const files = listCsFiles(CONFIG_DIR, []);
    t.diagnostic(`対象 ${files.length} ファイル`);
    const missing = files.filter((file) => !hasFictionalMarkInHeader(file));
    assert.deepEqual(missing, [], `先頭 ${HEADER_LINE_COUNT} 行に「${FICTIONAL_MARK}」が無いファイルがあります:\n${missing.join('\n')}`);
  });

  test(`6. ${CONFIG_DIR}/ の外の .cs に、日本語を含む文字列リテラルが無い（コメント行を除く）`, (t) => {
    const files = listCsFiles(DOMAIN_CORE_DIR, [CONFIG_DIR]);
    t.diagnostic(`対象 ${files.length} ファイル`);
    const hits = findLines(files, JAPANESE_PATTERN, false);
    assert.deepEqual(hits, [], `コメント行以外に日本語があります:\n${hits.join('\n')}`);
  });
});

test(
  `7. gh pr checks ${PR_NUMBER} で CI の ${REQUIRED_CI_JOBS.join('・')} が pass である`,
  { timeout: testTimeout(TIMEOUTS_MS.gh * 3 + TIMEOUTS_MS.ghWatch) },
  async (t) => {
    const head = await runCommand('gh', ['pr', 'view', PR_NUMBER, '--json', 'headRefOid', '--jq', '.headRefOid'], {
      timeoutMs: TIMEOUTS_MS.gh,
      env: {},
    });
    assertExitZero(head, 'gh pr view');
    t.diagnostic(`PR #${PR_NUMBER} の先頭コミット: ${head.stdout.trim()}`);

    let checks = await readRequiredChecks();
    if (checks.some((check) => check.bucket === 'pending')) {
      t.diagnostic(`実行中のため、gh pr checks ${PR_NUMBER} --watch --interval ${WATCH_INTERVAL_SECONDS} で完了を待ちます`);
      const watch = await runCommand('gh', ['pr', 'checks', PR_NUMBER, '--watch', '--interval', WATCH_INTERVAL_SECONDS], {
        timeoutMs: TIMEOUTS_MS.ghWatch,
        env: {},
      });
      // --watch は、すべて完了して失敗が無ければ 0、失敗があれば 1 で終わる。判定は、完了後に取り直した内容で行う
      assert.ok([0, 1].includes(watch.exitCode), `gh pr checks --watch が完了しませんでした。\n${describeRun(watch)}`);
      checks = await readRequiredChecks();
    }
    for (const check of checks) {
      t.diagnostic(`${check.workflow} / ${check.name}: ${check.bucket}（${check.state}） ${check.link}`);
    }
    for (const job of REQUIRED_CI_JOBS) {
      const entries = checks.filter((check) => check.name === job);
      assert.ok(entries.length > 0, `CI の ${job} が見つかりません`);
      for (const entry of entries) {
        assert.equal(entry.bucket, 'pass', `CI の ${job} が pass ではありません: ${entry.bucket}（${entry.state}） ${entry.link}`);
      }
    }
  },
);
