#!/usr/bin/env bash
# プラグインをビルドし、ZIP パッケージを作成する。
# 作成した ZIP を Jellyfin 管理画面の「Install from ZIP」からアップロードしてインストールする。
# 使い方: ./install-plugin.sh

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_NAME="JapaneseLearningSubtitles"
BUILD_OUTPUT="$SCRIPT_DIR/Jellyfin.Plugin.JapaneseLearningSubtitles/bin/Release/net8.0"
DLL_NAME="Jellyfin.Plugin.JapaneseLearningSubtitles.dll"
DIST_DIR="$SCRIPT_DIR/dist"
ZIP_PATH="$DIST_DIR/${PLUGIN_NAME}.zip"

echo "Building ${PLUGIN_NAME}..."
dotnet build "$SCRIPT_DIR/Jellyfin.Plugin.JapaneseLearningSubtitles/Jellyfin.Plugin.JapaneseLearningSubtitles.csproj" -c Release

if [[ ! -f "$BUILD_OUTPUT/$DLL_NAME" ]]; then
  echo "Error: ビルド出力が見つかりません: $BUILD_OUTPUT/$DLL_NAME" >&2
  exit 1
fi

mkdir -p "$DIST_DIR"

# 公式プラグインと同様に meta.json を ZIP に含める（インストールに必要）
MANIFEST_PATH="$SCRIPT_DIR/manifest/manifest.json"
META_JSON_PATH="$DIST_DIR/meta.json"
if [[ -f "$MANIFEST_PATH" ]] && command -v jq >/dev/null 2>&1; then
  jq -c '.[0] | {guid, name, description, overview, owner, category} + .versions[0] | {guid, name, description, overview, owner, category, version, changelog, targetAbi, timestamp} | with_entries(select(.value != null))' \
    "$MANIFEST_PATH" > "$META_JSON_PATH"
  zip -q -o -j "$ZIP_PATH" "$BUILD_OUTPUT/$DLL_NAME" "$META_JSON_PATH"
else
  zip -q -o -j "$ZIP_PATH" "$BUILD_OUTPUT/$DLL_NAME"
fi

echo "ZIP を作成しました: $ZIP_PATH"
echo ""
echo "次の手順でインストールしてください："
echo "  1. Jellyfin 管理画面を開く"
echo "  2. ダッシュボード → プラグイン → カタログ の「Install from ZIP」"
echo "  3. 上記の ZIP ファイルを選択してアップロード"
echo "  4. Jellyfin を再起動"