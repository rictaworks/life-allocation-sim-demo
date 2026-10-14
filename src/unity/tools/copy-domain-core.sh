#!/usr/bin/env bash
# Domain Core（netstandard2.1）を Release でビルドし、その DLL を Unity の Assets/Plugins/DomainCore/ にコピーする。
# ソースはコピーしない。DLL は Git に載せない（.gitignore で除外）。既存のファイルは上書きするだけで、削除はしない。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CORE_DIR="$ROOT/src/core/DomainCore"
DLL_SOURCE="$CORE_DIR/bin/Release/netstandard2.1/DomainCore.dll"
PLUGIN_DIR="$ROOT/src/unity/Assets/Plugins/DomainCore"

dotnet build "$CORE_DIR" -c Release
mkdir -p "$PLUGIN_DIR"
cp -f "$DLL_SOURCE" "$PLUGIN_DIR/DomainCore.dll"
echo "コピーしました: src/unity/Assets/Plugins/DomainCore/DomainCore.dll"
