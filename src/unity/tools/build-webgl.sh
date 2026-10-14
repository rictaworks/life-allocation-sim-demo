#!/usr/bin/env bash
# Unity 6000.0.34f1 で WebGL をビルドし、出力を src/web/unity/ に置く（WSL から Windows 側の Unity.exe を呼ぶ）。
#
# 使い方（リポジトリのルートで）： bash src/unity/tools/build-webgl.sh
# 環境変数（省略可）：
#   UNITY_EXE        Unity.exe の場所（既定：Unity Hub の 6000.0.34f1）
#   LASD_UNITY_WORK  Windows 側の作業フォルダ（既定：%TEMP%\LifeAllocationSim-unity-build）
#
# 仕組み：
#   Unity は大文字と小文字を区別するファイルシステム上のプロジェクトを開けないため（WSL のフォルダは不可）、
#   src/unity の Assets・Packages・ProjectSettings を Windows 側の作業フォルダへ上書きコピーし、そこでビルドする。
#   ビルド後に生成物（.meta・シーン・packages-lock.json・ProjectSettings）を src/unity へ戻し、出力を src/web/unity/ へ置く。
#   削除はしない。作業フォルダにだけ残ったソースは一覧で示して止まるので、内容を確認してから手動で削除してください。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
UNITY_EXE="${UNITY_EXE:-/mnt/c/Program Files/Unity/Hub/Editor/6000.0.34f1/Editor/Unity.exe}"
EXECUTE_METHOD="LifeAllocationSim.Editor.WebGLBuild.Build"
# Unity は出力のファイル名を出力フォルダの名前から決める。製品名（BuildConfig.ProductName）と一致させる
PRODUCT_NAME="LifeAllocationSim"
OUTPUT_DIR="$ROOT/src/web/unity"
REQUIRED_FILES=(
  "Build/LifeAllocationSim.loader.js"
  "Build/LifeAllocationSim.framework.js"
  "Build/LifeAllocationSim.data"
  "Build/LifeAllocationSim.wasm"
)

if [ ! -f "$UNITY_EXE" ]; then
  echo "Unity.exe が見つかりません: $UNITY_EXE（UNITY_EXE で場所を指定できます）" >&2
  exit 1
fi

# 1. Domain Core の DLL を用意する
bash "$ROOT/src/unity/tools/copy-domain-core.sh"

# 2. Windows 側の作業フォルダへ、ソースを上書きコピーする
if [ -z "${LASD_UNITY_WORK:-}" ]; then
  WIN_TEMP="$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r')"
  LASD_UNITY_WORK="$(wslpath -u "$WIN_TEMP")/LifeAllocationSim-unity-build"
fi
WORK="$LASD_UNITY_WORK"
mkdir -p "$WORK/Assets" "$WORK/Packages" "$WORK/ProjectSettings" "$WORK/webgl/$PRODUCT_NAME"
cp -r "$ROOT/src/unity/Assets/." "$WORK/Assets/"
cp -r "$ROOT/src/unity/Packages/." "$WORK/Packages/"
cp -r "$ROOT/src/unity/ProjectSettings/." "$WORK/ProjectSettings/"

# 3. 作業フォルダにだけあるソースが無いか確かめる（ビルドに混ざるのを防ぐ）
STALE="$(cd "$WORK/Assets" && find . -type f -not -path './Plugins/*' -not -name '*.meta' | sort | while read -r file; do
  if [ ! -e "$ROOT/src/unity/Assets/$file" ]; then echo "$file"; fi
done)"
if [ -n "$STALE" ]; then
  echo "作業フォルダにだけ残っているソースがあるため、中止します。" >&2
  echo "$STALE" | sed "s#^#  $WORK/Assets/#" >&2
  echo "内容を確認し、不要なら手動で削除してから、もう一度実行してください。" >&2
  exit 1
fi

# 4. Unity.exe をバッチモードで呼ぶ（-nographics。終了コードとログを確かめる）
WIN_PROJECT="$(wslpath -w "$WORK")"
WIN_OUTPUT="$(wslpath -w "$WORK/webgl/$PRODUCT_NAME")"
WIN_LOG="$(wslpath -w "$WORK/build.log")"
set +e
"$UNITY_EXE" -batchmode -nographics -quit \
  -projectPath "$WIN_PROJECT" \
  -executeMethod "$EXECUTE_METHOD" \
  -outputPath "$WIN_OUTPUT" \
  -logFile "$WIN_LOG"
UNITY_STATUS=$?
set -e
if [ "$UNITY_STATUS" -ne 0 ]; then
  echo "Unity の終了コード: $UNITY_STATUS（ログ: $WORK/build.log）" >&2
  grep -E "error|Exception|Build failed" "$WORK/build.log" | tail -n 30 >&2 || true
  exit "$UNITY_STATUS"
fi

# 5. 生成物を src/unity へ戻す（.meta・シーン・packages-lock.json・ProjectSettings）
(cd "$WORK" && find Assets -type f \( -name '*.meta' -o -name '*.unity' \) -not -path 'Assets/Plugins/*' -exec cp --parents {} "$ROOT/src/unity/" \;)
cp -f "$WORK/Packages/packages-lock.json" "$ROOT/src/unity/Packages/"
cp -f "$WORK/ProjectSettings/"*.asset "$WORK/ProjectSettings/ProjectVersion.txt" "$ROOT/src/unity/ProjectSettings/"

# 6. 出力を src/web/unity/ へ置く（上書きだけ。削除はしない）
mkdir -p "$OUTPUT_DIR"
cp -r "$WORK/webgl/$PRODUCT_NAME/." "$OUTPUT_DIR/"
for file in "${REQUIRED_FILES[@]}"; do
  if [ ! -f "$OUTPUT_DIR/$file" ]; then
    echo "出力が見つかりません: src/web/unity/$file（ログ: $WORK/build.log）" >&2
    exit 1
  fi
done
echo "完了: src/web/unity/ に WebGL のビルドを置きました（ログ: $WORK/build.log）。"
