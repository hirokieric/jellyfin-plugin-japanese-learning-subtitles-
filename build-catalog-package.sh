#!/usr/bin/env bash
# カタログ用にビルド・ZIP 作成・マニフェスト更新を行う。
# 環境変数 RELEASE_ZIP_URL に ZIP の公開 URL を指定すると、manifest.json の sourceUrl と checksum が書き込まれる。
# 例: RELEASE_ZIP_URL="https://github.com/yourname/jellyfin/releases/download/v1.0.0.0/JapaneseLearningSubtitles.zip" ./build-catalog-package.sh
# 出力: dist/JapaneseLearningSubtitles.zip, manifest/manifest.json（RELEASE_ZIP_URL 指定時のみ更新）

set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PLUGIN_NAME="JapaneseLearningSubtitles"
DLL_NAME="Jellyfin.Plugin.JapaneseLearningSubtitles.dll"
BUILD_OUTPUT="$SCRIPT_DIR/Jellyfin.Plugin.JapaneseLearningSubtitles/bin/Release/net8.0"
ASSEMBLY_INFO="$SCRIPT_DIR/Jellyfin.Plugin.JapaneseLearningSubtitles/Properties/AssemblyInfo.cs"
DIST_DIR="$SCRIPT_DIR/dist"
ZIP_PATH="$DIST_DIR/${PLUGIN_NAME}.zip"
MANIFEST_PATH="$SCRIPT_DIR/manifest/manifest.json"

echo "Building ${PLUGIN_NAME}..."
dotnet build "$SCRIPT_DIR/Jellyfin.Plugin.JapaneseLearningSubtitles/Jellyfin.Plugin.JapaneseLearningSubtitles.csproj" -c Release

if [[ ! -f "$BUILD_OUTPUT/$DLL_NAME" ]]; then
  echo "Error: ビルド出力が見つかりません: $BUILD_OUTPUT/$DLL_NAME" >&2
  exit 1
fi

mkdir -p "$DIST_DIR"
zip -q -o -j "$ZIP_PATH" "$BUILD_OUTPUT/$DLL_NAME"
echo "ZIP を作成しました: $ZIP_PATH"

# バージョン取得（AssemblyInfo.cs の AssemblyVersion）
VERSION="1.0.0.0"
if [[ -f "$ASSEMBLY_INFO" ]]; then
  VERSION=$(grep -E 'AssemblyVersion\(' "$ASSEMBLY_INFO" | sed -E 's/.*"([0-9.]+)".*/\1/')
fi

if [[ -n "${RELEASE_ZIP_URL:-}" ]]; then
  # MD5（32文字の16進数）。macOS: md5 -q / Linux: md5sum
  if command -v md5 >/dev/null 2>&1; then
    CHECKSUM=$(md5 -q "$ZIP_PATH")
  else
    CHECKSUM=$(md5sum "$ZIP_PATH" | awk '{print $1}')
  fi
  TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

  # manifest.json を更新（sourceUrl, checksum, version, timestamp）
  if [[ -f "$MANIFEST_PATH" ]]; then
    if command -v jq >/dev/null 2>&1; then
      jq --arg url "$RELEASE_ZIP_URL" --arg cs "$CHECKSUM" --arg ver "$VERSION" --arg ts "$TIMESTAMP" \
        '.[0].versions[0].sourceUrl = $url | .[0].versions[0].checksum = $cs | .[0].versions[0].version = $ver | .[0].versions[0].timestamp = $ts' \
        "$MANIFEST_PATH" > "${MANIFEST_PATH}.tmp" && mv "${MANIFEST_PATH}.tmp" "$MANIFEST_PATH"
    else
      # jq がない場合はプレースホルダーのみ置換（初回用）
      sed -e "s|REPLACE_WITH_RELEASE_ZIP_URL|${RELEASE_ZIP_URL}|g" \
          -e "s|REPLACE_WITH_MD5_CHECKSUM|${CHECKSUM}|g" \
          -e "s|\"version\": \"1.0.0.0\"|\"version\": \"${VERSION}\"|" \
          -e "s|\"timestamp\": \"2026-02-07T00:00:00Z\"|\"timestamp\": \"${TIMESTAMP}\"|" \
          "$MANIFEST_PATH" > "${MANIFEST_PATH}.tmp" && mv "${MANIFEST_PATH}.tmp" "$MANIFEST_PATH"
    fi
    echo "マニフェストを更新しました: $MANIFEST_PATH (checksum=${CHECKSUM})"
  fi
else
  echo ""
  echo "RELEASE_ZIP_URL が未設定のため、manifest.json は更新しません。"
  echo "ZIP を GitHub Releases などにアップロードしたら、次を実行してください："
  echo "  RELEASE_ZIP_URL='<ZIPの公開URL>' ./build-catalog-package.sh"
fi
