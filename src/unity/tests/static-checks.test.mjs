// Unity 側（担当 D）の静的な検査。Unity エディタ内のコードは動かさない（イシュー #11 の 10 の例外）。
// 検査の対象は、リポジトリ上の実ファイルの内容と、git check-ignore の結果だけにする。
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const repoRoot = join(here, '..', '..', '..');
const unityRoot = join(repoRoot, 'src', 'unity');
const assetsRoot = join(unityRoot, 'Assets');
const thisFile = join(here, 'static-checks.test.mjs');

const read = (path) => readFileSync(join(repoRoot, path), 'utf8');
const exists = (path) => existsSync(join(repoRoot, path));

// dir 以下のファイルを再帰的に列挙する（存在しないディレクトリは空）
function listFiles(dir, predicate) {
  if (!existsSync(dir)) return [];
  const out = [];
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) {
      out.push(...listFiles(full, predicate));
    } else if (predicate(full)) {
      out.push(full);
    }
  }
  return out;
}

const repoRel = (full) => relative(repoRoot, full).split(sep).join('/');

// Config の外にある C# ファイル（Plugins と Resources は対象外）
function unityCodeFiles() {
  return listFiles(assetsRoot, (f) => f.endsWith('.cs'))
    .filter((f) => !/[\\/](Plugins|Resources)[\\/]/.test(f));
}

// C# の文字列リテラルを拾う（コメント・文字リテラル・逐語文字列・補間文字列に対応する）
function findStringLiterals(source) {
  const found = [];
  let line = 1;
  let i = 0;
  const n = source.length;
  while (i < n) {
    const c = source[i];
    const next = source[i + 1];
    if (c === '\n') {
      line += 1;
      i += 1;
    } else if (c === '/' && next === '/') {
      while (i < n && source[i] !== '\n') i += 1;
    } else if (c === '/' && next === '*') {
      i += 2;
      while (i < n && !(source[i] === '*' && source[i + 1] === '/')) {
        if (source[i] === '\n') line += 1;
        i += 1;
      }
      i += 2;
    } else if (c === '"') {
      const verbatim = i > 0 && (source[i - 1] === '@' || (source[i - 1] === '$' && source[i - 2] === '@'));
      const startLine = line;
      let text = '';
      i += 1;
      while (i < n) {
        const d = source[i];
        if (d === '\n') line += 1;
        if (verbatim) {
          if (d === '"' && source[i + 1] === '"') {
            text += '""';
            i += 2;
            continue;
          }
          if (d === '"') break;
        } else {
          if (d === '\\') {
            text += d + (source[i + 1] ?? '');
            i += 2;
            continue;
          }
          if (d === '"') break;
        }
        text += d;
        i += 1;
      }
      i += 1;
      found.push({ line: startLine, text });
    } else if (c === "'") {
      i += 1;
      while (i < n && source[i] !== "'" && source[i] !== '\n') {
        i += source[i] === '\\' ? 2 : 1;
      }
      i += 1;
    } else {
      i += 1;
    }
  }
  return found;
}

// git check-ignore：除外されるなら true、されないなら false
function isIgnored(path) {
  const result = spawnSync('git', ['check-ignore', '--no-index', '-q', path], { cwd: repoRoot });
  if (result.status === 0) return true;
  if (result.status === 1) return false;
  throw new Error(`git check-ignore が失敗しました（${result.status}）: ${result.stderr}`);
}

test('.gitignore は Unity の生成物と DLL と WebGL の出力を除外し、ソースは除外しない', () => {
  const mustIgnore = [
    'src/unity/Library/x',
    'src/unity/Temp/x',
    'src/unity/Obj/x',
    'src/unity/Logs/x.log',
    'src/unity/UserSettings/x',
    'src/unity/Build/x',
    'src/unity/MemoryCaptures/x',
    'src/unity/Assets/Plugins/DomainCore/DomainCore.dll',
    'src/web/unity/Build/LifeAllocationSim.loader.js',
    'src/web/unity/TemplateData/style.css',
    'src/web/unity/StreamingAssets/x',
  ];
  const mustKeep = [
    'src/unity/Assets/Scripts/Config/UiTexts.cs',
    'src/unity/Assets/Editor/WebGLBuild.cs',
    'src/unity/Assets/Plugins.meta',
    'src/unity/Assets/Scenes/Main.unity.meta',
    'src/unity/Packages/manifest.json',
    'src/unity/ProjectSettings/ProjectVersion.txt',
    'src/unity/tools/build-webgl.sh',
    'src/unity/tools/copy-domain-core.sh',
  ];
  for (const path of mustIgnore) assert.equal(isIgnored(path), true, `除外されるべき: ${path}`);
  for (const path of mustKeep) assert.equal(isIgnored(path), false, `除外されてはいけない: ${path}`);
});

test('Assets 配下の .meta（GUID を含む）は除外しない。除外するのは DLL のフォルダの中だけ', () => {
  const metas = listFiles(assetsRoot, (f) => f.endsWith('.meta'))
    .map(repoRel)
    .filter((path) => !path.startsWith('src/unity/Assets/Plugins/DomainCore/'));
  assert.ok(metas.length > 0, 'Assets 配下に .meta が無い');
  for (const path of metas) assert.equal(isIgnored(path), false, `除外されてはいけない: ${path}`);
  assert.equal(isIgnored('src/unity/Assets/Plugins/DomainCore/DomainCore.dll.meta'), true, 'DLL の .meta は DLL と一緒に除外する');
});

test('DLL を取り出すスクリプトは Domain Core を Release でビルドし、DLL だけをコピーする', () => {
  const path = 'src/unity/tools/copy-domain-core.sh';
  assert.ok(exists(path), `${path} が無い`);
  const script = read(path);
  assert.match(script, /dotnet build/);
  assert.match(script, /src\/core\/DomainCore/);
  assert.match(script, /-c Release/);
  assert.match(script, /bin\/Release\/netstandard2\.1\/DomainCore\.dll/);
  assert.match(script, /src\/unity\/Assets\/Plugins\/DomainCore/);
  assert.doesNotMatch(script, /\*\.cs/, 'ソース（.cs）をコピーしてはいけない');
});

test('Unity 側のスクリプトに、削除や同期の削除オプションを含めない', () => {
  // 検査の定義そのものはこのファイルにあるため、このファイルは対象から外す
  const scripts = listFiles(unityRoot, (f) => /\.(sh|mjs|js|ps1|bat)$/.test(f)).filter((f) => f !== thisFile);
  assert.ok(scripts.length > 0, 'Unity 側のスクリプトが見つからない');
  const forbidden = [
    /(^|[\s;&|])rm\s/m,
    /\brmdir\b/,
    /\bunlink\b/,
    /git\s+clean/,
    /-delete\b/,
    /--delete\b/,
    /robocopy[^\n]*\/MIR/i,
  ];
  for (const file of scripts) {
    const body = readFileSync(file, 'utf8');
    for (const pattern of forbidden) {
      assert.doesNotMatch(body, pattern, `${repoRel(file)} に削除系の記述がある: ${pattern}`);
    }
  }
});

test('ビルドの入口は Unity.exe をバッチモードで呼び、Editor の静的メソッドを実行する', () => {
  const path = 'src/unity/tools/build-webgl.sh';
  assert.ok(exists(path), `${path} が無い`);
  const script = read(path);
  for (const word of ['-batchmode', '-nographics', '-executeMethod', '-quit', '-logFile', 'wslpath -w', 'src/web/unity', 'copy-domain-core.sh']) {
    assert.ok(script.includes(word), `${path} に ${word} が無い`);
  }
  assert.ok(script.includes('LifeAllocationSim.Editor.WebGLBuild.Build'), 'executeMethod の名前が一致しない');
  const cs = read('src/unity/Assets/Editor/WebGLBuild.cs');
  assert.match(cs, /namespace\s+LifeAllocationSim\.Editor/);
  assert.match(cs, /public\s+static\s+void\s+Build\s*\(/);
});

test('Unity プロジェクトの骨組みは 6000.0.34f1 で、必要なパッケージを持つ', () => {
  assert.match(read('src/unity/ProjectSettings/ProjectVersion.txt'), /m_EditorVersion:\s*6000\.0\.34f1/);
  const manifest = JSON.parse(read('src/unity/Packages/manifest.json'));
  for (const name of [
    'com.unity.ugui',
    'com.unity.nuget.newtonsoft-json',
    'com.unity.modules.ui',
    'com.unity.modules.unitywebrequest',
  ]) {
    assert.ok(name in manifest.dependencies, `manifest に ${name} が無い`);
  }
  assert.ok(exists('src/unity/ProjectSettings/ProjectSettings.asset'), 'ProjectSettings.asset が無い');
});

test('WebGL の製品名・会社名・版は web/js/config.js と一致し、圧縮は無効にする', () => {
  const config = read('src/web/js/config.js');
  const productName = /productName:\s*'([^']+)'/.exec(config)?.[1];
  const companyName = /companyName:\s*'([^']+)'/.exec(config)?.[1];
  const productVersion = /productVersion:\s*'([^']+)'/.exec(config)?.[1];
  assert.ok(productName && companyName && productVersion, 'config.js から値を読めない');

  const buildConfig = read('src/unity/Assets/Editor/Config/BuildConfig.cs');
  assert.ok(buildConfig.includes(`"${productName}"`), `製品名 ${productName} が BuildConfig.cs に無い`);
  assert.ok(buildConfig.includes(`"${companyName}"`), `会社名 ${companyName} が BuildConfig.cs に無い`);
  assert.ok(buildConfig.includes(`"${productVersion}"`), `版 ${productVersion} が BuildConfig.cs に無い`);

  const build = read('src/unity/Assets/Editor/WebGLBuild.cs');
  assert.match(build, /WebGLCompressionFormat\.Disabled/);
});

test('C# の文字列リテラルは Config/ に集約する（Assets 配下の全ファイル）', () => {
  const files = unityCodeFiles();
  assert.ok(files.some((f) => f.endsWith('UiTexts.cs')), 'UiTexts.cs が見つからない');
  const violations = [];
  for (const file of files) {
    if (/[\\/]Config[\\/]/.test(file)) continue;
    for (const literal of findStringLiterals(readFileSync(file, 'utf8'))) {
      if (literal.text !== '') violations.push(`${repoRel(file)}:${literal.line} "${literal.text}"`);
    }
  }
  assert.deepEqual(violations, [], '文字列リテラルは Config/ に移す');
});

test('規則の数値を Unity 側に書かない（年齢・万分率・目安額は Domain Core から読む）', () => {
  const forbidden = /(?<![\w.])(22|65|10000|15000|30000000|30_000_000)(?![\w])/;
  const violations = [];
  assert.ok(unityCodeFiles().length > 0, 'Unity の C# が見つからない');
  for (const file of unityCodeFiles()) {
    if (/[\\/]Config[\\/]/.test(file)) continue;
    readFileSync(file, 'utf8').split('\n').forEach((text, index) => {
      const code = text.replace(/\/\/.*$/, '');
      if (forbidden.test(code)) violations.push(`${repoRel(file)}:${index + 1}`);
    });
  }
  assert.deepEqual(violations, [], '規則の数値は GameRules などから読む');
});

test('PlayerPrefs・Unity Play・エディタのダイアログを使わない', () => {
  const forbidden = [/PlayerPrefs/, /EnterPlayMode/, /EditorApplication\.isPlaying/, /DisplayDialog/];
  assert.ok(unityCodeFiles().length > 0, 'Unity の C# が見つからない');
  for (const file of unityCodeFiles()) {
    const body = readFileSync(file, 'utf8');
    for (const pattern of forbidden) {
      assert.doesNotMatch(body, pattern, `${repoRel(file)} で使用禁止の API を使っている: ${pattern}`);
    }
  }
});

test('保存 API のパスと契約 2 のエラーコードは ApiConfig.cs に集約する', () => {
  const api = read('src/unity/Assets/Scripts/Config/ApiConfig.cs');
  for (const path of ['/api/session', '/api/game', '/api/games', '/api/games/turns']) {
    assert.ok(api.includes(`"${path}"`), `パス ${path} が無い`);
  }
  const codes = [
    'session_required',
    'origin_forbidden',
    'unsupported_media_type',
    'payload_too_large',
    'bad_request',
    'allocation_negative',
    'allocation_step_mismatch',
    'allocation_sum_mismatch',
    'difficulty_missing',
    'difficulty_unknown',
    'game_not_found',
    'turn_conflict',
    'turn_allocation_mismatch',
    'game_finished',
    'rate_limited',
    'service_unavailable',
    'internal_error',
  ];
  for (const code of codes) {
    assert.ok(api.includes(`"${code}"`), `エラーコード ${code} が無い`);
  }
});

test('ゲームの途中の 401（session_required）は 404 と同じくリセットとして新規開始へ誘導し、開始前の 401 は従来どおり再試行する', () => {
  const source = read('src/unity/Assets/Scripts/Game/GameController.cs');
  // 途中の操作は「次の年へ」の保存だけ。その失敗処理に途中のフラグを渡す
  assert.match(source, /\}, \(\) => AdvanceYear\(allocation\), inGame: true\);/, '次の年への保存が途中のフラグを渡していない');
  assert.match(source, /HandleFailure\(response, retry, inGame\)/, '途中のフラグが失敗処理に渡っていない');
  assert.match(
    source,
    /code == ApiConfig\.ErrorGameNotFound \|\| \(inGame && code == ApiConfig\.ErrorSessionRequired\)/,
    '途中の 401 がリセットの条件に入っていない',
  );
  assert.match(source, /ResetToTitle\(UiTexts\.ResetNotice\)/, 'リセットの文言が無い');
  assert.match(
    source,
    /case ApiConfig\.ErrorSessionRequired:\s*ShowError\(UiTexts\.ErrorSession, Boot, UiTexts\.Retry\);/,
    'ゲームを始める前の 401 の再試行が無い',
  );
});

test('画面の文言は UiTexts.cs に集約し、要件の主な文言を含む', () => {
  const texts = read('src/unity/Assets/Scripts/Config/UiTexts.cs');
  for (const word of [
    'データがリセットされました',
    '次の年へ',
    '続きから',
    '新規開始',
    'もう一度遊ぶ',
    '架空のモデル値',
    '再試行',
    '上書き',
    '貯蓄',
    '投資',
    '生活費',
    '満足度',
  ]) {
    assert.ok(texts.includes(word), `UiTexts.cs に「${word}」が無い`);
  }
});

test('要件 6 章の画面を、それぞれの名前の C# で持つ', () => {
  for (const name of ['TitleScreen', 'MainScreen', 'EventScreen', 'YearResultScreen', 'FinalScreen', 'AssetChart']) {
    assert.ok(exists(`src/unity/Assets/Scripts/UI/${name}.cs`), `${name}.cs が無い`);
  }
});

test('日本語の表示に使うフォントと、そのライセンス（OFL）を同梱する', () => {
  const font = join(repoRoot, 'src/unity/Assets/Resources/Fonts/MPLUS1p-Regular.ttf');
  assert.ok(existsSync(font), 'フォントが無い');
  assert.ok(statSync(font).size > 100_000, 'フォントのサイズが小さすぎる');
  const license = join(repoRoot, 'src/unity/Assets/Resources/Fonts/OFL.txt');
  assert.ok(existsSync(license), 'ライセンスが無い');
  assert.match(readFileSync(license, 'utf8'), /SIL OPEN FONT LICENSE/);
});
