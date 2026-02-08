# Japanese Learning Subtitles — Jellyfin プラグイン

英語字幕のタイミングに合わせて日本語 SRT 字幕を自動生成する Jellyfin プラグインです。
Jellyfin の二重字幕表示機能と組み合わせることで、英語学習に活用できます。

---

## 動作概要

1. ライブラリ内の動画を定期スキャンし、既存の英語 SRT を検出
2. OpenSubtitles から日本語字幕を取得し、英語字幕のタイムコードに DP アライメント
3. 日本語字幕が見つからない場合は翻訳 API（OpenAI / DeepL / カスタム）でフォールバック
4. メディアファイルと同じフォルダに `.ja.srt` として保存

---

## 前提条件

| 項目 | バージョン |
|------|-----------|
| Jellyfin Server | 10.10.x 以上 |
| .NET SDK（ビルド時のみ） | 8.0 以上 |
| OpenSubtitles アカウント | REST API キーが必要（任意） |
| 翻訳 API キー | OpenAI / DeepL のいずれか（任意） |

---

## ビルド方法

### 1. リポジトリをクローン

```bash
git clone <リポジトリURL>
cd Jellyfin.Plugin.JapaneseLearningSubtitles
```

### 2. NuGet パッケージを復元してビルド

```bash
dotnet restore Jellyfin.Plugin.JapaneseLearningSubtitles.sln
dotnet build Jellyfin.Plugin.JapaneseLearningSubtitles.sln -c Release
```

ビルド成果物は以下に出力されます：

```
Jellyfin.Plugin.JapaneseLearningSubtitles/bin/Release/net8.0/Jellyfin.Plugin.JapaneseLearningSubtitles.dll
```

### 3. テストの実行（任意）

```bash
dotnet test Jellyfin.Plugin.JapaneseLearningSubtitles.sln
```

SRT パース/ライト、テキスト正規化、DP アライメント、EN 字幕検出、キャッシュの各ユニットテストが実行されます。

---

## インストール方法

### 方法 1：カタログからインストール（リポジトリを追加）※推奨

プラグインフォルダを直接触らず、Jellyfin の「リポジトリを追加」からカタログでインストールできます。

1. Jellyfin 管理画面 → **ダッシュボード** → **プラグイン** → **カタログ**
2. **リポジトリ** で **「+」** をクリック
3. **名前**: 任意（例: Japanese Learning Subtitles）  
   **URL**: このプラグインの **manifest.json の公開 URL** を指定  
   - 例（GitHub で `manifest` を公開している場合）:  
     `https://raw.githubusercontent.com/<あなたのユーザー名>/<リポジトリ名>/<ブランチ名>/manifest/manifest.json`
4. **OK** で保存
5. カタログに **「Japanese Learning Subtitles」** が表示されるので、**インストール** をクリック
6. 必要に応じて Jellyfin を再起動

**注意:** 上記の URL が有効になるには、リポジトリ管理者が [公開手順](#公開手順リポジトリ管理者向け) に従って manifest と ZIP を公開している必要があります。

---

### 公開手順（リポジトリ管理者向け）

カタログで配布するには、**manifest.json** と **ZIP パッケージ** を公開 URL で配れるようにします。

1. **ZIP を作成し、公開 URL を用意する**  
   - GitHub の場合: リリースを作成し、`dist/JapaneseLearningSubtitles.zip` をアップロード  
   - リリースの ZIP の URL 例:  
     `https://github.com/<user>/<repo>/releases/download/v1.0.0.0/JapaneseLearningSubtitles.zip`

2. **マニフェストを更新する**（ZIP の URL と checksum を書き込む）:
   ```bash
   RELEASE_ZIP_URL='https://github.com/<user>/<repo>/releases/download/v1.0.0.0/JapaneseLearningSubtitles.zip' ./build-catalog-package.sh
   ```
   - 上記で `dist/JapaneseLearningSubtitles.zip` が作成され、`manifest/manifest.json` の `sourceUrl` と `checksum` が更新されます。
   - **jq** が入っていれば、既に URL が入っている manifest も正しく更新されます。

3. **manifest.json を公開する**  
   - `manifest/manifest.json` をリポジトリにコミット・プッシュする  
   - ユーザーが「リポジトリを追加」で指定する URL は、このファイルの **raw  URL**（例:  
     `https://raw.githubusercontent.com/<user>/<repo>/main/manifest/manifest.json`）です。

4. **バージョンアップ時**  
   - 新しいリリース用 ZIP を GitHub Releases などにアップロード  
   - 再度 `RELEASE_ZIP_URL=... ./build-catalog-package.sh` を実行して manifest を更新し、コミット・プッシュ  
   - 必要に応じて `manifest/manifest.json` の `versions` に新しい要素を追加（複数バージョン対応）

---

### 方法 A：DLL を手動コピー

1. ビルドした DLL をプラグインフォルダにコピーします：

```bash
# Linux の場合
mkdir -p /var/lib/jellyfin/plugins/JapaneseLearningSubtitles
cp Jellyfin.Plugin.JapaneseLearningSubtitles/bin/Release/net8.0/Jellyfin.Plugin.JapaneseLearningSubtitles.dll \
   /var/lib/jellyfin/plugins/JapaneseLearningSubtitles/

# Windows の場合
# C:\ProgramData\Jellyfin\Server\plugins\JapaneseLearningSubtitles\ にコピー

# macOS の場合
# ~/.local/share/jellyfin/plugins/JapaneseLearningSubtitles/ にコピー
```

2. Jellyfin Server を再起動します：

```bash
sudo systemctl restart jellyfin
```

### 方法 2：ZIP パッケージとしてインストール（Install from ZIP）

1. プロジェクトルートで ZIP を作成：

```bash
./install-plugin.sh
```

2. Jellyfin の管理画面 → プラグイン → カタログの **「Install from ZIP」** で `dist/JapaneseLearningSubtitles.zip` をアップロード
3. Jellyfin Server を再起動

### 方法 3: Docker 環境の場合

`docker-compose.yml` のボリュームマッピングでプラグインフォルダをマウント済みの場合：

```bash
# ホスト側のプラグインフォルダにコピー
cp Jellyfin.Plugin.JapaneseLearningSubtitles.dll /path/to/jellyfin/plugins/JapaneseLearningSubtitles/

# コンテナを再起動
docker-compose restart jellyfin
```

---

## 設定

Jellyfin の管理画面から「プラグイン」→「Japanese Learning Subtitles」の設定画面を開きます。

### OpenSubtitles 設定

| 設定項目 | 説明 |
|---------|------|
| Username | OpenSubtitles のアカウント名 |
| Password | OpenSubtitles のパスワード |
| API Key | [opensubtitles.com](https://www.opensubtitles.com) で発行した REST API キー |

OpenSubtitles の設定は任意です。未設定の場合、すべての字幕が翻訳 API 経由で生成されます。

### 翻訳プロバイダー設定

| プロバイダー | 設定項目 | 説明 |
|------------|---------|------|
| **OpenAI**（推奨） | API Key | OpenAI の API キー |
| | Model | 使用するモデル名（デフォルト: `gpt-4o-mini`） |
| **DeepL** | API Key | DeepL の API キー（末尾 `:fx` でFreeプラン自動判別） |
| **Custom HTTP** | Endpoint | POST リクエストを受け付けるカスタムエンドポイント URL |

カスタム HTTP エンドポイントのリクエスト/レスポンス形式：

```json
// リクエスト（POST）
{ "texts": ["Hello", "World"], "source": "en", "target": "ja" }

// レスポンス
{ "translations": ["こんにちは", "世界"] }
```

### スキャン設定

| 設定項目 | デフォルト | 説明 |
|---------|----------|------|
| Scope | Movies & Series | スキャン対象（映画のみ / TV シリーズのみ / 両方） |
| Overwrite existing | OFF | 既存の `.ja.srt` を上書きするかどうか |
| Max Parallel | 2 | 同時処理数（API レートリミット考慮） |
| Alignment Confidence Threshold | 0.3 | この閾値未満のキューは翻訳フォールバック（0.0〜1.0） |

---

## タスクの実行

### 自動実行（スケジュール）

デフォルトでは毎週日曜日の午前2時に実行されます。

管理画面 →「ダッシュボード」→「スケジュールされたタスク」で変更可能です。

### 手動実行

管理画面 →「スケジュールされたタスク」→「Generate Japanese Learning Subtitles」→「今すぐ実行」

---

## 出力ファイル

メディアファイルと同じフォルダにサイドカーファイルとして保存されます：

```
映画の場合:
  /movies/The Matrix (1999)/The Matrix (1999).mkv
  /movies/The Matrix (1999)/The Matrix (1999).en.srt   ← 既存の英語字幕
  /movies/The Matrix (1999)/The Matrix (1999).ja.srt   ← 生成された日本語字幕

TV シリーズの場合:
  /tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.mkv
  /tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.en.srt
  /tv/Breaking Bad/Season 01/Breaking Bad - S01E01 - Pilot.ja.srt
```

出力は標準的な SRT 形式（UTF-8 BOM 付き）です。

---

## 再生時の使い方

1. Jellyfin プレイヤーで動画を再生
2. 字幕メニューから英語字幕と日本語字幕をそれぞれ選択
3. Jellyfin の二重字幕表示で EN + JA を同時表示

---

## プロジェクト構成

```
Jellyfin.Plugin.JapaneseLearningSubtitles/
├── Plugin.cs                          # プラグインエントリポイント
├── PluginServiceRegistrator.cs        # DI サービス登録
├── Configuration/
│   ├── PluginConfiguration.cs         # 設定モデル
│   └── configPage.html                # 管理画面 UI
├── Srt/
│   ├── SubtitleCue.cs                 # 字幕キューモデル
│   ├── SrtParser.cs                   # SRT パーサー
│   ├── SrtWriter.cs                   # SRT ライター
│   └── TextNormalizer.cs              # テキスト正規化（EN/JA）
├── Providers/
│   ├── EnglishSubtitleLocator.cs      # 英語 SRT 検出
│   ├── OpenSubtitlesClient.cs         # OpenSubtitles API クライアント
│   ├── ITranslationProvider.cs        # 翻訳プロバイダーインターフェース
│   ├── OpenAITranslationProvider.cs   # OpenAI 翻訳
│   ├── DeepLTranslationProvider.cs    # DeepL 翻訳
│   ├── CustomHttpTranslationProvider.cs # カスタム HTTP 翻訳
│   └── TranslationProviderFactory.cs  # プロバイダーファクトリ
├── Alignment/
│   └── SubtitleAligner.cs             # DP アライメントエンジン
├── Cache/
│   └── GenerationCacheStore.cs        # 生成キャッシュ（冪等性）
├── ScheduledTasks/
│   └── GenerateJapaneseLearningSubtitlesTask.cs  # スケジュールタスク
└── Properties/
    └── AssemblyInfo.cs

Jellyfin.Plugin.JapaneseLearningSubtitles.Tests/
├── Tests/
│   ├── SrtParserTests.cs
│   ├── SrtWriterTests.cs
│   ├── TextNormalizerTests.cs
│   ├── SubtitleAlignerTests.cs
│   ├── EnglishSubtitleLocatorTests.cs
│   └── GenerationCacheTests.cs
└── Fixtures/
    ├── sample_en.srt
    ├── sample_ja.srt
    ├── sample_en_tagged.srt
    └── sample_ja_shifted.srt
```

---

## 処理パイプライン

```
動画アイテム
  │
  ├─ 英語 SRT を検出 ─→ なし → スキップ（警告ログ）
  │
  ├─ .ja.srt が既存 & 上書き OFF → スキップ
  │
  ├─ キャッシュ確認 → EN字幕未変更 → スキップ
  │
  ├─ OpenSubtitles で日本語字幕を検索
  │    ├─ 見つかった → DP アライメントで EN タイミングに合わせる
  │    │                 └─ 信頼度が低いキュー → 翻訳フォールバック
  │    └─ 見つからない → 全キューを翻訳 API で生成
  │
  ├─ .ja.srt として保存
  │
  └─ キャッシュ更新
```

---

## トラブルシューティング

### プラグインが表示されない

Jellyfin のプラグインフォルダに DLL が配置されているか確認してください。フォルダパスは OS によって異なります。再起動後に管理画面の「プラグイン」に表示されるはずです。

### OpenSubtitles ログインに失敗する

ログに `OpenSubtitles login failed` と表示される場合、ユーザー名・パスワード・API キーを確認してください。OpenSubtitles REST API v1 のキーが必要です（レガシー XML-RPC キーでは動作しません）。

### 字幕が生成されない

Jellyfin のログで `No English SRT found` を検索してください。この警告が出ている場合、英語の SRT サイドカーファイルが存在しないか、ファイル命名規則がマッチしていません。対応する命名パターンは `.en.srt`, `.eng.srt`, `.english.srt` です。

### 翻訳の品質が低い

OpenAI のモデルを `gpt-4o` に変更するか、`gpt-4o-mini` のままでも翻訳品質は十分なことが多いです。Alignment Confidence Threshold を下げると、アライメント結果をより多く採用し、翻訳フォールバックの割合が減ります。

### API レートリミットエラー

Max Parallel を `1` に下げてください。OpenSubtitles と翻訳 API の両方にレートリミットがあるため、大きいライブラリでは低めの並列数が安全です。

---

## ライセンス

MIT License
