# OpenSubtitles API — 本プラグインでの使い方

Jellyfin Plugin Japanese Learning Subtitles が OpenSubtitles REST API をどう使っているかをまとめたドキュメントです。

---

## 1. このプロジェクトでの役割

- **目的**: 日本語字幕（.ja.srt）生成の**入力**として、OpenSubtitles から日本語字幕を取得する。
- **取得できない場合**: 英語字幕を翻訳（OpenAI / DeepL / カスタムHTTP）して日本語字幕を生成する。
- **実装**: `Jellyfin.Plugin.JapaneseLearningSubtitles.Providers.OpenSubtitlesClient` が API の認証・検索・ダウンロードを担当。

---

## 2. 設定（プラグイン側）

| 設定項目 | 説明 | 保存先 |
|----------|------|--------|
| **OpenSubtitlesUsername** | OpenSubtitles のログイン用ユーザー名 | `PluginConfiguration` |
| **OpenSubtitlesPassword** | ログイン用パスワード | `PluginConfiguration` |
| **OpenSubtitlesApiKey** | REST API 用 API キー（全リクエストで必須） | `PluginConfiguration` |

- 設定画面: プラグイン設定ページの「OpenSubtitles」セクション（`Configuration/configPage.html`）。
- API キー未設定の場合は OpenSubtitles は使わず、翻訳のみで日本語字幕を生成する。

---

## 3. API キーの作成方法

プラグインで OpenSubtitles を使うには、**API キー**と**ログイン用のアカウント**（ユーザー名・パスワード）が必要です。

### 手順

1. **アカウントを作成する**
   - [OpenSubtitles のサインアップページ](https://www.opensubtitles.com/en/users/sign_up) で新規登録する。
   - 過去に OpenSubtitles.org のアカウントがある場合は、[アカウントのインポート](https://www.opensubtitles.com/en/users/import) で移行できる。

2. **API キーを発行する**
   - ログイン後、**[API Consumers](https://www.opensubtitles.com/en/consumers)** のページを開く。
   - プロフィールや設定から「API Consumers」／「Consumers」などと書かれたリンクを探してもよい。
   - そのページで「Create」や「Generate」などから **API キーを 1 つ作成**する。
   - 表示されたキー（長い文字列）をコピーし、**プラグイン設定の「API Key」欄に貼り付けて保存**する。

3. **プラグイン設定に入力する**
   - **Username**: 手順1で登録したメールアドレスまたはユーザー名。
   - **Password**: そのアカウントのパスワード。
   - **API Key**: 手順2でコピーした API キー。

### 補足

- **ダウンロード制限**: 検索回数に制限はないが、字幕の**ダウンロード回数**はアカウント種別で異なります（匿名: 24時間あたり5本、認証ユーザー: 10本〜、VIP: 最大1000本など）。詳しくは [OpenSubtitles ヘルプ](https://opensubtitles.tawk.help/article/getting-started) を参照。
- **API キーの扱い**: 全 API リクエストでこのキーが必須です。他人に公開しないようにしてください。

---

## 4. クライアントの作成・利用箇所

- **HttpClient の登録**: `PluginServiceRegistrator.cs` で `AddHttpClient("OpenSubtitles")` を登録。
- **OpenSubtitlesClient の利用**: `GenerateJapaneseLearningSubtitlesTask` 内で、API キーが設定されている場合にのみ `OpenSubtitlesClient` を new し、`LoginAsync` 成功時に `osAvailable = true` として以降の処理で利用する。

```csharp
// GenerateJapaneseLearningSubtitlesTask.cs のイメージ
osClient = new OpenSubtitlesClient(
    _httpClientFactory.CreateClient("OpenSubtitles"),
    _loggerFactory.CreateLogger<OpenSubtitlesClient>());

osAvailable = await osClient.LoginAsync(
    config.OpenSubtitlesUsername,
    config.OpenSubtitlesPassword,
    config.OpenSubtitlesApiKey,
    ct);
```

---

## 5. API の前提（本プロジェクトで使用している値）

| 項目 | 値 |
|------|-----|
| ベースURL | `https://api.opensubtitles.com/api/v1` |
| User-Agent | `JellyfinJapaneseLearningSubtitles v1.0` |
| リトライ | 最大3回、指数バックオフ（初回2秒） |
| トークン有効期限 | ログイン後 23 時間で再ログイン（API は約24時間） |

---

## 6. 認証（ログイン）

- **エンドポイント**: `POST {BaseUrl}/login`
- **ヘッダ**: `Api-Key`, `Accept: application/json`, `User-Agent`
- **ボディ**: `{"username":"...","password":"..."}`

`OpenSubtitlesClient.LoginAsync(username, password, apiKey, ct)` が担当。

- 既にトークンがあり `DateTime.UtcNow < _tokenExpiry` の場合はそのまま `true` を返す。
- 成功時は `_authToken` と `_tokenExpiry` をセットし、`_httpClient.DefaultRequestHeaders.Authorization = Bearer <token>` を設定。
- 429 / 5xx のときは `ExecuteWithRetryAsync` でリトライ。

---

## 7. 字幕検索（日本語のみ）

- **エンドポイント**: `GET {BaseUrl}/subtitles?languages=ja&order_by=download_count&order_direction=desc&...`
- **認証**: 上記 Bearer トークン（ログイン済みであること）

`OpenSubtitlesClient.SearchJapaneseSubtitlesAsync(imdbId, tmdbId, title, year, parentImdbId, seasonNumber, episodeNumber, ct)` が担当。

### 検索条件の付け方（実装どおり）

1. **imdbId がある場合**: `imdb_id=<id>`（`tt` が無ければ先頭に付与）
2. **なければ tmdbId**: `tmdb_id=<id>`
3. **なければ title**: `query=<title>`、年があれば `year=<year>`
4. **エピソードの場合**: `parent_imdb_id`（シリーズの IMDb）、`season_number`、`episode_number` を追加

いずれも無い場合は検索せず空リストを返す。

### レスポンスの利用

- `data[].attributes.files` が空のエントリは除外。
- 各要素から `SubtitleSearchResult`: `FileId`, `FileName`, `DownloadCount`, `Format` を組み立てて返す。
- 本タスクでは「ダウンロード数最大の1件」を採用（`results[0]`）。

---

## 8. 字幕ダウンロード

- **ステップ1**: `POST {BaseUrl}/download` に `{"file_id": <id>}` を送り、レスポンスの `link` を取得。
- **ステップ2**: その `link` に GET でアクセスし、返ってきた文字列をそのまま字幕テキスト（SRT 等）として扱う。

`OpenSubtitlesClient.DownloadSubtitleAsync(fileId, ct)` が両方を行う。成功時は SRT 文字列、失敗時は `null`。

---

## 9. 本プラグインでの呼び出しフロー

1. **GenerateJapaneseLearningSubtitlesTask** が対象動画リストを取得し、1本ずつ処理。
2. 各動画で **TryFetchOpenSubtitlesJapanese(item, osClient, ct)** を呼ぶ。
   - **item のメタデータ取得**:
     - IMDb: `item.ProviderIds[MetadataProvider.Imdb]`
     - TMDb: `item.ProviderIds[MetadataProvider.Tmdb]`（int にパース）
     - タイトル: `item.Name`、年: `item.ProductionYear`
     - エピソードの場合: `Episode.ParentIndexNumber` / `IndexNumber`、シリーズの `ProviderIds[Imdb]` を `parentImdbId` に。
   - **SearchJapaneseSubtitlesAsync**(上記引数) で日本語字幕を検索。
   - 結果が 0 件なら `null` を返す（翻訳フォールバック）。
   - 1 件以上なら `results[0]` の `FileId` で **DownloadSubtitleAsync** を実行。
   - 取得した文字列を **SrtParser.ParseString(content)** で `List<SubtitleCue>` にし、それを返す。
3. OpenSubtitles で取得した cue は、必要に応じて **SubtitleAligner** で英語字幕のタイムコードにアラインされ、学習用日本語字幕生成に使われる。

---

## 10. レスポンス用モデル（OpenSubtitlesClient 内）

- **LoginResponse**: `token`
- **SearchResponse**: `data` → `SearchDataItem[]`
- **SearchDataItem**: `attributes` → **SearchAttributes**
- **SearchAttributes**: `download_count`, `format`, `files` → **SearchFile[]**
- **SearchFile**: `file_id`, `file_name`
- **DownloadResponse**: `link`

公開型は **SubtitleSearchResult**（`FileId`, `FileName`, `DownloadCount`, `Format`）。

---

## 11. エラーハンドリング（実装どおり）

- **ログイン失敗**: `LoginAsync` が `false`。タスクは「OpenSubtitles なし」として翻訳のみで続行。
- **検索失敗 / 0 件**: 空リストを返し、当該動画は翻訳で日本語字幕を生成。
- **ダウンロード失敗**: `DownloadSubtitleAsync` が `null` を返し、同様に翻訳フォールバック。
- **429 / 5xx**: `ExecuteWithRetryAsync` で最大3回、2s → 4s → 8s のバックオフでリトライ。

---

## 12. 関連ファイル一覧

| ファイル | 役割 |
|----------|------|
| `Providers/OpenSubtitlesClient.cs` | 認証・検索・ダウンロード、リトライ、JSON モデル |
| `Configuration/PluginConfiguration.cs` | OpenSubtitles のユーザー名・パスワード・APIキー |
| `Configuration/configPage.html` | OpenSubtitles 設定 UI |
| `ScheduledTasks/GenerateJapaneseLearningSubtitlesTask.cs` | タスク実行、`TryFetchOpenSubtitlesJapanese`、メタデータ取得と検索/DL の呼び出し |
| `PluginServiceRegistrator.cs` | `HttpClient("OpenSubtitles")` 登録 |
| `Alignment/SubtitleAligner.cs` | OpenSubtitles 取得日本語字幕の英語タイムコードへのアライン |

---

## 13. 参考リンク

- [OpenSubtitles](https://www.opensubtitles.com/) — アカウント・API キー取得
- [OpenSubtitles API Docs](https://opensubtitles.stoplight.io/docs/opensubtitles-api) — REST API 仕様
