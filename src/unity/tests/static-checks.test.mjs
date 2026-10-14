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

// ---------------------------------------------------------------------------
// 指摘 1・3・5・22 の検査：例外の握りつぶし・?? の既定値・static の可変フィールド・外部の呼び出し、
// 保存の有無の判定、UNITY_EXE の既定値。
// 検出器は C# の本文を読む。コメントと文字列・文字リテラルの中身を空白にしてから、違反の行番号を返す。
// ---------------------------------------------------------------------------

// C# の本文から、コメントと文字列・文字リテラルの中身を空白にする（改行は残し、行番号を保つ）
function csharpCode(source) {
  const blank = (text) => text.replace(/[^\n]/g, ' ');
  let out = '';
  let i = 0;
  const n = source.length;
  while (i < n) {
    const c = source[i];
    const next = source[i + 1];
    if (c === '/' && next === '/') {
      const end = source.indexOf('\n', i);
      const stop = end === -1 ? n : end;
      out += blank(source.slice(i, stop));
      i = stop;
    } else if (c === '/' && next === '*') {
      const end = source.indexOf('*/', i + 2);
      const stop = end === -1 ? n : end + 2;
      out += blank(source.slice(i, stop));
      i = stop;
    } else if (c === '"') {
      const verbatim = source[i - 1] === '@' || (source[i - 1] === '$' && source[i - 2] === '@');
      let j = i + 1;
      while (j < n) {
        if (verbatim) {
          if (source[j] === '"' && source[j + 1] === '"') {
            j += 2;
            continue;
          }
          if (source[j] === '"') break;
        } else {
          if (source[j] === '\\') {
            j += 2;
            continue;
          }
          if (source[j] === '"' || source[j] === '\n') break;
        }
        j += 1;
      }
      const closed = j < n && source[j] === '"';
      out += `"${blank(source.slice(i + 1, j))}${closed ? '"' : ''}`;
      i = closed ? j + 1 : j;
    } else if (c === "'") {
      let j = i + 1;
      while (j < n && source[j] !== "'" && source[j] !== '\n') {
        j += source[j] === '\\' ? 2 : 1;
      }
      const closed = j < n && source[j] === "'";
      out += `'${blank(source.slice(i + 1, j))}${closed ? "'" : ''}`;
      i = closed ? j + 1 : j;
    } else {
      out += c;
      i += 1;
    }
  }
  return out;
}

// 1 始まりの行番号
function lineOf(text, index) {
  return text.slice(0, index).split('\n').length;
}

// open の { に対応する } までの中身（コードの部分）。対応が無ければ例外にして、検査が黙って通らないようにする
function blockBody(code, open) {
  let depth = 0;
  for (let i = open; i < code.length; i += 1) {
    if (code[i] === '{') depth += 1;
    if (code[i] === '}') {
      depth -= 1;
      if (depth === 0) return code.slice(open + 1, i);
    }
  }
  throw new Error(`対応する } が見つからない（${lineOf(code, open)} 行目）`);
}

// 握りつぶした catch（本文に throw も Debug.Log も無い）の行番号
function findSwallowedCatches(source) {
  const code = csharpCode(source);
  const lines = [];
  for (const match of code.matchAll(/\bcatch\b/g)) {
    const open = code.indexOf('{', match.index);
    if (open === -1) throw new Error(`catch の本文が無い（${lineOf(code, match.index)} 行目）`);
    const body = blockBody(code, open);
    if (!/\bthrow\b/.test(body) && !/\bDebug\.Log(?:Warning|Error|Exception)?\s*\(/.test(body)) {
      lines.push(lineOf(code, match.index));
    }
  }
  return lines;
}

// ?? の既定値（右辺が throw ではないもの）の行番号。?? throw（値が無ければ例外）は許す
function findNullCoalescingDefaults(source) {
  const code = csharpCode(source);
  return [...code.matchAll(/\?\?=?(?!\s*throw\b)/g)].map((match) => lineOf(code, match.index));
}

// static の可変フィールドの行番号（static readonly と const は除く。メソッド・プロパティ・static クラスは対象外）
function findMutableStaticFields(source) {
  const code = csharpCode(source);
  const pattern = /^[ \t]*(?:\[[^\]\n]*\][ \t]*)*(?:(?:public|private|internal|protected)[ \t]+)?static[ \t]+(?!readonly\b|const\b|class\b|struct\b|enum\b|interface\b)[^();{}=\n]+?[ \t]+[A-Za-z_]\w*[ \t]*(?:=[^;\n]*)?;/gm;
  return [...code.matchAll(pattern)].map((match) => lineOf(code, match.index));
}

// 外部の JavaScript・ネイティブの呼び出し（ExternalEval・ExternalCall・DllImport・extern）の行番号（重複なし）
function findExternalCalls(source) {
  const code = csharpCode(source);
  const lines = new Set([...code.matchAll(/\b(?:ExternalEval|ExternalCall|DllImport|extern)\b/g)]
    .map((match) => lineOf(code, match.index)));
  return [...lines].sort((a, b) => a - b);
}

// ? が三項演算子の記号か（?. ・?[ ・?? の一部は除く）
function isTernaryQuestion(code, index) {
  const prev = code[index - 1];
  const next = code[index + 1];
  return code[index] === '?' && next !== '?' && next !== '.' && next !== '[' && prev !== '?';
}

// 三項演算子の ? の位置から、真の枝と偽の枝の本文を返す。対応する : が無ければ例外にして、検査が黙って通らないようにする。
// 真の枝は ; と閉じ括弧で止まる。偽の枝は、それに加えて , でも止まる（型の引数の , を枝の終わりと誤認しないため）。
function splitTernary(code, question) {
  let depth = 0;
  let nested = 0;
  let colon = -1;
  let end = code.length;
  for (let j = question + 1; j < code.length; j += 1) {
    const c = code[j];
    if (c === '(' || c === '[' || c === '{') {
      depth += 1;
    } else if (c === ')' || c === ']' || c === '}') {
      if (depth === 0) {
        end = j;
        break;
      }
      depth -= 1;
    } else if (depth === 0 && c === ';') {
      end = j;
      break;
    } else if (depth === 0 && c === ',' && colon !== -1) {
      end = j;
      break;
    } else if (depth === 0 && colon === -1 && isTernaryQuestion(code, j)) {
      nested += 1;
    } else if (depth === 0 && colon === -1 && c === ':' && code[j - 1] !== ':' && code[j + 1] !== ':') {
      if (nested === 0) {
        colon = j;
      } else {
        nested -= 1;
      }
    }
  }

  if (colon === -1) {
    throw new Error(`三項演算子の : が見つからない（${lineOf(code, question)} 行目）`);
  }

  return {
    whenTrue: code.slice(question + 1, colon).trim(),
    whenFalse: code.slice(colon + 1, end).trim(),
  };
}

// null の判定（== null・is null・IsNullOrEmpty・IsNullOrWhiteSpace、およびその否定）を条件にした三項演算子のうち、
// 値が null のときの枝が null でも throw でもないもの（既定値を入れているもの）の行番号。null を返す枝と throw する枝は許す。
function findNullDefaultTernaries(source) {
  const code = csharpCode(source);
  const nullTest = /(?:==\s*null|\bis\s+null|\bIsNull(?:OrEmpty|OrWhiteSpace)\s*\((?:[^()]|\([^()]*\))*\))\s*$/;
  const notNullTest = /(?:!=\s*null|\bis\s+not\s+null|!\s*(?:string\.)?IsNull(?:OrEmpty|OrWhiteSpace)\s*\((?:[^()]|\([^()]*\))*\))\s*$/;
  const lines = [];
  for (let i = 0; i < code.length; i += 1) {
    if (!isTernaryQuestion(code, i)) continue;
    const before = code.slice(Math.max(0, i - 200), i);
    const negated = notNullTest.test(before);
    if (!negated && !nullTest.test(before)) continue;
    const { whenTrue, whenFalse } = splitTernary(code, i);
    const nullBranch = negated ? whenFalse : whenTrue;
    if (nullBranch !== 'null' && !/^throw\b/.test(nullBranch)) {
      lines.push(lineOf(code, i));
    }
  }
  return lines;
}

// Assets 配下の C# 本文（Plugins と Resources を除く）に、検出器を当てて違反を集める
function collectViolations(detector) {
  const violations = [];
  for (const file of unityCodeFiles()) {
    for (const line of detector(readFileSync(file, 'utf8'))) {
      violations.push(`${repoRel(file)}:${line}`);
    }
  }
  return violations;
}

test('検出器は違反の例を見つけ、正しい書き方を見逃さない（自己確認）', () => {
  const bad = [
    'class A {',
    '  static int counter = 0;',
    '  static readonly int Max = 3;',
    '  const string Name = "static int x;";',
    '  // static int y;',
    '  static void Run() { }',
    '  void F() {',
    '    try { G(); } catch (System.Exception e) { return; }',
    '    var x = a ?? "";',
    '    var y = a ?? throw new System.Exception();',
    '    Application.ExternalEval("x");',
    '  }',
    '  [System.Runtime.InteropServices.DllImport("x")]',
    '  static extern int N();',
    '}',
  ].join('\n');
  assert.deepEqual(findMutableStaticFields(bad), [2], 'static の可変フィールドを行 2 で見つけるべき');
  assert.deepEqual(findSwallowedCatches(bad), [8], '握りつぶした catch を行 8 で見つけるべき');
  assert.deepEqual(findNullCoalescingDefaults(bad), [9], '?? の既定値を行 9 で見つけるべき');
  assert.deepEqual(findExternalCalls(bad), [11, 13, 14], '外部の呼び出しを行 11・13・14 で見つけるべき');

  const good = [
    'try { G(); } catch (System.Exception e) { throw; }',
    'try { G(); } catch (System.Exception e) { Debug.LogWarning("x"); return; }',
    'var y = a ?? throw new System.Exception();',
  ].join('\n');
  assert.deepEqual(findSwallowedCatches(good), [], '再送出と記録のある catch は見逃さない');
  assert.deepEqual(findNullCoalescingDefaults(good), [], '?? throw は既定値ではない');
});

test('例外は握りつぶさない（catch は throw するか、Debug.Log で記録する）', () => {
  assert.deepEqual(collectViolations(findSwallowedCatches), [], '握りつぶした catch がある');
});

test('既定値の ?? を使わない（値が無いときは明示的に扱う。?? throw は許す）', () => {
  assert.deepEqual(collectViolations(findNullCoalescingDefaults), [], '?? の既定値がある');
});

test('null の判定で既定値を入れる三項演算子の検出器は、違反の例を見つけ、正しい書き方を見逃さない（自己確認）', () => {
  const bad = [
    'class A {',
    '  string F(D d, C c, string x, bool flag) {',
    '    var t = d == null ? string.Empty : d.text;',
    '    var n = c == null ? 0 : c.Value;',
    '    var s = d != null ? d.text : "";',
    '    var u = string.IsNullOrEmpty(x) ? "x" : x;',
    '    var v = !string.IsNullOrEmpty(x) ? x : "y";',
    '    var w = d == null',
    '      ? string.Empty',
    '      : d.text;',
    '    var r = d != null ? d.text : flag ? "a" : "b";',
    '    return w + r;',
    '  }',
    '}',
  ].join('\n');
  assert.deepEqual(findNullDefaultTernaries(bad), [3, 4, 5, 6, 7, 9, 11], '既定値を入れる三項演算子を行 3・4・5・6・7・9・11 で見つけるべき');
  assert.throws(() => findNullDefaultTernaries('var a = d == null ? 1;'), /三項演算子の :/, '対応する : が無いときは黙って通さず例外にする');

  const good = [
    'var a = d == null ? null : d.text;',
    'var b = d == null ? throw new System.Exception() : d.text;',
    'var c = d != null ? d.text : null;',
    'var e = flag ? "a" : "b";',
    'var f = d?.text;',
    'var g = d?.Child ?? throw new System.Exception();',
    'int? h = null;',
    'var k = a == null || b == null ? null : c;',
    'var z = "d == null ? 1 : 2"; // d == null ? 3 : 4',
  ].join('\n');
  assert.deepEqual(findNullDefaultTernaries(good), [], 'null と throw の枝、null の判定ではない三項演算子、文字列とコメントの中は違反にしない');
});

test('null の判定で既定値を入れない（== null ? 既定値・!= null ? 値 : 既定値 を使わない。null と throw は許す）', () => {
  assert.deepEqual(collectViolations(findNullDefaultTernaries), [], 'null の判定で既定値を入れる三項演算子がある');
});

test('static の可変フィールドを持たない（static readonly と const だけ）', () => {
  assert.deepEqual(collectViolations(findMutableStaticFields), [], 'static の可変フィールドがある');
});

test('ExternalEval・ExternalCall・DllImport・extern を使わない（外部の JavaScript とネイティブを呼ばない）', () => {
  assert.deepEqual(collectViolations(findExternalCalls), [], '外部の呼び出しがある');
});

test('保存の有無は JSON の null だけを「保存なし」とし、想定外の型は型名をログに残して失敗にする', () => {
  const source = csharpCode(read('src/unity/Assets/Scripts/Game/GameController.cs'));
  assert.doesNotMatch(source, /hasSavedGame\s*=[^;]*==\s*JTokenType\.Object/, '保存の有無を型の等値比較だけで決めている');
  assert.match(source, /JTokenType\.Null/, '保存なし（JSON の null）の判定が無い');
  assert.match(source, /LogTexts\.FieldTypeUnexpected,\s*ApiConfig\.KeyGame,\s*\w+\.Type\)/, '想定外の型の名前をログに残していない');
  assert.equal((source.match(/TryReadSavedGame\(/g) ?? []).length, 3, '起動と続きからが同じ判定を通っていない（定義 1 と呼び出し 2）');
});

test('ビルドの UNITY_EXE は既定値を持たず、未設定ならビルドを始めずに止まる', () => {
  const script = read('src/unity/tools/build-webgl.sh');
  assert.doesNotMatch(script, /\/mnt\/[a-z]\//, 'Windows 側の既定のパスが残っている');
  assert.doesNotMatch(script, /^\s*UNITY_EXE=/m, 'UNITY_EXE に既定値を代入している');
  assert.doesNotMatch(script, /UNITY_EXE[^\n]*既定/, 'UNITY_EXE の説明に既定値がある');
  const guard = script.indexOf('-z "${UNITY_EXE:-}"');
  const use = script.indexOf('-f "$UNITY_EXE"');
  assert.ok(guard !== -1, 'UNITY_EXE が未設定のときの確認が無い');
  assert.ok(use > guard, 'UNITY_EXE の確認が、場所を使う前に無い');
  assert.match(script.slice(guard, use), /exit 1/, 'UNITY_EXE が未設定のときに終了していない');
});

test('README は UNITY_EXE を必須として説明し、WSL の内部パスを書かない', () => {
  const readme = read('README.md');
  assert.match(readme, /UNITY_EXE[^\n]*(必須|未設定)/, 'README に UNITY_EXE の設定方法（必須・未設定のとき）が無い');
  assert.doesNotMatch(readme, /\/mnt\/[a-z]\//, 'README に WSL の内部パスがある');
  assert.doesNotMatch(readme, /[A-Za-z]:\\/, 'README に Windows の絶対パスがある');
});

// ---------------------------------------------------------------------------
// 無言の null スキップ：null の判定で何もせずに続ける if（else も throw も記録も無いもの）。
// 検出器は判定の文と行を返すだけで、必要かどうかは判断しない。必要な部品（スライダーの塗りつぶし）は例外にして、
// 任意の引数や任意の表示（onClick・onChanged・notice など）は仕様として省略してよいので、この検査の対象にしない。
// ---------------------------------------------------------------------------

// open の ( か { に対応する閉じ括弧の位置。対応が無ければ例外にして、検査が黙って通らないようにする
function closingIndex(code, open) {
  const close = code[open] === '(' ? ')' : '}';
  let depth = 0;
  for (let i = open; i < code.length; i += 1) {
    if (code[i] === code[open]) depth += 1;
    if (code[i] === close) {
      depth -= 1;
      if (depth === 0) return i;
    }
  }
  throw new Error(`対応する ${close} が見つからない（${lineOf(code, open)} 行目）`);
}

// 無言の null スキップ（条件が null の判定で、else が無く、本文に throw も Debug.Log も無い if）の行と判定の文
function findSilentNullSkips(source) {
  // null の判定：== null・!= null・is null・is not null・IsNullOrEmpty・IsNullOrWhiteSpace、値型の HasValue
  const nullTest = /(?:==|!=)\s*null\b|\bis\s+(?:not\s+)?null\b|\bIsNull(?:OrEmpty|OrWhiteSpace)\s*\(|\.HasValue\b/;
  const code = csharpCode(source);
  const skips = [];
  for (const match of code.matchAll(/\bif\s*\(/g)) {
    const open = match.index + match[0].length - 1;
    const close = closingIndex(code, open);
    const condition = code.slice(open + 1, close).trim();
    if (!nullTest.test(condition)) continue;

    let start = close + 1;
    while (start < code.length && /\s/.test(code[start])) start += 1;
    let end;
    if (code[start] === '{') {
      end = closingIndex(code, start) + 1;
    } else {
      const semicolon = code.indexOf(';', start);
      if (semicolon === -1) throw new Error(`if の本文の ; が見つからない（${lineOf(code, match.index)} 行目）`);
      end = semicolon + 1;
    }

    const body = code.slice(start, end);
    if (/^\s*else\b/.test(code.slice(end))) continue;
    if (/\bthrow\b/.test(body) || /\bDebug\.Log(?:Warning|Error|Exception)?\s*\(/.test(body)) continue;
    skips.push({ line: lineOf(code, match.index), condition });
  }
  return skips;
}

test('無言の null スキップの検出器は、違反の例を見つけ、正しい書き方を見逃さない（自己確認）', () => {
  const bad = [
    'class A {',
    '  void F(Image fill, string text, Slider s, float? v) {',
    '    if (fill != null) { fill.color = C; }',
    '    if (s == null) return;',
    '    if (!string.IsNullOrEmpty(text)) { Use(text); }',
    '    if (text is null) { Other(); }',
    '    if (v.HasValue) { Use(v.Value); }',
    '  }',
    '}',
  ].join('\n');
  assert.deepEqual(findSilentNullSkips(bad), [
    { line: 3, condition: 'fill != null' },
    { line: 4, condition: 's == null' },
    { line: 5, condition: '!string.IsNullOrEmpty(text)' },
    { line: 6, condition: 'text is null' },
    { line: 7, condition: 'v.HasValue' },
  ], '本文に throw も記録も無い null の判定を、行と判定の文で見つけるべき');

  const good = [
    'class A {',
    '  void F(Image fill, Slider s) {',
    '    if (fill == null) { throw new System.InvalidOperationException(M); }',
    '    if (fill != null) { fill.color = C; } else { throw new System.InvalidOperationException(M); }',
    '    if (fill == null) { Debug.LogWarning(M); return; }',
    '    if (s.Count > 0) { Use(); }',
    '    var z = "if (fill != null) { }";',
    '    // if (fill != null) { }',
    '  }',
    '}',
  ].join('\n');
  assert.deepEqual(findSilentNullSkips(good), [], 'throw・記録・else のある判定、null の判定ではない if、文字列とコメントの中は違反にしない');
  assert.throws(() => findSilentNullSkips('if (fill != null { Use(); }'), /対応する \) が見つからない/, '対応する括弧が無いときは黙って通さず例外にする');
});

test('スライダーの塗りつぶしは、無いときに例外を投げ、無言で飛ばさない（UiFactory.cs）', () => {
  const source = read('src/unity/Assets/Scripts/UI/UiFactory.cs');
  const silentFill = findSilentNullSkips(source).filter((skip) => /\bfill(?:Rect)?\b/.test(skip.condition));
  assert.deepEqual(silentFill, [], '塗りつぶしの判定が、無いときに無言で飛ばしている');

  const code = csharpCode(source);
  assert.match(
    code,
    /if\s*\(\s*(?:slider\.)?fillRect\s*==\s*null\s*\)\s*\{\s*throw new InvalidOperationException\(LogTexts\.SliderFillMissing\);\s*\}/,
    'fillRect が無いときに例外を投げていない',
  );
  assert.match(
    code,
    /if\s*\(\s*fill\s*==\s*null\s*\)\s*\{\s*throw new InvalidOperationException\(LogTexts\.SliderFillImageMissing\);\s*\}/,
    'Image が無いときに例外を投げていない',
  );
  assert.match(code, /fill\.color = AppConfig\.SliderFillColor;/, '正常時の塗りつぶしの色の設定が無い');

  const logTexts = read('src/unity/Assets/Scripts/Config/LogTexts.cs');
  assert.match(logTexts, /\bSliderFillMissing = "[^"\n]+";/, 'LogTexts に SliderFillMissing が無い');
  assert.match(logTexts, /\bSliderFillImageMissing = "[^"\n]+";/, 'LogTexts に SliderFillImageMissing が無い');
});
