/**
 * PR #14（Issue #2 Domain Core：配分の検証・自動調整・乱数・マスタ定数）の受け入れ条件を、実装を実際に呼び出して検査する。
 *
 * 実行（リポジトリのルートで）:
 *   node --test --test-reporter=spec test/pr14/acceptance.check.mjs
 *   （Node.js 22 の node --test にフォルダ（test/pr14/）を渡すと、モジュールとして読み込もうとして失敗する。
 *     ファイルか、引用符で囲んだグロブ 'test/pr14/*.check.mjs' を渡す）
 *
 * 前提:
 *  - Linux・macOS（WSL を含む）。.NET 8 SDK、Node.js 20 以上、gh（認証済み）。
 *  - origin/main を取得済み（git fetch origin）。
 *  - Playwright の一覧の検査は test/node_modules を使う（test/ で npm ci 済みであること）。
 *  - Domain Core は純粋な C# のライブラリで、開発サーバーは無い。検査の対象は手元の作業ツリー（PR #14 の先頭コミット）。
 *
 * 実行すると起きること:
 *  - dotnet test を 2 回（PR のテスト src/core/DomainCore.Tests と、テスターの受け入れテスト test/pr14/DomainCore.AcceptanceTests）、
 *    dotnet build -c Release --no-incremental を 1 回実行する。除外対象のビルド成果物（bin・obj）を書く。
 *  - gh で、PR の先頭コミットと CI の結果を GitHub に問い合わせる。CI が実行中なら、終わるまで（上限 expectations.json の ci.waitLimitMs）待つ。
 *
 * 受け入れ条件（Issue #2）と検査の対応:
 *  1〜4・8：dotnet test（PR のテストと受け入れテスト）。受け入れテストは条件から書いた別のテストで、実装をプロジェクト参照で呼ぶ。
 *  3：加えて、ソースの字句に System.Random が無い。SplitMix64 の既知の値を、このファイルの BigInt の計算で確かめる。
 *  5：Config 以外の .cs に文字列リテラル（日本語を含む）・文字リテラルが無い（このファイルの字句解析）。PR に検査のテストがあり、合格している。
 *  6：float・double・decimal の型名と実数リテラルが無い（コメントを除く）。公開の型の形は受け入れテスト、IL は PR のテストが調べる。
 *  7：Config の各ファイルの先頭行に、数値がすべて架空のモデル値である旨のコメントがある（ファイルを読んで確かめる）。
 *  9：PR の変更が src/core/DomainCore と src/core/DomainCore.Tests の新規ファイル（とこのフォルダ）だけで、src/ の他の層にマスタのコード・名前が無い。
 *  その他：Release ビルドの成功と警告の件数・CI（core・backend）が pass・このフォルダのファイルを Playwright が拾わない。
 *
 * 作りの決まり:
 *  - 子プロセスは execFile に引数の配列を渡して起動する（シェルを介さない）。すべてに timeout を付ける。
 *  - ファイル名は Playwright の testMatch（*.spec.* / *.test.*）に当たらない形にしている。
 *  - 子プロセスにだけ環境変数（expectations.json の nestedRunGuardEnv）を渡し、それがあれば何も実行しない。
 *  - 期待値は expectations.json と splitmix64-vectors.json にまとめている。
 */
import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { existsSync, readdirSync, readFileSync, realpathSync, statSync } from 'node:fs';
import path from 'node:path';
import { before, describe, test } from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
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
// ファイルの時刻の精度が粗いファイルシステムでも、ビルドの開始より後に書かれたと判定できるようにする余裕
const MTIME_TOLERANCE_MS = 2000;

// SplitMix64（S. Vigna の splitmix64.c、Java の java.util.SplittableRandom と同じ）。実装とは別に、BigInt で 2^64 を法として計算する
const SPLITMIX64 = Object.freeze({
  gamma: 0x9e3779b97f4a7c15n,
  firstMultiplier: 0xbf58476d1ce4e5b9n,
  secondMultiplier: 0x94d049bb133111ebn,
  firstShift: 30n,
  secondShift: 27n,
  finalShift: 31n,
  wordBits: 64,
});

// 日本語の文字：ひらがな・カタカナ・漢字・全角の記号（、。「」など）・長音・全角英数・半角カタカナ
const JAPANESE_CHARACTER = /[\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Han}\u3000-\u303F\u30FC\uFF01-\uFF60\uFF61-\uFF9F]/u;

// C# の識別子（逐語識別子 @x と、\uXXXX のエスケープを含む）
const IDENTIFIER_SOURCE = String.raw`@?(?:[\p{L}\p{Nl}_]|\\u[0-9A-Fa-f]{4}|\\U[0-9A-Fa-f]{8})(?:[\p{L}\p{Nl}\p{Nd}\p{Mn}\p{Mc}\p{Pc}\p{Cf}]|\\u[0-9A-Fa-f]{4}|\\U[0-9A-Fa-f]{8})*`;

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
    throw new Error('Windows では dotnet・gh などの起動の形が異なるため、WSL などの Linux 環境で実行してください。');
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

function runDotnet(args, timeoutMs) {
  return runCommand('dotnet', args, { cwd: REPO_ROOT, timeoutMs, env: EXPECT.childEnv.dotnet });
}

/** 比較の基点（origin/main と HEAD の分岐点）。 */
async function mergeBase() {
  const stdout = await gitOutput(['merge-base', EXPECT.baseRef, 'HEAD'], `git merge-base ${EXPECT.baseRef} HEAD（git fetch origin が必要な場合があります）`);
  return stdout.trim();
}

/**
 * 除外規則だけで、パスが除外されるかを判定する（PR #13 の検査と同じ方法）。
 *  - --no-index：追跡中かどうかに関係なく、規則で判定する。
 *  - 判定は -q（-v なし）の終了コードで行う。当たった規則は -v -n の出力から得る。
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

/** テストの名前（名前空間.クラス.メソッド(引数)）から、prefix の後ろのクラス名を取り出す。prefix で始まらなければ null。 */
function testClassName(name, prefix) {
  if (!name.startsWith(prefix)) {
    return null;
  }
  const rest = name.slice(prefix.length);
  const dot = rest.indexOf('.');
  return dot < 0 ? null : rest.slice(0, dot);
}

/** クラスごとの結果の数（Passed・Failed・Skipped）を数える。 */
function outcomesByClass(outcomes, prefix) {
  const byClass = new Map();
  for (const [name, outcome] of outcomes) {
    const className = testClassName(name, prefix);
    if (className === null) {
      throw new Error(`名前空間が ${prefix} でないテストがあります: ${name}`);
    }
    const entry = byClass.get(className) ?? { Passed: 0, Failed: 0, Skipped: 0 };
    entry[outcome] += 1;
    byClass.set(className, entry);
  }
  return byClass;
}

/** dotnet build の出力の要約（成否・警告と誤りの件数・警告の行）を読む。件数の行が無ければ例外にする。 */
function parseBuildSummary(stdout) {
  const warnings = /^[ \t]*(\d+) Warning\(s\)[ \t]*$/m.exec(stdout);
  const errors = /^[ \t]*(\d+) Error\(s\)[ \t]*$/m.exec(stdout);
  if (warnings === null || errors === null) {
    throw new Error('dotnet build の出力に「N Warning(s)」「N Error(s)」がありません');
  }
  const warningLines = [...new Set(stdout.split('\n').map((line) => line.trim()).filter((line) => /: warning [A-Z]+\d+:/.test(line)))];
  return {
    succeeded: /^Build succeeded\.[ \t]*$/m.test(stdout),
    failed: /^Build FAILED\.[ \t]*$/m.test(stdout),
    warnings: Number(warnings[1]),
    errors: Number(errors[1]),
    warningLines,
  };
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

/** gh pr checks --json の結果を読む。チェックがまだ登録されていない（no checks reported）ときは null。 */
function parseChecks(result) {
  // gh pr checks は、失敗があると 1、実行中があると 8 で終わる。どちらも JSON を出すため、判定は内容で行う
  if (![0, 1, 8].includes(result.exitCode)) {
    throw new Error(`gh pr checks が失敗しました。\n${describeRun(result)}`);
  }
  if (result.stdout.trim() === '') {
    if (/no checks reported/i.test(result.stderr)) {
      return null;
    }
    throw new Error(`gh pr checks の出力が空です。\n${describeRun(result)}`);
  }
  return parseJsonOutput(result, 'gh pr checks');
}

/** 必要なジョブのチェックが、どれもそろって実行中でない（結果が出た）か。 */
function requiredChecksSettled(checks, ci) {
  return ci.requiredJobs.every((job) => {
    const entries = checks.filter((check) => check.workflow === ci.workflowName && check.name === job);
    return entries.length > 0 && entries.every((check) => check.bucket !== 'pending');
  });
}

/** CI の必要なジョブの結果が出るまで、一定の間隔で問い合わせる。上限を過ぎたら、最後の状態を返す。 */
async function waitForRequiredChecks() {
  const ci = EXPECT.ci;
  const deadline = Date.now() + ci.waitLimitMs;
  let polls = 0;
  for (;;) {
    polls += 1;
    const result = await runGh(['pr', 'checks', String(EXPECT.pullRequest), '--json', 'name,state,bucket,workflow,event,link,completedAt']);
    const checks = parseChecks(result);
    if (checks !== null && requiredChecksSettled(checks, ci)) {
      return { checks, polls, timedOut: false };
    }
    if (Date.now() + ci.pollIntervalMs > deadline) {
      return { checks, polls, timedOut: true };
    }
    await delay(ci.pollIntervalMs);
  }
}

/** git diff --name-status -z の出力を、状態とパスの組にする（--no-renames で使う）。 */
function parseNameStatus(stdout) {
  const fields = stdout.split('\0').filter((field) => field !== '');
  if (fields.length % 2 !== 0) {
    throw new Error('git diff --name-status の出力を、状態とパスの組に分けられません');
  }
  const entries = [];
  for (let i = 0; i < fields.length; i += 2) {
    entries.push({ status: fields[i], path: fields[i + 1] });
  }
  return entries;
}

/** root の下の、拡張子 extension のファイルを、root からの相対パス（/ 区切り）の順に返す。root の直下の excluded のフォルダは除く。 */
function listFiles(root, extension, excludedTopLevelDirNames) {
  const found = [];
  const walk = (relativeDir) => {
    const entries = readdirSync(path.join(root, relativeDir), { withFileTypes: true });
    for (const entry of entries) {
      const relative = relativeDir === '' ? entry.name : `${relativeDir}/${entry.name}`;
      if (entry.isDirectory()) {
        if (!(relativeDir === '' && excludedTopLevelDirNames.includes(entry.name))) {
          walk(relative);
        }
      } else if (entry.isFile() && entry.name.endsWith(extension)) {
        found.push(relative);
      } else if (!entry.isFile() && !entry.isDirectory() && entry.name.endsWith(extension)) {
        throw new Error(`通常のファイルでない .cs があります（シンボリックリンクなど）: ${relative}`);
      }
    }
  };
  walk('');
  return found.sort();
}

function containsJapanese(text) {
  return JAPANESE_CHARACTER.test(text);
}

/** C# の識別子の文字列から、逐語識別子の @ を除き、\uXXXX のエスケープを文字に戻す。 */
function decodeIdentifier(text) {
  const body = text.startsWith('@') ? text.slice(1) : text;
  return body.replace(/\\u([0-9A-Fa-f]{4})|\\U([0-9A-Fa-f]{8})/g, (_, four, eight) => String.fromCodePoint(Number.parseInt(four ?? eight, 16)));
}

function isDigit(c) {
  return c >= '0' && c <= '9';
}

/**
 * ソースの検査に使う、C# の最小限の字句解析器（PR のテストの字句解析器とは別に書いたもの）。
 * - コメント（// と /* *\/）と前処理命令（行頭の #）を、それぞれ comment・preprocessor の字句として取り出す（検査の対象からは外す）。
 * - 文字列は通常（"…"）・逐語（@"…"）・補間（$"…"・$@"…"・@$"…"）を string、文字リテラルを char として取り出す。
 *   content には中身を入れる（通常の文字列はエスケープを文字に戻す）。補間の式の部分はコードとして字句に分ける。
 * - 数値は integer と real（小数点・指数・接尾辞 f・d・m のあるもの）に分ける。16 進数・2 進数・桁区切り（_）・範囲（..）を扱う。
 * - 識別子は identifier（name は @ とエスケープを戻した名前）。記号は読み飛ばす。
 * - 対象は C# 9（Domain Core の LangVersion）。生文字列リテラル（C# 11）は扱わない。
 * - 解釈できない入力（閉じていない文字列・コメント・補間の式、知らないエスケープ）は例外にする（検査を素通りさせない）。
 */
class CSharpLexer {
  constructor(source) {
    this.source = source;
    this.index = 0;
    this.line = 1;
    this.atLineStart = true;
    this.tokens = [];
    this.identifierPattern = new RegExp(IDENTIFIER_SOURCE, 'uy');
  }

  static tokenize(source) {
    const lexer = new CSharpLexer(source);
    lexer.scanCode(false);
    return lexer.tokens;
  }

  get atEnd() {
    return this.index >= this.source.length;
  }

  peek(offset = 0) {
    return this.source[this.index + offset] ?? '';
  }

  advance(count = 1) {
    for (let k = 0; k < count; k += 1) {
      const c = this.source[this.index];
      if (c === '\n') {
        this.line += 1;
        this.atLineStart = true;
      } else if (!/\s/.test(c)) {
        this.atLineStart = false;
      }
      this.index += 1;
    }
  }

  push(kind, start, startLine, extra = {}) {
    this.tokens.push({ kind, text: this.source.slice(start, this.index), line: startLine, ...extra });
  }

  unterminated(what, startLine) {
    return new Error(`${what}が閉じていません（${startLine} 行目）`);
  }

  /** コードを読む。補間の式の中（insideHole）では、深さ 0 の } か :（:: を除く）の手前で戻る。 */
  scanCode(insideHole) {
    let depth = 0;
    const startLine = this.line;
    while (!this.atEnd) {
      const c = this.peek();
      if (insideHole && depth === 0 && (c === '}' || (c === ':' && this.peek(1) !== ':'))) {
        return;
      }
      if (c === '#' && this.atLineStart) {
        this.skipLine('preprocessor');
      } else if (c === '/' && this.peek(1) === '/') {
        this.skipLine('comment');
      } else if (c === '/' && this.peek(1) === '*') {
        this.skipBlockComment();
      } else if (c === '"') {
        this.scanRegularString();
      } else if (c === '@' && this.peek(1) === '"') {
        this.scanVerbatimString();
      } else if (c === '$' && this.peek(1) === '"') {
        this.scanInterpolatedString(1, false);
      } else if (((c === '$' && this.peek(1) === '@') || (c === '@' && this.peek(1) === '$')) && this.peek(2) === '"') {
        this.scanInterpolatedString(2, true);
      } else if (c === "'") {
        this.scanCharLiteral();
      } else if ((c === '.' && this.peek(1) === '.') || (c === ':' && this.peek(1) === ':')) {
        // 範囲演算子（1..5）の 2 つ目の . を実数（.5）の始まりと読まない。:: は名前の修飾で、補間の書式の始まりではない
        this.advance(2);
      } else if (isDigit(c) || (c === '.' && isDigit(this.peek(1)))) {
        this.scanNumber();
      } else if (this.matchIdentifier() !== null) {
        this.scanIdentifier();
      } else {
        if ('([{'.includes(c)) {
          depth += 1;
        } else if (')]}'.includes(c) && depth > 0) {
          depth -= 1;
        }
        this.advance();
      }
    }
    if (insideHole) {
      throw this.unterminated('補間の式', startLine);
    }
  }

  skipLine(kind) {
    const start = this.index;
    const startLine = this.line;
    while (!this.atEnd && this.peek() !== '\n') {
      this.advance();
    }
    this.push(kind, start, startLine);
  }

  skipBlockComment() {
    const start = this.index;
    const startLine = this.line;
    this.advance(2);
    while (!(this.peek() === '*' && this.peek(1) === '/')) {
      if (this.atEnd) {
        throw this.unterminated('コメント', startLine);
      }
      this.advance();
    }
    this.advance(2);
    this.push('comment', start, startLine);
  }

  /** \ から始まるエスケープを読み、表す文字を返す。 */
  readEscape(startLine) {
    this.advance();
    const c = this.peek();
    const simple = { "'": "'", '"': '"', '\\': '\\', 0: '\0', a: '\x07', b: '\b', f: '\f', n: '\n', r: '\r', t: '\t', v: '\v' };
    if (c !== '' && Object.hasOwn(simple, c)) {
      this.advance();
      return simple[c];
    }
    if (c === 'u' || c === 'U') {
      const length = c === 'u' ? 4 : 8;
      const hex = this.source.slice(this.index + 1, this.index + 1 + length);
      if (!new RegExp(`^[0-9A-Fa-f]{${length}}$`).test(hex)) {
        throw new Error(`解釈できない \\${c} のエスケープです（${startLine} 行目）`);
      }
      this.advance(1 + length);
      return String.fromCodePoint(Number.parseInt(hex, 16));
    }
    if (c === 'x') {
      this.advance();
      let hex = '';
      while (hex.length < 4 && /[0-9A-Fa-f]/.test(this.peek())) {
        hex += this.peek();
        this.advance();
      }
      if (hex === '') {
        throw new Error(`解釈できない \\x のエスケープです（${startLine} 行目）`);
      }
      return String.fromCharCode(Number.parseInt(hex, 16));
    }
    throw new Error(`解釈できないエスケープ \\${c} です（${startLine} 行目）`);
  }

  scanRegularString() {
    const start = this.index;
    const startLine = this.line;
    this.advance();
    let content = '';
    for (;;) {
      if (this.atEnd || this.peek() === '\n') {
        throw this.unterminated('文字列', startLine);
      }
      const c = this.peek();
      if (c === '"') {
        this.advance();
        break;
      }
      if (c === '\\') {
        content += this.readEscape(startLine);
      } else {
        content += c;
        this.advance();
      }
    }
    this.push('string', start, startLine, { content });
  }

  scanVerbatimString() {
    const start = this.index;
    const startLine = this.line;
    this.advance(2);
    let content = '';
    for (;;) {
      if (this.atEnd) {
        throw this.unterminated('逐語文字列', startLine);
      }
      const c = this.peek();
      if (c === '"' && this.peek(1) === '"') {
        content += '"';
        this.advance(2);
      } else if (c === '"') {
        this.advance();
        break;
      } else {
        content += c;
        this.advance();
      }
    }
    this.push('string', start, startLine, { content });
  }

  scanInterpolatedString(prefixLength, verbatim) {
    const start = this.index;
    const startLine = this.line;
    this.advance(prefixLength + 1);
    let content = '';
    for (;;) {
      if (this.atEnd || (!verbatim && this.peek() === '\n')) {
        throw this.unterminated('補間文字列', startLine);
      }
      const c = this.peek();
      if (c === '"' && verbatim && this.peek(1) === '"') {
        content += '"';
        this.advance(2);
      } else if (c === '"') {
        this.advance();
        break;
      } else if ((c === '{' && this.peek(1) === '{') || (c === '}' && this.peek(1) === '}')) {
        content += c;
        this.advance(2);
      } else if (c === '{') {
        this.advance();
        this.scanCode(true);
        content += this.readHoleTail(startLine, verbatim);
      } else if (c === '\\' && !verbatim) {
        content += this.readEscape(startLine);
      } else {
        content += c;
        this.advance();
      }
    }
    this.push('string', start, startLine, { content, interpolated: true });
  }

  /** 補間の式の後ろ（書式指定 :… と、閉じ括弧 }）を読む。書式指定は文字列の中身として返す。 */
  readHoleTail(startLine, verbatim) {
    let format = '';
    if (this.peek() === ':') {
      this.advance();
      while (this.peek() !== '}') {
        if (this.atEnd || (!verbatim && this.peek() === '\n')) {
          throw this.unterminated('補間の書式指定', startLine);
        }
        if (this.peek() === '\\' && !verbatim) {
          format += this.readEscape(startLine);
        } else {
          format += this.peek();
          this.advance();
        }
      }
    }
    if (this.peek() !== '}') {
      throw this.unterminated('補間の式', startLine);
    }
    this.advance();
    return format;
  }

  scanCharLiteral() {
    const start = this.index;
    const startLine = this.line;
    this.advance();
    let content;
    if (this.peek() === '\\') {
      content = this.readEscape(startLine);
    } else if (this.atEnd || this.peek() === '\n' || this.peek() === "'") {
      throw new Error(`文字リテラルが正しくありません（${startLine} 行目）`);
    } else {
      content = this.peek();
      this.advance();
    }
    if (this.peek() !== "'") {
      throw this.unterminated('文字リテラル', startLine);
    }
    this.advance();
    this.push('char', start, startLine, { content });
  }

  scanNumber() {
    const start = this.index;
    const startLine = this.line;
    let isReal = false;
    if (this.peek() === '0' && /[xXbB]/.test(this.peek(1))) {
      const digits = /[xX]/.test(this.peek(1)) ? /[0-9A-Fa-f_]/ : /[01_]/;
      this.advance(2);
      while (this.peek() !== '' && digits.test(this.peek())) {
        this.advance();
      }
    } else {
      while (isDigit(this.peek()) || this.peek() === '_') {
        this.advance();
      }
      if (this.peek() === '.' && isDigit(this.peek(1))) {
        isReal = true;
        this.advance();
        while (isDigit(this.peek()) || this.peek() === '_') {
          this.advance();
        }
      }
      if (/[eE]/.test(this.peek()) && (isDigit(this.peek(1)) || (/[+-]/.test(this.peek(1)) && isDigit(this.peek(2))))) {
        isReal = true;
        this.advance(/[+-]/.test(this.peek(1)) ? 2 : 1);
        while (isDigit(this.peek()) || this.peek() === '_') {
          this.advance();
        }
      }
      if (this.peek() !== '' && /[fFdDmM]/.test(this.peek())) {
        isReal = true;
        this.advance();
      }
    }
    if (!isReal) {
      while (this.peek() !== '' && /[uUlL]/.test(this.peek())) {
        this.advance();
      }
    }
    this.push(isReal ? 'real' : 'integer', start, startLine);
  }

  matchIdentifier() {
    this.identifierPattern.lastIndex = this.index;
    return this.identifierPattern.exec(this.source);
  }

  scanIdentifier() {
    const match = this.matchIdentifier();
    const start = this.index;
    const startLine = this.line;
    this.advance(match[0].length);
    this.push('identifier', start, startLine, { name: decodeIdentifier(match[0]), verbatim: match[0].startsWith('@') });
  }
}

/** SplitMix64 を BigInt で計算する（2^64 を法とする）。nextInt は 2^64 − (2^64 mod bound) 以上を捨てる棄却法。 */
function splitMix64Reference(seed) {
  const word = 1n << BigInt(SPLITMIX64.wordBits);
  const mask = word - 1n;
  let state = BigInt.asUintN(SPLITMIX64.wordBits, BigInt(seed));
  const nextUInt64 = () => {
    state = (state + SPLITMIX64.gamma) & mask;
    let z = state;
    z = ((z ^ (z >> SPLITMIX64.firstShift)) * SPLITMIX64.firstMultiplier) & mask;
    z = ((z ^ (z >> SPLITMIX64.secondShift)) * SPLITMIX64.secondMultiplier) & mask;
    return z ^ (z >> SPLITMIX64.finalShift);
  };
  const nextInt = (bound) => {
    if (!Number.isInteger(bound) || bound < 1) {
      throw new Error(`上限は 1 以上の整数にしてください: ${bound}`);
    }
    const divisor = BigInt(bound);
    const limit = word - (word % divisor);
    for (;;) {
      const value = nextUInt64();
      if (value < limit) {
        return Number(value % divisor);
      }
    }
  };
  return { nextUInt64, nextInt };
}

function toHex64(value) {
  return `0x${value.toString(16).toUpperCase().padStart(16, '0')}`;
}

/** Domain Core の .cs（直下の bin・obj を除く）を読み、字句に分ける。 */
function loadDomainCoreSources() {
  const sources = EXPECT.sources;
  const root = repoPath(sources.domainCoreDir);
  return listFiles(root, '.cs', sources.excludedTopLevelDirNames).map((relative) => {
    const text = readFileSync(path.join(root, relative), 'utf8');
    let tokens;
    try {
      tokens = CSharpLexer.tokenize(text);
    } catch (error) {
      throw new Error(`${sources.domainCoreDir}/${relative} を字句に分けられません: ${error.message}`);
    }
    return {
      relative,
      repoRelative: `${sources.domainCoreDir}/${relative}`,
      isConfig: relative.split('/')[0] === sources.configDirName,
      text,
      tokens,
    };
  });
}

function isLiteral(token) {
  return token.kind === 'string' || token.kind === 'char';
}

function formatToken(file, token) {
  const shown = token.text.length > 80 ? `${token.text.slice(0, 80)}…` : token.text;
  return `${file.repoRelative}:${token.line}: ${shown}`;
}

/** 語が文中にあるか。英数字と _ だけの語は、前後が英数字・_ でないときだけ一致とする（単語として探す）。 */
function containsWord(text, word) {
  if (/^[A-Za-z0-9_]+$/.test(word)) {
    return new RegExp(`(?<![A-Za-z0-9_])${word}(?![A-Za-z0-9_])`).test(text);
  }
  return text.includes(word);
}

function registerSelfCheckSuite() {
  const kinds = (source, wanted) =>
    CSharpLexer.tokenize(source)
      .filter((token) => wanted.includes(token.kind))
      .map((token) => `${token.kind}:${token.kind === 'identifier' ? token.name : token.text}`);
  const contents = (source) => CSharpLexer.tokenize(source).filter(isLiteral).map((token) => token.content);

  describe('検査の仕組みの確認（判定が素通りにならないこと）', () => {
    test('字句解析：コメント・前処理命令・文字列の中の語を、識別子として数えない', () => {
      const source = '#region double\n// double\n/* float */ var s = "decimal"; var c = \'d\';\n#endregion';
      assert.deepEqual(kinds(source, ['identifier']), ['identifier:var', 'identifier:s', 'identifier:var', 'identifier:c']);
      assert.deepEqual(kinds(source, ['comment', 'preprocessor']).map((item) => item.split(':')[0]), ['preprocessor', 'comment', 'comment', 'preprocessor']);
    });

    test('字句解析：実数リテラルを見つけ、16 進数・2 進数・桁区切り・範囲・メンバーの呼び出しを実数と読まない', () => {
      assert.deepEqual(kinds('1.5 2f 3d 4m 1e3 .5 6E-2 7.0e+1', ['real', 'integer']), [
        'real:1.5', 'real:2f', 'real:3d', 'real:4m', 'real:1e3', 'real:.5', 'real:6E-2', 'real:7.0e+1',
      ]);
      assert.deepEqual(kinds('0x9E3779B97F4A7C15UL 0xBFul 3_000_000 0b1010 1..5 1.ToString() 10L', ['real', 'integer']), [
        'integer:0x9E3779B97F4A7C15UL', 'integer:0xBFul', 'integer:3_000_000', 'integer:0b1010', 'integer:1', 'integer:5', 'integer:1', 'integer:10L',
      ]);
    });

    test('字句解析：通常・逐語・補間の文字列と文字リテラルの中身を取り出し、エスケープを文字に戻す', () => {
      assert.deepEqual(contents(String.raw`var a = "\u65E5本\"x"; var b = @"C:\dir ""q"""; var c = '\u8A9E'; var d = '\'';`), ['日本"x', 'C:\\dir "q"', '語', "'"]);
      // 補間の式の中の文字列も取り出す。書式指定と {{ }} は外側の文字列の中身になる
      assert.deepEqual(contents(String.raw`var e = $"{x}日本{(y ? "a" : "b"),5:D2}{{z}}";`), ['a', 'b', '日本D2{z}']);
      assert.deepEqual(contents(String.raw`var f = $@"a""{x}""b"; var g = @$"{global::System.Int32.MaxValue}";`), ['a""b', '']);
    });

    test('字句解析：補間の式の中の日本語の識別子は、文字列の中身に入れない', () => {
      const tokens = CSharpLexer.tokenize('var s = $"{名前}";');
      assert.deepEqual(tokens.filter(isLiteral).map((token) => token.content), ['']);
      assert.ok(tokens.some((token) => token.kind === 'identifier' && token.name === '名前'));
    });

    test('字句解析：逐語識別子と \\u のエスケープを含む識別子を、元の名前に戻す', () => {
      assert.deepEqual(kinds(String.raw`var @double = 1; \u0064ouble x; SeededRandom r;`, ['identifier']), [
        'identifier:var', 'identifier:double', 'identifier:double', 'identifier:x', 'identifier:SeededRandom', 'identifier:r',
      ]);
    });

    test('字句解析：閉じていない文字列・コメント・補間の式と、知らないエスケープを例外にする', () => {
      assert.throws(() => CSharpLexer.tokenize('var s = "abc;\n'));
      assert.throws(() => CSharpLexer.tokenize('/* never closed'));
      assert.throws(() => CSharpLexer.tokenize('var s = $"{x";'));
      assert.throws(() => CSharpLexer.tokenize(String.raw`var s = "\q";`));
      assert.throws(() => CSharpLexer.tokenize("var c = '';"));
    });

    test('日本語の判定：ひらがな・カタカナ・漢字・全角の記号・半角カタカナを見つけ、英数字は見つけない', () => {
      for (const text of ['ですます', 'カタカナー', '漢字', '（）', '、。', 'ｶﾀｶﾅ', 'ａｂｃ']) {
        assert.equal(containsJapanese(text), true, text);
      }
      for (const text of ['easy', 'System.Random', '0x1F', '{0}% ', '']) {
        assert.equal(containsJapanese(text), false, text);
      }
    });

    test('dotnet test の読み取り：失敗を数え、クラスごとに分け、合計との食い違いと合計の欠落を例外にする', () => {
      const output = ['  Passed A.B.C(x: "a b") [1 ms]', '  Failed A.B.D [2 ms]', '  Error Message:', '   x', 'Test Run Failed.', 'Total tests: 2', '     Passed: 1', '     Failed: 1'].join('\n');
      const summary = parseDotnetTestOutput(output);
      assert.equal(summary.passed, 1);
      assert.equal(summary.failed, 1);
      assert.deepEqual([...outcomesByClass(summary.outcomes, 'A.').entries()], [['B', { Passed: 1, Failed: 1, Skipped: 0 }]]);
      assert.throws(() => parseDotnetTestOutput(output.replace('Total tests: 2', 'Total tests: 3')));
      assert.throws(() => parseDotnetTestOutput('  Passed A.B.C [1 ms]'));
      assert.throws(() => outcomesByClass(summary.outcomes, 'Z.'));
    });

    test('dotnet build の読み取り：成否と件数を読み、同じ警告の行を 1 つに数え、件数の行の欠落を例外にする', () => {
      const warning = "/x/A.cs(1,2): warning CS0168: The variable 'x' is declared but never used [/x/DomainCore.csproj]";
      const output = ['  DomainCore -> /x/DomainCore.dll', warning, '', 'Build succeeded.', '', warning, '    1 Warning(s)', '    0 Error(s)'].join('\n');
      const summary = parseBuildSummary(output);
      assert.deepEqual({ succeeded: summary.succeeded, failed: summary.failed, warnings: summary.warnings, errors: summary.errors }, { succeeded: true, failed: false, warnings: 1, errors: 0 });
      assert.equal(summary.warningLines.length, 1);
      assert.throws(() => parseBuildSummary('Build succeeded.'));
    });

    test(`SplitMix64（BigInt）：シード 0 の最初の 5 個が、既知の値（splitmix64.c・SplittableRandom）と一致する`, () => {
      const random = splitMix64Reference(0n);
      const values = EXPECT.splitMix64.canonicalSeedZero.map(() => toHex64(random.nextUInt64()));
      assert.deepEqual(values, EXPECT.splitMix64.canonicalSeedZero);
    });

    test(`${EXPECT.splitMix64.vectorsFile} の値が、BigInt で計算し直した値と一致する（受け入れテストの期待値の確認）`, (t) => {
      const vectors = loadJson(repoPath(EXPECT.splitMix64.vectorsFile));
      assert.ok(vectors.nextUInt64.length > 0 && vectors.nextInt.length > 0, '既知の値がありません');
      for (const entry of vectors.nextUInt64) {
        const random = splitMix64Reference(BigInt(entry.seed));
        assert.deepEqual(entry.values.map(() => toHex64(random.nextUInt64())), entry.values, `シード ${entry.seed} の nextUInt64`);
      }
      for (const entry of vectors.nextInt) {
        const random = splitMix64Reference(BigInt(entry.seed));
        assert.deepEqual(entry.values.map(() => random.nextInt(entry.bound)), entry.values, `シード ${entry.seed}・上限 ${entry.bound} の nextInt`);
      }
      t.diagnostic(`nextUInt64：シード ${vectors.nextUInt64.length} 個・nextInt：${vectors.nextInt.length} 通り`);
    });
  });
}

function registerPreconditionSuite() {
  const ownDir = EXPECT.ownDir;
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

    test(`PR #${EXPECT.pullRequest} の先頭コミットが、手元の HEAD と一致する`, { timeout: testTimeout(EXPECT.timeoutsMs.gh) }, async (t) => {
      const view = await runGh(['pr', 'view', String(EXPECT.pullRequest), '--json', 'headRefOid,headRefName,state']);
      assertExitZero(view, 'gh pr view');
      const { headRefOid, headRefName, state } = parseJsonOutput(view, 'gh pr view');
      const head = (await gitOutput(['rev-parse', 'HEAD'], 'git rev-parse HEAD')).trim();
      t.diagnostic(`PR の先頭：${headRefName} ${headRefOid}（${state}）・手元の HEAD：${head}`);
      assert.equal(headRefOid, head, '手元の HEAD と PR の先頭コミットが異なります（push の前か、別のブランチです）');
    });
  });
}

function registerCoreTestSuite() {
  const core = EXPECT.coreTests;
  describe(`受け入れ条件 1〜8：PR の単体テスト（dotnet test ${core.project}・xUnit）`, () => {
    let run = null;
    let summary = null;
    before(
      async () => {
        run = await runDotnet(['test', core.project, '--logger', 'console;verbosity=normal'], EXPECT.timeoutsMs.dotnetTest);
      },
      { timeout: testTimeout(EXPECT.timeoutsMs.dotnetTest) },
    );

    test(`dotnet test ${core.project} が成功し、${core.expectedTotal} 件すべて合格する（失敗 0 件）`, (t) => {
      assertExitZero(run, 'dotnet test');
      summary = parseDotnetTestOutput(run.stdout);
      t.diagnostic(`dotnet test：合格 ${summary.passed} 件・失敗 ${summary.failed} 件・スキップ ${summary.skipped} 件・合計 ${summary.total} 件`);
      assert.equal(summary.failed, 0, `失敗したテストがあります。\n${describeRun(run)}`);
      assert.equal(summary.skipped, 0, 'スキップしたテストがあります');
      assert.equal(summary.total, core.expectedTotal, `件数が期待（${core.expectedTotal} 件）と異なります`);
      assert.equal(summary.passed, core.expectedTotal);
    });

    test('受け入れ条件 1・2・3・4・8 のテストのクラスがあり、すべて合格している', (t) => {
      const parsed = summary ?? parseDotnetTestOutput(run.stdout);
      const byClass = outcomesByClass(parsed.outcomes, core.namePrefix);
      for (const group of core.classesByCriterion) {
        for (const className of group.classes) {
          const counts = byClass.get(className);
          t.diagnostic(`条件 ${group.criterion}：${className} ${counts === undefined ? '0 件（ありません）' : `合格 ${counts.Passed} 件・失敗 ${counts.Failed} 件・スキップ ${counts.Skipped} 件`}`);
          assert.ok(counts !== undefined && counts.Passed > 0, `${className} のテストがありません`);
          assert.equal(counts.Failed + counts.Skipped, 0, `${className} に合格していないテストがあります`);
        }
      }
    });

    test('受け入れ条件 3・5・6・7 の規則を検査するテスト（ソースと IL の検査）が、すべて合格している', (t) => {
      const parsed = summary ?? parseDotnetTestOutput(run.stdout);
      for (const group of core.ruleTests) {
        for (const name of group.tests) {
          const outcome = parsed.outcomes.get(name);
          t.diagnostic(`条件 ${group.criterion}：${name.slice(core.namePrefix.length)} ${outcome ?? '（実行されていません）'}`);
          assert.equal(outcome, 'Passed', `${name} が合格していません`);
        }
      }
    });
  });
}

function registerAcceptanceTestSuite() {
  const acceptance = EXPECT.acceptanceTests;
  describe(`受け入れ条件 1〜4・6・8：テスターの受け入れテスト（dotnet test ${acceptance.project}・xUnit）`, () => {
    let run = null;
    let summary = null;
    before(
      async () => {
        run = await runDotnet(['test', acceptance.project, '--logger', 'console;verbosity=normal'], EXPECT.timeoutsMs.acceptanceTest);
      },
      { timeout: testTimeout(EXPECT.timeoutsMs.acceptanceTest) },
    );

    test(`dotnet test ${acceptance.project} が成功し、${acceptance.expectedTotal} 件すべて合格する（失敗 0 件）`, (t) => {
      assertExitZero(run, 'dotnet test（受け入れテスト）');
      summary = parseDotnetTestOutput(run.stdout);
      t.diagnostic(`dotnet test：合格 ${summary.passed} 件・失敗 ${summary.failed} 件・スキップ ${summary.skipped} 件・合計 ${summary.total} 件`);
      assert.equal(summary.failed, 0, `失敗したテストがあります。\n${describeRun(run)}`);
      assert.equal(summary.skipped, 0, 'スキップしたテストがあります');
      assert.equal(summary.total, acceptance.expectedTotal, `件数が期待（${acceptance.expectedTotal} 件）と異なります`);
    });

    test('受け入れ条件ごとのクラスが、期待の件数だけ実行され、すべて合格している', (t) => {
      const parsed = summary ?? parseDotnetTestOutput(run.stdout);
      const byClass = outcomesByClass(parsed.outcomes, acceptance.namePrefix);
      for (const expected of acceptance.classes) {
        const counts = byClass.get(expected.class) ?? { Passed: 0, Failed: 0, Skipped: 0 };
        t.diagnostic(`条件 ${expected.criterion}：${expected.class} 合格 ${counts.Passed} 件・失敗 ${counts.Failed} 件・スキップ ${counts.Skipped} 件（期待 ${expected.expected} 件）`);
        assert.equal(counts.Passed, expected.expected, `${expected.class} の合格が ${expected.expected} 件ではありません`);
        assert.equal(counts.Failed + counts.Skipped, 0);
      }
      const unknown = [...byClass.keys()].filter((className) => !acceptance.classes.some((expected) => expected.class === className));
      assert.deepEqual(unknown, [], '期待に無いクラスがあります');
    });
  });
}

function registerReleaseBuildSuite() {
  const build = EXPECT.releaseBuild;
  describe(`Domain Core の Release ビルド（dotnet build ${build.project} -c ${build.configuration}）`, () => {
    test(`dotnet build ${build.project} -c ${build.configuration} --no-incremental が成功し、警告 ${build.expectedWarnings} 件・エラー ${build.expectedErrors} 件である`, { timeout: testTimeout(EXPECT.timeoutsMs.dotnetBuild) }, async (t) => {
      // --no-incremental：前回のビルドの成果物があってもコンパイルし直し、警告を数え漏らさない
      const startedAt = Date.now();
      const result = await runDotnet(['build', build.project, '-c', build.configuration, '--no-incremental'], EXPECT.timeoutsMs.dotnetBuild);
      assertExitZero(result, 'dotnet build');
      const summary = parseBuildSummary(result.stdout);
      t.diagnostic(`dotnet build：${summary.succeeded ? 'Build succeeded' : '成功の表示なし'}・警告 ${summary.warnings} 件・エラー ${summary.errors} 件`);
      for (const line of summary.warningLines) {
        t.diagnostic(`警告：${line}`);
      }
      assert.equal(summary.succeeded, true, `Build succeeded の表示がありません。\n${describeRun(result)}`);
      assert.equal(summary.errors, build.expectedErrors);
      assert.equal(summary.warnings, build.expectedWarnings, `警告の件数が期待（${build.expectedWarnings} 件）と異なります。\n${summary.warningLines.join('\n')}`);

      const dll = repoPath(build.outputDll);
      assert.ok(existsSync(dll), `${build.outputDll} がありません`);
      const writtenAt = statSync(dll).mtimeMs;
      t.diagnostic(`${build.outputDll}：${statSync(dll).size} バイト`);
      assert.ok(writtenAt >= startedAt - MTIME_TOLERANCE_MS, `${build.outputDll} が今回のビルドで書かれていません`);
    });
  });
}

function registerSourceSuites() {
  const sources = EXPECT.sources;
  let files = null;
  const loadOnce = () => {
    files ??= loadDomainCoreSources();
    return files;
  };
  const outsideConfig = () => loadOnce().filter((file) => !file.isConfig);
  const insideConfig = () => loadOnce().filter((file) => file.isConfig);

  describe(`受け入れ条件 5：Config 以外の .cs に文字列リテラルが無い（${sources.domainCoreDir} のソースを字句に分けて調べる）`, () => {
    test('Config の中と外の両方に .cs がある（検査の対象が空でない）', (t) => {
      t.diagnostic(`Config の外：${outsideConfig().length} 件（${outsideConfig().map((file) => file.relative).join('・')}）`);
      t.diagnostic(`Config の中：${insideConfig().length} 件`);
      assert.ok(outsideConfig().length > 0);
      assert.ok(insideConfig().length > 0);
    });

    test('Config 以外の .cs に、日本語を含む文字列リテラル・文字リテラルが無い', (t) => {
      const found = outsideConfig().flatMap((file) => file.tokens.filter((token) => isLiteral(token) && containsJapanese(token.content)).map((token) => formatToken(file, token)));
      t.diagnostic(`日本語を含むリテラル：${found.length} 件`);
      assert.deepEqual(found, []);
    });

    test('Config 以外の .cs に、文字列リテラル・文字リテラルが 1 つも無い（Issue の条件 5・PR 本文の記載）', (t) => {
      const found = outsideConfig().flatMap((file) => file.tokens.filter(isLiteral).map((token) => formatToken(file, token)));
      t.diagnostic(`リテラル：${found.length} 件`);
      assert.deepEqual(found, []);
    });

    test('（検査が素通りしていないことの確認）Config の .cs からは、日本語の文字列リテラルが見つかる', (t) => {
      const literals = insideConfig().flatMap((file) => file.tokens.filter((token) => token.kind === 'string'));
      const japanese = literals.filter((token) => containsJapanese(token.content));
      t.diagnostic(`Config の文字列リテラル：${literals.length} 件（うち日本語を含む ${japanese.length} 件）`);
      assert.ok(japanese.length > 0);
    });

    test(`PR のテストにも同じ検査がある（${EXPECT.coreTests.literalRuleTestSource.file} を読んで確かめる。合否は PR の単体テストの節で確かめる）`, (t) => {
      const rule = EXPECT.coreTests.literalRuleTestSource;
      const text = readRepoFile(rule.file);
      const missing = rule.mustContain.filter((fragment) => !text.includes(fragment));
      t.diagnostic(`確かめた記述：${rule.mustContain.join('／')}`);
      assert.deepEqual(missing, [], `検査のテストに見当たらない記述があります: ${missing.join('／')}`);
    });
  });

  describe('受け入れ条件 6：Domain Core の .cs に float・double・decimal の型の使用が無い（コメントを除いて判定する）', () => {
    test(`型名（${sources.floatingPointNames.join('・')}）と実数リテラル（1.5・2f・3d・4m・1e3 など）が無い`, (t) => {
      const found = loadOnce().flatMap((file) =>
        file.tokens
          .filter((token) => token.kind === 'real' || (token.kind === 'identifier' && sources.floatingPointNames.includes(token.name)))
          .map((token) => formatToken(file, token)),
      );
      const inComments = loadOnce().flatMap((file) =>
        file.tokens
          .filter((token) => (token.kind === 'comment' || token.kind === 'preprocessor') && sources.floatingPointNames.some((name) => containsWord(token.text, name)))
          .map((token) => formatToken(file, token)),
      );
      t.diagnostic(`検査した .cs：${loadOnce().length} 件・コード中の該当：${found.length} 件・コメント中の該当（判定に含めない）：${inComments.length} 件`);
      for (const line of inComments) {
        t.diagnostic(`コメント：${line}`);
      }
      assert.deepEqual(found, []);
    });
  });

  describe('受け入れ条件 3：Domain Core の .cs に System.Random が無い（コメントを除いて判定する）', () => {
    test(`型名 ${sources.randomTypeName}（System.Random・using の別名・global:: を含む）をコードで使っていない`, (t) => {
      const found = loadOnce().flatMap((file) =>
        file.tokens
          .filter((token) => (token.kind === 'identifier' && token.name === sources.randomTypeName) || (isLiteral(token) && token.content.includes(sources.randomQualifiedName)))
          .map((token) => formatToken(file, token)),
      );
      const inComments = loadOnce().flatMap((file) =>
        file.tokens
          .filter((token) => token.kind === 'comment' && token.text.includes(sources.randomQualifiedName))
          .map((token) => formatToken(file, token)),
      );
      t.diagnostic(`コード中の該当：${found.length} 件・コメント中の「${sources.randomQualifiedName}」（判定に含めない）：${inComments.length} 件`);
      for (const line of inComments) {
        t.diagnostic(`コメント：${line}`);
      }
      assert.deepEqual(found, []);
    });
  });

  describe(`受け入れ条件 7：${sources.domainCoreDir}/${sources.configDirName}/ があり、各ファイルの先頭に架空のモデル値の注記がある`, () => {
    test(`${sources.configDirName}/ はフォルダで、PR 本文の ${sources.expectedConfigFiles.length} ファイルがある`, (t) => {
      const dir = repoPath(`${sources.domainCoreDir}/${sources.configDirName}`);
      assert.ok(existsSync(dir) && statSync(dir).isDirectory(), `${sources.configDirName}/ がありません`);
      const names = insideConfig().map((file) => file.relative.slice(sources.configDirName.length + 1));
      t.diagnostic(`Config の .cs：${names.join('・')}`);
      assert.deepEqual(names, [...sources.expectedConfigFiles].sort());
    });

    test(`各ファイルの 1 行目が「${sources.configNoticePrefix}」のコメントで、${sources.configNoticeMarkers.map((marker) => `「${marker}」`).join('')}を含む（ファイルを読んで確かめる）`, (t) => {
      const problems = [];
      for (const file of insideConfig()) {
        const hasBom = file.text.startsWith('\uFEFF');
        const firstLine = (hasBom ? file.text.slice(1) : file.text).split('\n')[0].replace(/\r$/, '');
        t.diagnostic(`${file.relative}${hasBom ? '（BOM あり）' : ''}：${firstLine}`);
        if (!firstLine.startsWith(sources.configNoticePrefix)) {
          problems.push(`${file.relative}：1 行目がコメントではありません`);
        }
        const missing = sources.configNoticeMarkers.filter((marker) => !firstLine.includes(marker));
        if (missing.length > 0) {
          problems.push(`${file.relative}：1 行目に ${missing.join('・')} がありません`);
        }
      }
      assert.ok(insideConfig().length > 0, 'Config の .cs がありません');
      assert.deepEqual(problems, []);
    });
  });

  describe('受け入れ条件 9：ゲームの規則を Domain Core だけに書いている', () => {
    test(`PR の変更は ${EXPECT.editScope.newFilePrefixes.join('・')} の新規ファイル（と ${EXPECT.ownDir}/）だけ（csproj などの既存のファイルを変えていない）`, async (t) => {
      const base = await mergeBase();
      const entries = parseNameStatus(await gitOutput(['diff', '--name-status', '--no-renames', '-z', base, 'HEAD'], 'git diff --name-status'));
      const own = entries.filter((entry) => entry.path.startsWith(`${EXPECT.ownDir}/`));
      const scoped = entries.filter((entry) => EXPECT.editScope.newFilePrefixes.some((prefix) => entry.path.startsWith(prefix)));
      const outside = entries.filter((entry) => !own.includes(entry) && !scoped.includes(entry));
      const notNew = scoped.filter((entry) => entry.status !== 'A');
      for (const prefix of EXPECT.editScope.newFilePrefixes) {
        t.diagnostic(`${prefix}：${scoped.filter((entry) => entry.path.startsWith(prefix)).length} 件`);
      }
      t.diagnostic(`${EXPECT.ownDir}/：${own.length} 件・それ以外：${outside.length} 件（${base.slice(0, 7)}..HEAD）`);
      assert.ok(scoped.length > 0, 'Domain Core の変更がありません');
      assert.deepEqual(outside.map((entry) => `${entry.status} ${entry.path}`), [], '変更の範囲（Issue の Edit scope）の外のファイルがあります');
      assert.deepEqual(notNew.map((entry) => `${entry.status} ${entry.path}`), [], '新規でない（既存を変えた）ファイルがあります');
    });

    test(`${EXPECT.editScope.otherLayersRoot} の ${EXPECT.editScope.ruleOwnerPrefix} 以外（Rails・Unity・再計算ホスト）に、マスタのコードと名前（${sources.masterTableFiles.join('・')} の文字列）が無い`, async (t) => {
      const words = [...new Set(insideConfig()
        .filter((file) => sources.masterTableFiles.includes(file.relative.slice(sources.configDirName.length + 1)))
        .flatMap((file) => file.tokens.filter((token) => token.kind === 'string').map((token) => token.content)))];
      const tracked = splitNul(await gitOutput(['ls-files', '-z', '--', EXPECT.editScope.otherLayersRoot], 'git ls-files'));
      const others = tracked.filter((file) => !file.startsWith(EXPECT.editScope.ruleOwnerPrefix));
      const hits = [];
      let textFiles = 0;
      for (const file of others) {
        const buffer = readFileSync(repoPath(file));
        if (buffer.includes(0)) {
          continue;
        }
        textFiles += 1;
        const text = buffer.toString('utf8');
        for (const word of words) {
          if (containsWord(text, word)) {
            hits.push(`${file}：${word}`);
          }
        }
      }
      t.diagnostic(`探した語：${words.length} 個（${words.join('・')}）`);
      t.diagnostic(`検査したファイル：${textFiles} 件（${EXPECT.editScope.otherLayersRoot} の追跡中 ${tracked.length} 件のうち ${EXPECT.editScope.ruleOwnerPrefix} 以外 ${others.length} 件のテキスト）`);
      assert.ok(words.length > 0, '探す語がありません（Config の字句解析を確かめてください）');
      assert.deepEqual(hits, []);
    });
  });
}

function registerOwnFilesSuite() {
  const e2e = EXPECT.e2e;
  const e2eDir = repoPath(e2e.dir);
  const playwrightDir = path.join(e2eDir, 'node_modules', ...e2e.playwrightPackage.split('/'));
  const ownDirFromE2e = path.relative(e2eDir, HERE);
  describe(`このフォルダ（${EXPECT.ownDir}/）の扱い`, () => {
    for (const expected of EXPECT.ownFiles.cases) {
      test(`${expected.path} は git で${expected.ignored ? '除外される（ビルド成果物）' : '追跡できる（除外されない）'}`, async (t) => {
        const { ignored, rule } = await checkIgnore(expected.path);
        t.diagnostic(`当たった規則：${rule || '（なし）'}`);
        assert.equal(ignored, expected.ignored, `${expected.path} の判定が期待と異なります（${rule}）`);
      });
    }

    test(`Playwright が設定を読み込め、${ownDirFromE2e} のファイルをテストとして拾わない`, { timeout: testTimeout(EXPECT.timeoutsMs.playwrightList) }, async (t) => {
      const manifestPath = path.join(playwrightDir, 'package.json');
      assert.ok(existsSync(manifestPath), `${path.relative(REPO_ROOT, playwrightDir)} がありません。${e2e.dir}/ で npm ci を実行してください`);
      const cli = path.join(playwrightDir, loadJson(manifestPath).bin[e2e.playwrightBin]);
      const result = await runCommand(process.execPath, [cli, 'test', '--list', '--pass-with-no-tests', '--reporter=json'], {
        cwd: e2eDir,
        timeoutMs: EXPECT.timeoutsMs.playwrightList,
        env: {},
      });
      assertExitZero(result, 'playwright test --list');
      const report = parseJsonOutput(result, 'playwright test --list');
      const { files, tests } = collectPlaywrightSpecs(report.suites);
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
  describe(`CI：gh pr checks ${pr} で ${ci.requiredJobs.join('・')} が pass である`, () => {
    test(
      `${ci.workflowName} の ${ci.requiredJobs.join('・')} が pass（実行中なら ${ci.pollIntervalMs / 1000} 秒ごとに、最長 ${ci.waitLimitMs / 60000} 分待つ）`,
      { timeout: ci.waitLimitMs + testTimeout(EXPECT.timeoutsMs.gh) * 2 },
      async (t) => {
        const view = await runGh(['pr', 'view', pr, '--json', 'headRefOid']);
        assertExitZero(view, 'gh pr view');
        const head = (await gitOutput(['rev-parse', 'HEAD'], 'git rev-parse HEAD')).trim();
        assert.equal(parseJsonOutput(view, 'gh pr view').headRefOid, head, 'CI の結果は PR の先頭コミットのもの。手元の HEAD が PR の先頭と異なります');

        const { checks, polls, timedOut } = await waitForRequiredChecks();
        t.diagnostic(`問い合わせ：${polls} 回${timedOut ? '（上限まで待ちましたが、結果がそろいませんでした）' : ''}`);
        for (const check of checks ?? []) {
          t.diagnostic(`${check.workflow} / ${check.name}：${check.bucket}（${check.state}・${check.event}・完了 ${check.completedAt || '未'}）${check.link}`);
        }
        assert.ok(checks !== null, 'チェックがまだ登録されていません');
        assert.equal(timedOut, false, '上限まで待っても、必要なジョブの結果がそろいませんでした');
        for (const job of ci.requiredJobs) {
          const entries = checks.filter((check) => check.workflow === ci.workflowName && check.name === job);
          assert.ok(entries.length > 0, `${ci.workflowName} / ${job} のチェックがありません`);
          assert.ok(
            entries.every((check) => check.bucket === 'pass'),
            `${ci.workflowName} / ${job} が pass ではありません: ${entries.map((check) => check.bucket).join(', ')}`,
          );
        }
      },
    );
  });
}

function registerAllSuites() {
  registerSelfCheckSuite();
  registerPreconditionSuite();
  registerCoreTestSuite();
  registerAcceptanceTestSuite();
  registerReleaseBuildSuite();
  registerSourceSuites();
  registerOwnFilesSuite();
  registerCiSuite();
}

if (process.env[EXPECT.nestedRunGuardEnv] === '1') {
  test('入れ子の起動を検知したため、検査を実行しません', { skip: true }, () => {});
} else {
  registerAllSuites();
}
