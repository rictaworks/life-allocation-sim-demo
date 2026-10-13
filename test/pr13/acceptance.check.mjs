/**
 * PR #13（Issue #1 環境構築：骨組み・テスト基盤・CI）の受け入れ条件を、実装を実際に呼び出して検査する。
 *
 * 実行（リポジトリのルートで）:
 *   node --test --test-reporter=spec test/pr13/acceptance.check.mjs
 *
 * 前提:
 *  - Linux・macOS（WSL を含む）。Windows では npm・bundle などが .cmd のため、シェルを介さずに起動できない。
 *  - .NET 8 SDK、Ruby 3.4（src/backend で bundle install 済み）、Node.js 20 以上、gh（認証済み）。
 *  - origin/main を取得済み（git fetch origin）。
 *
 * 実行すると起きること:
 *  - test/ で npm ci を実行する（受け入れ条件 3）。npm ci は test/node_modules を作り直す。
 *  - dotnet test・rspec・rails runner が、除外対象のビルド成果物とログ（bin・obj・log・tmp）を書く。
 *  - gh で、PR の先頭コミットと CI の結果を GitHub に問い合わせる。
 *
 * 作りの決まり:
 *  - 子プロセスは execFile に引数の配列を渡して起動する（シェルを介さない）。すべてに timeout を付ける。
 *  - ファイル名は Playwright の testMatch（*.spec.* / *.test.*）に当たらない形にしている。
 *  - 子プロセスにだけ環境変数（expectations.json の nestedRunGuardEnv）を渡し、それがあれば何も実行しない。
 *    このファイルが子プロセスから読み込まれても、検査を入れ子で起動しないため。
 *  - 期待値は expectations.json にまとめている。
 */
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { readFileSync, realpathSync } from 'node:fs';
import path from 'node:path';
import { before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(HERE, '..', '..');
const EXPECT = loadJson(path.join(HERE, 'expectations.json'));
const MAX_BUFFER = 64 * 1024 * 1024;
const TAIL_LINES = 40;
// 利用者の全体設定の除外ファイル（~/.config/git/ignore など）を判定から外すため、空のファイルを指す
const EMPTY_EXCLUDES_FILE = '/dev/null';
// node:test の時間切れは、子プロセスの時間切れより後にする（子プロセスの時間切れの報告を優先する）
const TEST_TIMEOUT_MARGIN_MS = 30000;

function loadJson(filePath) {
  const text = readFileSync(filePath, 'utf8');
  try {
    return JSON.parse(text);
  } catch (error) {
    throw new Error(`JSON として読めません: ${filePath}: ${error.message}`);
  }
}

function repoPath(relativePath) {
  return path.join(REPO_ROOT, relativePath);
}

function readRepoFile(relativePath) {
  return readFileSync(repoPath(relativePath), 'utf8');
}

function testTimeout(childTimeoutMs) {
  return childTimeoutMs + TEST_TIMEOUT_MARGIN_MS;
}

function tail(text) {
  return String(text).replace(/\s+$/, '').split('\n').slice(-TAIL_LINES).join('\n');
}

/** NUL 区切りの出力（git の -z）を配列にする。 */
function splitNul(text) {
  return text
    .split('\0')
    .map((item) => item.replace(/^\n+/, ''))
    .filter((item) => item !== '');
}

/**
 * 子プロセスを起動し、終了を待つ。シェルは介さない（execFile に引数の配列を渡す）。
 * 非 0 の終了・時間切れ・起動の失敗は例外にせず、結果として返す。判定は呼び出し側で明示的に行う。
 * 標準入力はすぐに閉じる（入力を待つ子プロセスが止まらないようにする）。
 */
function runCommand(file, args, { cwd, timeoutMs, env }) {
  if (process.platform === 'win32') {
    throw new Error('Windows では npm・bundle などが .cmd のため、シェルを介さずに起動できません。WSL などの Linux 環境で実行してください。');
  }
  if (!Number.isInteger(timeoutMs) || timeoutMs <= 0) {
    throw new Error(`timeoutMs が正の整数ではありません: ${timeoutMs}`);
  }
  return new Promise((resolve) => {
    let stdinError = null;
    const child = execFile(
      file,
      args,
      {
        cwd,
        env: { ...process.env, ...env, [EXPECT.nestedRunGuardEnv]: '1' },
        timeout: timeoutMs,
        killSignal: 'SIGTERM',
        maxBuffer: MAX_BUFFER,
        windowsHide: true,
        encoding: 'utf8',
      },
      (error, stdout, stderr) => {
        resolve(toResult({ file, args, cwd, timeoutMs, error, stdout, stderr, stdinError }));
      },
    );
    child.stdin.on('error', (error) => {
      stdinError = error;
    });
    child.stdin.end();
  });
}

function toResult({ file, args, cwd, timeoutMs, error, stdout, stderr, stdinError }) {
  const errorCode = error !== null && typeof error.code === 'string' ? error.code : null;
  return {
    command: [file, ...args].join(' '),
    cwd,
    timeoutMs,
    exitCode: error === null ? 0 : Number.isInteger(error.code) ? error.code : null,
    signal: error !== null && error.signal ? error.signal : null,
    timedOut: error !== null && error.killed === true && errorCode === null,
    errorCode,
    stdinErrorCode: stdinError === null ? null : stdinError.code,
    stdout: String(stdout),
    stderr: String(stderr),
  };
}

function describeRun(result) {
  const lines = [
    `コマンド: ${result.command}`,
    `場所: ${path.relative(REPO_ROOT, result.cwd) || '.'}`,
    `終了コード: ${result.exitCode}`,
  ];
  if (result.errorCode !== null) lines.push(`エラー: ${result.errorCode}`);
  if (result.timedOut) lines.push(`時間切れで止めました（${result.timeoutMs} ms）`);
  if (result.signal !== null) lines.push(`シグナル: ${result.signal}`);
  if (result.stdinErrorCode !== null) lines.push(`標準入力のエラー: ${result.stdinErrorCode}`);
  lines.push(`標準出力（末尾 ${TAIL_LINES} 行）:\n${tail(result.stdout)}`);
  lines.push(`標準エラー出力（末尾 ${TAIL_LINES} 行）:\n${tail(result.stderr)}`);
  return lines.join('\n');
}

function assertExitZero(result, label) {
  assert.equal(result.exitCode, 0, `${label} が成功しませんでした。\n${describeRun(result)}`);
}

function parseJsonOutput(result, label) {
  try {
    return JSON.parse(result.stdout);
  } catch (error) {
    throw new Error(`${label} の出力を JSON として読めません: ${error.message}\n${describeRun(result)}`);
  }
}

function runGit(args) {
  return runCommand('git', ['-c', `core.excludesFile=${EMPTY_EXCLUDES_FILE}`, ...args], {
    cwd: REPO_ROOT,
    timeoutMs: EXPECT.timeoutsMs.git,
    env: EXPECT.childEnv.git,
  });
}

async function gitOutput(args, label) {
  const result = await runGit(args);
  assertExitZero(result, label);
  return result.stdout;
}

function runGh(args) {
  return runCommand('gh', args, { cwd: REPO_ROOT, timeoutMs: EXPECT.timeoutsMs.gh, env: EXPECT.childEnv.gh });
}

/** 比較の基点（origin/main と HEAD の分岐点）。 */
async function mergeBase() {
  const stdout = await gitOutput(['merge-base', EXPECT.baseRef, 'HEAD'], `git merge-base ${EXPECT.baseRef} HEAD（git fetch origin が必要な場合があります）`);
  return stdout.trim();
}

/**
 * 除外規則だけで、パスが除外されるかを判定する。
 *  - --no-index：追跡中かどうかに関係なく、規則で判定する（--no-index が無いと、追跡中のファイルは常に「除外されない」になる）。
 *  - 判定は -q（-v なし）の終了コードで行う。-v は否定パターン（!）に当たっても終了コード 0 を返すため、判定には使わない。
 *  - 当たった規則（どのファイルの何行目か）は、-v -n の出力から得る。
 */
async function checkIgnore(relativePath) {
  const verdict = await runGit(['check-ignore', '--no-index', '-q', '--', relativePath]);
  if (verdict.exitCode !== 0 && verdict.exitCode !== 1) {
    throw new Error(`git check-ignore が判定できませんでした。\n${describeRun(verdict)}`);
  }
  const detail = await runGit(['check-ignore', '--no-index', '-v', '-n', '--', relativePath]);
  if (detail.exitCode !== 0 && detail.exitCode !== 1) {
    throw new Error(`git check-ignore -v が失敗しました。\n${describeRun(detail)}`);
  }
  return { ignored: verdict.exitCode === 0, rule: detail.stdout.trim() };
}

/**
 * dotnet test（console のロガー・verbosity=normal）の出力から、テストごとの結果と合計を読む。
 * 1 行 1 件の「  Passed 名前 [時間]」を数え、「Total tests: N」と食い違えば例外にする。
 */
function parseDotnetTestOutput(stdout) {
  const outcomes = new Map();
  for (const match of stdout.matchAll(/^[ \t]+(Passed|Failed|Skipped)[ \t]+(.+?)[ \t]+\[[^\]\n]*\][ \t]*$/gm)) {
    const [, outcome, name] = match;
    if (outcomes.has(name)) {
      throw new Error(`同じテストの結果が 2 回出ています: ${name}`);
    }
    outcomes.set(name, outcome);
  }
  const totalMatch = /^Total tests:[ \t]*(\d+)[ \t]*$/m.exec(stdout);
  if (totalMatch === null) {
    throw new Error('dotnet test の出力に「Total tests:」がありません');
  }
  const total = Number(totalMatch[1]);
  if (total !== outcomes.size) {
    throw new Error(`Total tests（${total}）と、結果の行の数（${outcomes.size}）が一致しません`);
  }
  const counts = { Passed: 0, Failed: 0, Skipped: 0 };
  for (const outcome of outcomes.values()) {
    counts[outcome] += 1;
  }
  return { outcomes, total, passed: counts.Passed, failed: counts.Failed, skipped: counts.Skipped };
}

/** rspec --format json の出力（最後の JSON の行）を読む。 */
function parseRspecJson(stdout) {
  const jsonLines = stdout
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.startsWith('{'));
  if (jsonLines.length === 0) {
    throw new Error('rspec の出力に JSON がありません');
  }
  return JSON.parse(jsonLines[jsonLines.length - 1]);
}

/** npm ci の出力から、追加したパッケージの数と、監査で見つかった脆弱性の数を読む。 */
function parseNpmCiOutput(stdout) {
  const added = /added (\d+) packages?/.exec(stdout);
  if (added === null) {
    throw new Error('npm ci の出力に「added N packages」がありません');
  }
  const audited = /found (\d+) vulnerabilit(?:y|ies)/.exec(stdout);
  return { added: Number(added[1]), vulnerabilities: audited === null ? null : Number(audited[1]) };
}

/** Playwright の JSON レポートから、テストのあるファイルと、テストの件数を数える。 */
function collectPlaywrightSpecs(suites) {
  const files = new Set();
  let tests = 0;
  const walk = (suite) => {
    for (const spec of suite.specs ?? []) {
      files.add(spec.file);
      tests += spec.tests.length;
    }
    for (const child of suite.suites ?? []) {
      walk(child);
    }
  };
  for (const suite of suites) {
    walk(suite);
  }
  return { files: [...files], tests };
}

/**
 * .env.example を読む。空行と # の行を除き、各行が「名前=」（= の右側が空）であるかを調べる。
 * 値そのものは表示しない（万一、秘密の値が書かれていても出力に出さない）。
 */
function parseEnvExample(text) {
  const names = [];
  const problems = [];
  text.split(/\r?\n/).forEach((line, index) => {
    const trimmed = line.trim();
    if (trimmed === '' || trimmed.startsWith('#')) {
      return;
    }
    const match = /^([A-Za-z_][A-Za-z0-9_]*)=(.*)$/.exec(trimmed);
    if (match === null) {
      problems.push(`${index + 1} 行目が「名前=」の形ではありません`);
      return;
    }
    const [, name, value] = match;
    if (value.trim() !== '') {
      problems.push(`${index + 1} 行目の ${name} に値があります（${value.length} 文字）`);
    }
    names.push(name);
  });
  const duplicates = new Set(names.filter((name, index) => names.indexOf(name) !== index));
  for (const name of duplicates) {
    problems.push(`${name} が 2 回以上あります`);
  }
  return { names, problems };
}

/** ワークフローの起動の条件（on）を返す。 */
function workflowTriggers(workflow) {
  if (Object.hasOwn(workflow, 'on')) {
    return workflow.on;
  }
  // Ruby の YAML（YAML 1.1）は、引用符の無いキー on を真偽値 true として読む。JSON にすると "true" になる
  if (Object.hasOwn(workflow, 'true')) {
    return workflow.true;
  }
  throw new Error('ワークフローに on（起動の条件）がありません');
}

/** 機密ファイル（ルートの .gitignore の Secrets 節と同じ考え方）かを、ファイル名で判定する。 */
function isSecretPath(filePath, rules) {
  const name = path.posix.basename(filePath);
  if (rules.allowedNames.includes(name)) return false;
  if (rules.allowedSuffixes.some((suffix) => name.endsWith(suffix))) return false;
  if (rules.names.includes(name)) return true;
  if (rules.prefixes.some((prefix) => name.startsWith(prefix))) return true;
  return rules.suffixes.some((suffix) => name.endsWith(suffix));
}

function registerPreconditionSuite() {
  const ownDir = path.relative(REPO_ROOT, HERE);
  describe('前提：手元の内容が検査の対象と一致している', () => {
    test('リポジトリのルートは、このファイルの 2 つ上のフォルダである', async () => {
      const stdout = await gitOutput(['rev-parse', '--show-toplevel'], 'git rev-parse --show-toplevel');
      assert.equal(realpathSync(stdout.trim()), realpathSync(REPO_ROOT));
    });

    test(`${ownDir} 以外に、HEAD からの変更と未追跡のファイルが無い`, async () => {
      const stdout = await gitOutput(
        ['status', '--porcelain=v1', '-z', '--untracked-files=all', '--', '.', `:(exclude)${ownDir}`],
        'git status',
      );
      const entries = splitNul(stdout);
      assert.deepEqual(entries, [], `HEAD と異なる状態で検査しています:\n${entries.join('\n')}`);
    });
  });
}

function registerCoreSuite() {
  const core = EXPECT.core;
  describe('受け入れ条件 1：Domain Core（dotnet test・xUnit）', () => {
    let run = null;
    before(
      async () => {
        run = await runCommand('dotnet', ['test', core.testProject, '--logger', 'console;verbosity=normal'], {
          cwd: REPO_ROOT,
          timeoutMs: EXPECT.timeoutsMs.dotnetTest,
          env: EXPECT.childEnv.dotnet,
        });
      },
      { timeout: testTimeout(EXPECT.timeoutsMs.dotnetTest) },
    );

    test(`dotnet test ${core.testProject} が成功する（失敗 0 件）`, (t) => {
      assertExitZero(run, 'dotnet test');
      const summary = parseDotnetTestOutput(run.stdout);
      t.diagnostic(`dotnet test：合格 ${summary.passed} 件・失敗 ${summary.failed} 件・スキップ ${summary.skipped} 件・合計 ${summary.total} 件`);
      assert.ok(summary.total > 0, 'テストが 1 件も実行されていません');
      assert.equal(summary.failed, 0, `失敗したテストがあります。\n${describeRun(run)}`);
    });

    test('ToolchainTests（netstandard2.1・LangVersion 9 以下の検査）が合格している', (t) => {
      const summary = parseDotnetTestOutput(run.stdout);
      for (const name of core.requiredPassedTests) {
        const outcome = summary.outcomes.get(name);
        t.diagnostic(`${name}：${outcome === undefined ? '実行されていません' : outcome}`);
        assert.equal(outcome, 'Passed', `${name} が合格していません`);
      }
    });

    test('MSBuild で評価した DomainCore.csproj が netstandard2.1・LangVersion 9 以下である', { timeout: testTimeout(EXPECT.timeoutsMs.dotnetMsbuild) }, async (t) => {
      const result = await runCommand(
        'dotnet',
        ['msbuild', core.project, '-getProperty:TargetFramework', '-getProperty:TargetFrameworks', '-getProperty:LangVersion'],
        { cwd: REPO_ROOT, timeoutMs: EXPECT.timeoutsMs.dotnetMsbuild, env: EXPECT.childEnv.dotnet },
      );
      assertExitZero(result, 'dotnet msbuild -getProperty');
      const properties = parseJsonOutput(result, 'dotnet msbuild -getProperty').Properties;
      t.diagnostic(`TargetFramework=${properties.TargetFramework}・TargetFrameworks=「${properties.TargetFrameworks}」・LangVersion=${properties.LangVersion}`);
      assert.equal(properties.TargetFramework, core.targetFramework);
      assert.equal(properties.TargetFrameworks, '', 'TargetFrameworks（複数の指定）があります');
      assert.match(properties.LangVersion, /^\d+(\.\d+)?$/, `LangVersion が数値ではありません: ${properties.LangVersion}`);
      assert.ok(Number(properties.LangVersion) <= core.maxLangVersion, `LangVersion が ${core.maxLangVersion} を超えています: ${properties.LangVersion}`);
    });
  });
}

function registerBackendSuite() {
  const backend = EXPECT.backend;
  const backendDir = repoPath(backend.dir);
  describe('受け入れ条件 2：保存 API（bundle exec rspec）', () => {
    let run = null;
    before(
      async () => {
        // 件数を確実に読むため、JSON の形式で出力させる（実行する spec は bundle exec rspec と同じ）
        run = await runCommand('bundle', ['exec', 'rspec', '--format', 'json'], {
          cwd: backendDir,
          timeoutMs: EXPECT.timeoutsMs.rspec,
          env: EXPECT.childEnv.rspec,
        });
      },
      { timeout: testTimeout(EXPECT.timeoutsMs.rspec) },
    );

    test(`cd ${backend.dir} && bundle exec rspec が成功する（失敗 0 件）`, (t) => {
      assertExitZero(run, 'bundle exec rspec');
      const report = parseRspecJson(run.stdout);
      const summary = report.summary;
      t.diagnostic(
        `rspec：${report.summary_line}（example ${summary.example_count} 件・失敗 ${summary.failure_count} 件・保留 ${summary.pending_count} 件・example 外のエラー ${summary.errors_outside_of_examples_count} 件）`,
      );
      assert.ok(summary.example_count > 0, 'example が 1 件も実行されていません');
      assert.equal(summary.failure_count, 0);
      assert.equal(summary.errors_outside_of_examples_count, 0);
    });

    test('config.api_only を検査する example が合格している', (t) => {
      const report = parseRspecJson(run.stdout);
      const examples = report.examples.filter((example) => example.full_description.includes(backend.requiredExampleKeyword));
      for (const example of examples) {
        t.diagnostic(`${example.full_description}：${example.status}（${example.file_path}:${example.line_number}）`);
      }
      assert.ok(examples.length > 0, `${backend.requiredExampleKeyword} を検査する example がありません`);
      assert.ok(
        examples.every((example) => example.status === 'passed'),
        `合格していない example があります: ${examples.map((example) => example.status).join(', ')}`,
      );
    });
  });

  describe('受け入れ条件 8：dotenv-rails は development・test だけで読み込まれる', () => {
    test(`Gemfile で ${backend.dotenvGem} が development・test グループだけにある`, { timeout: testTimeout(EXPECT.timeoutsMs.bundleRuby) }, async (t) => {
      const script =
        'dep = Bundler.definition.dependencies.find { |d| d.name == ARGV[0] }; ' +
        'abort("#{ARGV[0]} が Gemfile にありません") if dep.nil?; ' +
        'print JSON.generate(dep.groups.map(&:to_s).sort)';
      const result = await runCommand('bundle', ['exec', 'ruby', '-rjson', '-e', script, '--', backend.dotenvGem], {
        cwd: backendDir,
        timeoutMs: EXPECT.timeoutsMs.bundleRuby,
        env: {},
      });
      assertExitZero(result, 'bundle exec ruby（Bundler のグループの取得）');
      const groups = parseJsonOutput(result, 'bundle exec ruby（Bundler のグループの取得）');
      t.diagnostic(`${backend.dotenvGem} のグループ：${groups.join('・')}`);
      assert.deepEqual(groups, [...backend.dotenvGroups].sort());
    });

    for (const expected of backend.dotenvByRailsEnv) {
      const verb = expected.loaded ? '読み込まれる' : '読み込まれない';
      test(`RAILS_ENV=${expected.railsEnv} では Dotenv が${verb}`, { timeout: testTimeout(EXPECT.timeoutsMs.railsRunner) }, async (t) => {
        // 起動時の警告が標準出力に混ざることがあるため、判定は目印の付いた行で行う
        const script = 'puts "PR13_DOTENV_DEFINED=#{defined?(::Dotenv) ? "yes" : "no"}"';
        const result = await runCommand('ruby', ['bin/rails', 'runner', script], {
          cwd: backendDir,
          timeoutMs: EXPECT.timeoutsMs.railsRunner,
          env: { ...expected.extraEnv, RAILS_ENV: expected.railsEnv },
        });
        assertExitZero(result, `bin/rails runner（RAILS_ENV=${expected.railsEnv}）`);
        const marks = [...result.stdout.matchAll(/^PR13_DOTENV_DEFINED=(yes|no)$/gm)].map((match) => match[1]);
        assert.equal(marks.length, 1, `判定の行がちょうど 1 行ではありません。\n${describeRun(result)}`);
        const loaded = marks[0] === 'yes';
        const state = (value) => (value ? '読み込まれる' : '読み込まれない');
        t.diagnostic(`RAILS_ENV=${expected.railsEnv}：Dotenv ${loaded ? 'が読み込まれています' : 'は読み込まれていません'}`);
        assert.equal(loaded, expected.loaded, `RAILS_ENV=${expected.railsEnv} で Dotenv が${state(loaded)}（期待は${state(expected.loaded)}）`);
      });
    }
  });
}

function registerE2eSuite() {
  const e2e = EXPECT.e2e;
  const e2eDir = repoPath(e2e.dir);
  const playwrightDir = path.join(e2eDir, 'node_modules', ...e2e.playwrightPackage.split('/'));
  const ownDirFromE2e = path.relative(e2eDir, HERE);
  describe('受け入れ条件 3：E2E の準備（test/ で npm ci・Playwright）', () => {
    test(`${e2e.dir}/ で npm ci が成功する`, { timeout: testTimeout(EXPECT.timeoutsMs.npmCi) }, async (t) => {
      const result = await runCommand('npm', ['ci'], { cwd: e2eDir, timeoutMs: EXPECT.timeoutsMs.npmCi, env: EXPECT.childEnv.npm });
      assertExitZero(result, 'npm ci');
      const summary = parseNpmCiOutput(result.stdout);
      const vulnerabilities = summary.vulnerabilities === null ? '監査の結果なし' : `脆弱性 ${summary.vulnerabilities} 件`;
      t.diagnostic(`npm ci：${summary.added} パッケージを追加・${vulnerabilities}`);
    });

    test(`${e2e.playwrightPackage} は package.json で固定した版が入っている`, (t) => {
      const manifest = loadJson(path.join(e2eDir, 'package.json'));
      const lock = loadJson(path.join(e2eDir, 'package-lock.json'));
      const installed = loadJson(path.join(playwrightDir, 'package.json'));
      const pinned = manifest.devDependencies[e2e.playwrightPackage];
      const locked = lock.packages[`node_modules/${e2e.playwrightPackage}`].version;
      t.diagnostic(`package.json：${pinned}・package-lock.json：${locked}・node_modules：${installed.version}`);
      assert.match(pinned, /^\d+\.\d+\.\d+$/, `版が完全一致の指定ではありません: ${pinned}`);
      assert.equal(locked, pinned);
      assert.equal(installed.version, pinned);
    });

    test(`Playwright が設定を読み込め、${ownDirFromE2e} のファイルをテストとして拾わない`, { timeout: testTimeout(EXPECT.timeoutsMs.playwrightList) }, async (t) => {
      const cli = path.join(playwrightDir, loadJson(path.join(playwrightDir, 'package.json')).bin[e2e.playwrightBin]);
      const result = await runCommand(process.execPath, [cli, 'test', '--list', '--pass-with-no-tests', '--reporter=json'], {
        cwd: e2eDir,
        timeoutMs: EXPECT.timeoutsMs.playwrightList,
        env: {},
      });
      assertExitZero(result, 'playwright test --list');
      const report = parseJsonOutput(result, 'playwright test --list');
      const { files, tests } = collectPlaywrightSpecs(report.suites);
      for (const project of report.config.projects) {
        t.diagnostic(`project ${project.name}：testDir=${path.relative(REPO_ROOT, project.testDir)}・testMatch=${JSON.stringify(project.testMatch)}`);
      }
      t.diagnostic(`Playwright のテスト：${tests} 件（${files.length} ファイル）`);
      assert.deepEqual(report.errors, [], 'Playwright がエラーを報告しています');
      const picked = files.filter((file) => file.split(path.sep).join('/').startsWith(`${ownDirFromE2e}/`));
      assert.deepEqual(picked, [], `${ownDirFromE2e} のファイルが Playwright のテストとして拾われています`);
    });
  });
}

function registerCiSuite() {
  const ci = EXPECT.ci;
  const pr = String(EXPECT.pullRequest);
  describe(`受け入れ条件 4：CI（${ci.workflowFile} と GitHub の結果）`, () => {
    let workflow = null;
    before(
      async () => {
        const script = 'print JSON.generate(YAML.safe_load(File.read(ARGV[0]), aliases: false))';
        const result = await runCommand('ruby', ['-ryaml', '-rjson', '-e', script, '--', repoPath(ci.workflowFile)], {
          cwd: REPO_ROOT,
          timeoutMs: EXPECT.timeoutsMs.rubyYaml,
          env: {},
        });
        assertExitZero(result, 'ruby（ワークフローの YAML の読み込み）');
        workflow = parseJsonOutput(result, 'ruby（ワークフローの YAML の読み込み）');
      },
      { timeout: testTimeout(EXPECT.timeoutsMs.rubyYaml) },
    );

    test(`pull_request と ${ci.pushBranch} への push で動く`, (t) => {
      const triggers = workflowTriggers(workflow);
      t.diagnostic(`on：${JSON.stringify(triggers)}`);
      assert.ok(Object.hasOwn(triggers, 'pull_request'), 'pull_request がありません');
      assert.ok(Object.hasOwn(triggers, 'push') && triggers.push !== null, 'push（ブランチの指定つき）がありません');
      assert.ok(
        Array.isArray(triggers.push.branches) && triggers.push.branches.includes(ci.pushBranch),
        `push の branches に ${ci.pushBranch} がありません`,
      );
    });

    test('permissions は contents: read だけで、ジョブ単位でも権限を広げていない', (t) => {
      t.diagnostic(`permissions：${JSON.stringify(workflow.permissions)}`);
      assert.deepEqual(workflow.permissions, ci.permissions);
      for (const [name, job] of Object.entries(workflow.jobs)) {
        if (!Object.hasOwn(job, 'permissions')) {
          continue;
        }
        t.diagnostic(`${name} の permissions：${JSON.stringify(job.permissions)}`);
        assert.equal(typeof job.permissions, 'object', `${name} の permissions が一括の指定（${job.permissions}）です`);
        for (const [scope, value] of Object.entries(job.permissions)) {
          assert.ok(ci.allowedJobPermissionValues.includes(value), `${name} の permissions.${scope} が ${value} です`);
        }
      }
    });

    test(`${ci.requiredJobs.join('・')} のジョブがあり、全ジョブに timeout-minutes がある`, (t) => {
      const jobs = Object.entries(workflow.jobs);
      for (const [name, job] of jobs) {
        t.diagnostic(`${name}：timeout-minutes=${job['timeout-minutes']}`);
      }
      for (const required of ci.requiredJobs) {
        assert.ok(Object.hasOwn(workflow.jobs, required), `${required} のジョブがありません`);
      }
      for (const [name, job] of jobs) {
        const minutes = job['timeout-minutes'];
        assert.ok(Number.isInteger(minutes) && minutes > 0, `${name} に timeout-minutes（正の整数）がありません: ${minutes}`);
      }
    });

    test(`PR #${pr} の先頭コミットが、手元の HEAD と一致する`, { timeout: testTimeout(EXPECT.timeoutsMs.gh) }, async (t) => {
      const view = await runGh(['pr', 'view', pr, '--json', 'headRefOid,headRefName']);
      assertExitZero(view, 'gh pr view');
      const { headRefOid, headRefName } = parseJsonOutput(view, 'gh pr view');
      const head = (await gitOutput(['rev-parse', 'HEAD'], 'git rev-parse HEAD')).trim();
      t.diagnostic(`PR の先頭：${headRefName} ${headRefOid}・手元の HEAD：${head}`);
      assert.equal(headRefOid, head, '手元の HEAD と PR の先頭コミットが異なります（push の前か、別のブランチです）');
    });

    test(`gh pr checks ${pr} で ${ci.requiredJobs.join('・')} が pass である`, { timeout: testTimeout(EXPECT.timeoutsMs.gh) }, async (t) => {
      const result = await runGh(['pr', 'checks', pr, '--json', 'name,state,bucket,workflow,event,link']);
      // gh pr checks は、失敗があると 1、実行中があると 8 で終わる。どちらも JSON を出すため、判定は内容で行う
      assert.ok([0, 1, 8].includes(result.exitCode), `gh pr checks が失敗しました。\n${describeRun(result)}`);
      const checks = parseJsonOutput(result, 'gh pr checks');
      for (const check of checks) {
        t.diagnostic(`${check.workflow} / ${check.name}：${check.bucket}（${check.state}・${check.event}）${check.link}`);
      }
      for (const job of ci.requiredJobs) {
        const entries = checks.filter((check) => check.workflow === ci.workflowName && check.name === job);
        assert.ok(entries.length > 0, `${ci.workflowName} / ${job} のチェックがありません`);
        assert.ok(
          entries.every((check) => check.bucket === 'pass'),
          `${ci.workflowName} / ${job} が pass ではありません: ${entries.map((check) => check.bucket).join(', ')}`,
        );
      }
    });
  });
}

function registerGitignoreSuite() {
  const gitignore = EXPECT.gitignore;
  describe('受け入れ条件 5：.gitignore（git check-ignore --no-index で規則を判定）', () => {
    for (const group of gitignore.groups) {
      describe(group.title, () => {
        for (const expected of group.cases) {
          test(`${expected.path} は${expected.ignored ? '除外される' : '除外されない'}`, async (t) => {
            const { ignored, rule } = await checkIgnore(expected.path);
            t.diagnostic(`当たった規則：${rule}`);
            assert.equal(ignored, expected.ignored, `${expected.path} の判定が期待と異なります（${rule}）`);
          });
        }
      });
    }

    test('追跡中のファイルは、どれも除外規則に当たらない（bin スタブ・.keep・csproj などが漏れずに追跡される）', async (t) => {
      const tracked = splitNul(await gitOutput(['ls-files', '-z'], 'git ls-files'));
      const result = await runGit(['check-ignore', '--no-index', '--', ...tracked]);
      assert.ok([0, 1].includes(result.exitCode), `git check-ignore が失敗しました。\n${describeRun(result)}`);
      const ignored = result.stdout.split('\n').filter((line) => line !== '');
      t.diagnostic(`追跡中のファイル ${tracked.length} 件を検査しました`);
      assert.deepEqual(ignored, [], '除外規則に当たる追跡中のファイルがあります');
    });

    test(`既存の行を変えていない（${EXPECT.baseRef} との分岐点からの差分に、削除の行が無い）`, async (t) => {
      const base = await mergeBase();
      const stdout = await gitOutput(['diff', '--numstat', '--no-ext-diff', base, 'HEAD', '--', gitignore.file], 'git diff --numstat');
      const line = stdout.trim();
      if (line === '') {
        t.diagnostic(`${base.slice(0, 7)} から変更はありません`);
        return;
      }
      const match = /^(\d+)\t(\d+)\t/.exec(line);
      assert.notEqual(match, null, `git diff --numstat の出力を読めません: ${line}`);
      t.diagnostic(`${base.slice(0, 7)} から：追加 ${match[1]} 行・削除 ${match[2]} 行`);
      assert.equal(Number(match[2]), 0, '既存の行が削除または変更されています');
    });
  });
}

function registerEnvExampleSuite() {
  const envExample = EXPECT.envExample;
  describe(`受け入れ条件 6：${envExample.file} は名前だけ`, () => {
    let parsed = null;
    before(() => {
      parsed = parseEnvExample(readRepoFile(envExample.file));
    });

    test('値を書いていない（= の右側が空）', (t) => {
      t.diagnostic(`名前：${parsed.names.join('・')}`);
      assert.ok(parsed.names.length > 0, '名前が 1 つもありません');
      assert.deepEqual(parsed.problems, []);
    });

    test(`${envExample.requiredNames.join('・')} がある`, () => {
      const missing = envExample.requiredNames.filter((name) => !parsed.names.includes(name));
      assert.deepEqual(missing, [], `足りない名前があります: ${missing.join('・')}`);
    });
  });
}

function registerSecretsSuite() {
  describe('受け入れ条件 7：機密ファイルを git に入れていない', () => {
    test('追跡中のファイルに、機密ファイル（.env・*.key・*.pem など）が無い', async (t) => {
      const tracked = splitNul(await gitOutput(['ls-files', '-z'], 'git ls-files'));
      const secrets = tracked.filter((file) => isSecretPath(file, EXPECT.secretFiles));
      t.diagnostic(`追跡中のファイル ${tracked.length} 件を検査しました`);
      assert.deepEqual(secrets, []);
    });

    test(`PR のどのコミットにも、機密ファイルが含まれていない（${EXPECT.baseRef} との分岐点からのコミット）`, async (t) => {
      const base = await mergeBase();
      const commits = (await gitOutput(['rev-list', `${base}..HEAD`], 'git rev-list')).split('\n').filter((line) => line !== '');
      const touched = new Set(splitNul(await gitOutput(['log', '--format=', '--name-only', '--no-renames', '-z', `${base}..HEAD`], 'git log')));
      const secrets = [...touched].filter((file) => isSecretPath(file, EXPECT.secretFiles));
      t.diagnostic(`コミット ${commits.length} 件・変更されたパス ${touched.size} 件を検査しました`);
      assert.ok(commits.length > 0, `${EXPECT.baseRef} との分岐点から HEAD までにコミットがありません`);
      assert.deepEqual(secrets, []);
    });
  });
}

function registerReadmeSuite() {
  const readme = EXPECT.readme;
  describe('受け入れ条件 9：README に開発の始め方がある', () => {
    test('各テストの実行コマンドが書かれている', (t) => {
      const text = readRepoFile(readme.file);
      t.diagnostic(`検査したコマンド：${readme.requiredCommands.join('／')}`);
      const missing = readme.requiredCommands.filter((command) => !text.includes(command));
      assert.deepEqual(missing, [], `書かれていないコマンドがあります: ${missing.join('／')}`);
    });
  });
}

function registerSelfCheckSuite() {
  describe('検査の仕組みの確認（判定が素通りにならないこと）', () => {
    test('.env.example の判定は、値のある行・形の違う行・重複を見つける', () => {
      const parsed = parseEnvExample('# 説明\nAPP_ENV=production\nSECRET_KEY_BASE=\nSECRET_KEY_BASE=\nnot a pair\n');
      assert.deepEqual(parsed.names, ['APP_ENV', 'SECRET_KEY_BASE', 'SECRET_KEY_BASE']);
      assert.equal(parsed.problems.length, 3);
    });

    test('dotnet test の読み取りは、失敗を数え、合計との食い違いと合計の欠落を例外にする', () => {
      const output = ['  Passed A.B.C [1 ms]', '  Failed A.B.D [2 ms]', '  Error Message:', '   x', 'Test Run Failed.', 'Total tests: 2', '     Passed: 1', '     Failed: 1'].join('\n');
      const summary = parseDotnetTestOutput(output);
      assert.equal(summary.passed, 1);
      assert.equal(summary.failed, 1);
      assert.throws(() => parseDotnetTestOutput(output.replace('Total tests: 2', 'Total tests: 3')));
      assert.throws(() => parseDotnetTestOutput('  Passed A.B.C [1 ms]'));
    });

    test('機密ファイルの判定は、master.key・.env・*.pem を見つけ、.env.example・*.pub.key・credentials.yml.enc を除く', () => {
      const rules = EXPECT.secretFiles;
      assert.equal(isSecretPath('src/backend/config/master.key', rules), true);
      assert.equal(isSecretPath('.env', rules), true);
      assert.equal(isSecretPath('src/backend/.env.production', rules), true);
      assert.equal(isSecretPath('certs/server.pem', rules), true);
      assert.equal(isSecretPath('.env.example', rules), false);
      assert.equal(isSecretPath('keys/deploy.pub.key', rules), false);
      assert.equal(isSecretPath('src/backend/config/credentials.yml.enc', rules), false);
    });

    test('ワークフローの on は、YAML 1.1 で true に変わったキーからも読み、無ければ例外にする', () => {
      assert.deepEqual(workflowTriggers({ on: { push: null } }), { push: null });
      assert.deepEqual(workflowTriggers({ true: { pull_request: null } }), { pull_request: null });
      assert.throws(() => workflowTriggers({ name: 'CI' }));
    });
  });
}

function registerAllSuites() {
  registerPreconditionSuite();
  registerCoreSuite();
  registerBackendSuite();
  registerE2eSuite();
  registerCiSuite();
  registerGitignoreSuite();
  registerEnvExampleSuite();
  registerSecretsSuite();
  registerReadmeSuite();
  registerSelfCheckSuite();
}

if (process.env[EXPECT.nestedRunGuardEnv] === '1') {
  test('入れ子の起動を検知したため、検査を実行しません', { skip: true }, () => {});
} else {
  registerAllSuites();
}
