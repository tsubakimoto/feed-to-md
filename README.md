# feed-to-md

はてなブックマークのIT人気エントリーを取得し、記事本文をMicrosoft Foundryのモデルで分類・要約してMarkdownへ保存するC# file-based CLI app。

## 前提

- .NET SDK 10.0.300以降と.NET 10 runtime（helper sourceの`#:include`を使用）。
- PowerShell 7。
- 既存Microsoft Foundry Projectと、テキスト生成が可能なモデルDeployment。Responses API対応が必要。
- 実行者のMicrosoft Entra ID認証と、モデルを呼び出せるProject権限。アプリはAzureリソースや権限を作成しない。

## 設定・実行

`DefaultAzureCredential`で認証する。開発環境では事前にAzure CLI等で対象Tenantへサインインしておく。APIキーは使用しない。

既存Projectの接続設定を環境変数で指定する。

```powershell
$env:AZURE_AI_PROJECT_ENDPOINT = 'https://<resource>.services.ai.azure.com/api/projects/<project>'
$env:AZURE_AI_MODEL_DEPLOYMENT_NAME = '<existing-deployment-name>'
dotnet run --file .\app.cs -- --count 10 --output-dir .\output
```

設定は起動ごとに環境変数から読み直す。cwdや`--output-dir`は接続設定の読み込みに影響しない。`appsettings.json`は廃止し、ファイルが残っていても読み込み・fallbackを行わない。未設定・空値・不正なendpoint/Deploymentは非0終了する。

上記の`$env:`指定は現在のPowerShell sessionと子processに適用される。アプリ自体は環境変数を永続化しない。`DefaultAzureCredential`が認証に使う環境変数等の仕組みは変更しない。これらの接続設定はAPIキー・password・tokenではない。

フィードは`https://b.hatena.ne.jp/hotentry/it.rss`を使用する。endpointはProject endpoint、Deployment名は既存Deploymentの名前を設定する。

```powershell
dotnet run --file .\app.cs -- --help
dotnet build .\app.cs -v:q
```

| 引数 | 動作 |
|---|---|
| `--count N` | 先頭から最大N件。正の整数。既定10件。フィード残数が少ない場合は存在する件数のみ処理 |
| `--output-dir PATH` | 出力基点。既定は作業ディレクトリ。相対パスも作業ディレクトリ基準 |
| `--help` | ヘルプ。Azure設定・認証は不要 |

NuGet依存はsourceの`#:package`にversionを固定して宣言する。初回build/runでrestoreされる。

## 出力

実行開始時のAsia/Tokyo日時を使い、`yyyy\MM\yyyyMMddHHmm.md`へUTF-8で保存する。`HH`は24時間表記。同じ分の同名ファイルは上書きせずエラー終了する。

例: `output\2026\11\202611012135.md`

```markdown
# 2026-11-01

## AI・機械学習

### 記事タイトル

<https://example.com/article>

本文の要点を日本語の短い段落でまとめた要約。
```

固定カテゴリは次の順序で出力する。空カテゴリは省略し、カテゴリ内はフィード順を維持する。各記事は1カテゴリにのみ分類する。

1. AI・機械学習
2. 開発
3. Web
4. Cloud・Infrastructure
5. Security
6. Data・DB
7. Hardware
8. その他

要約は200〜400字を目安とする。記事タイトル・URLはフィード由来、カテゴリと要約は取得した本文に基づくモデル出力。モデルが本文を正しく解釈する保証はないため、重要な内容はリンク先で確認する。

## 失敗時の動作・制約

- 個別記事の取得・要約失敗: タイトルとURLを残して`要約取得失敗`と表示。分類に失敗した場合は「その他」。失敗記事を別記事で補充しない。
- 設定・引数不足、フィード取得・解析失敗、認証・権限エラー、同名ファイル、保存失敗: 非0終了。
- 記事HTMLの本文をアプリ側で取得・抽出し、公開本文をFoundryに送信する。URLだけをモデルに渡して閲覧したとみなす方式ではない。推論料金が発生する。
- JavaScriptレンダリング、PDF抽出、ログイン・paywall・アクセス制限の回避は行わない。本文が取得できなければ要約を生成しない。
- 記事本文・認証tokenはログへ出力しない。Foundryには応答の保存無効を指定する。
- 取得上限は展開後2 MiB、取得timeoutは30秒、redirectは5回まで。モデル入力は20,000文字まで、生成timeoutは90秒、出力は3,000 tokensまで。本文を切り詰めた場合は警告する。
- 見出し以外の本文が100文字未満の場合は要約しない。paywallの代表的なHTML属性・構造を検出して除外するが、全サイトのアクセス制限を自動判定できるわけではない。
- HTTP(S)の80/443番portのみ取得し、localhost・private networkへのアクセスやredirectを拒否する。DNS解決結果の公開IPへ直接接続し、再解決によるprivate networkへの切り替えを防ぐ。記事取得はproxyを使用しないため、proxy必須環境では取得に失敗する。
- 逐次処理。アプリ自体にschedulerはなく、定期実行は以下のGitHub Actionsで行う。永続キャッシュ、複数フィード、Azure構築は対象外。

## GitHub Actionsで毎時実行

[hourly-feed-to-md.yml](.github/workflows/hourly-feed-to-md.yml)はUbuntu hosted runner（`ubuntu-latest`）とBash（`bash`）で、次のコマンドを毎時0分に実行する。cronはUTCだが、JSTでも毎時0分となる。

```bash
dotnet run --file app.cs -- --count 10 --output-dir output
```

.NET SDK 10.0.xの最新GAを導入し、10.0.300以上であることを確認する。runner内に一時global.jsonを作り、他のプリインストールSDKではなく選択したSDKを使用する。実行時間上限は45分。同時実行を抑止し、実行中のjobを取り消さない。

### 事前設定

1. Repositoryの **Settings → Secrets and variables → Actions → Variables** に次のRepository variablesを登録する。Workflowはこれらを同名の環境変数としてアプリへ渡す。未設定・空値では失敗する。

   | Variable | 内容 |
   |---|---|
   | `AZURE_AI_PROJECT_ENDPOINT` | 既存Foundry Projectのendpoint |
   | `AZURE_AI_MODEL_DEPLOYMENT_NAME` | 既存モデルDeployment名 |
2. 既存Entra ID ApplicationのService Principal、またはuser-assigned Managed Identityに、対象Repositoryのdefault branchに限定したFederated Credentialを準備する。Azure側の永続設定・権限は別途IaC等で管理する。

   | Federated Credentialの項目 | 値 |
   |---|---|
   | Issuer | `https://token.actions.githubusercontent.com` |
   | Subject | `repo:<owner>/<repo>:ref:refs/heads/<default-branch>` |
   | Audience | `api://AzureADTokenExchange` |

   `<owner>`、`<repo>`、`<default-branch>`は実際の値に置き換える。WorkflowにはGitHub Environmentを指定していない。OIDC subjectをRepositoryでカスタマイズしている場合は、そのsubjectに一致する設定が必要。
3. 同じIdentityに、既存Foundry Projectでモデルを呼び出せる最小限の権限を用意する。Azureへのlogin成功だけではFoundryのdata-plane権限を確認したことにならない。GitHub-hosted runnerからProject endpointへ通信できることも必要。
4. Repositoryの **Settings → Secrets and variables → Actions → Repository secrets** に次を登録する。

   | Secret | 内容 |
   |---|---|
   | `AZURE_CLIENT_ID` | Applicationまたはuser-assigned Managed Identityのclient ID |
   | `AZURE_TENANT_ID` | 対象TenantのID |
   | `AZURE_SUBSCRIPTION_ID` | 対象SubscriptionのID |

`azure/login`がOIDCでAzure CLIへloginし、アプリの`DefaultAzureCredential`がその資格情報を使用する。client secret、APIキー、認証tokenをWorkflowに保存しない。Workflowの権限は`contents: read`と`id-token: write`のみ。

この作業ではAzureリソース・Federated Credential・RBAC・GitHub Secrets・Repository variablesの作成や変更は行わない。

### 実行結果・確認

Workflowをdefault branchへ配置するとscheduleが有効になる。初回確認は **Actions → Hourly feed to Markdown → Run workflow** でdefault branchを選んで手動実行する。別branchでの手動実行はjobをskipする。

生成したMarkdownはWorkflow runの **Summary** ページに掲載する。runner内のoutputは実行前に初期化し、今回の生成ファイル1件だけを掲載する。全体失敗時は失敗表示とし、jobの失敗を隠さない。個別記事の要約失敗は既存アプリと同じくMarkdownに残る。

ArtifactアップロードやRepositoryへのcommitは行わない。Markdownファイル自体はrunner終了時に破棄され、SummaryもWorkflow runを削除すると失われる。Summaryの1 MiB上限を超える出力や、出力欠損・空ファイルは掲載stepを失敗させる。

GitHub scheduleは遅延・欠落する場合があり、厳密な実行時刻・実行間隔を保証しない。public Repositoryでは60日間活動がないとscheduleが自動無効化される。ローカルの構文・Summary検証では、実OIDC・Foundry接続・定期実行の成功までは確認できない。

- [GitHub Actions schedule](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)
- [GitHub Actions job summaries](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#adding-a-job-summary)
- [Azure Login OIDC](https://github.com/Azure/login#login-with-openid-connect-oidc-recommended)

## 検証

追加のtest frameworkを必要としない、C# file-basedのoffline test runnerを使用する。`dotnet test`ではなく次のコマンドで実行する。

```powershell
dotnet run --file .\tests\FeedToMarkdownTests.cs
dotnet run --file .\tests\FeedToMarkdownTests.cs -- --filter MarkdownDocument.
dotnet run --file .\tests\FeedToMarkdownTests.cs -- --list-tests
```

失敗時は非0終了。HTTPはfake handler、モデル分析はdelegateで置き換え、保存は一時directoryで検証する。Azure認証や実推論は呼び出さない。

| 要件 | Test evidence（代表例） |
|---|---|
| 環境変数から接続設定を読み込み・再読込・trim | `FoundryConfiguration.FromEnvironment.ValidAndReload`, `FoundryConfiguration.FromEnvironment.Trim` |
| 設定なしで非0終了・appsettingsへのfallbackなし・helpは設定不要 | `Application.EnvironmentMissing`, `Application.EnvironmentMissingIgnoresSettingsFile`, `Application.HelpWithoutEnvironment` |
| 不正値の検出・安全な診断・残存JSONを無視 | `FoundryConfiguration.FromEnvironment.EndpointValidation`, `FoundryConfiguration.FromEnvironment.DeploymentValidation`, `FoundryConfiguration.FromEnvironment.IgnoresSettingsFile`, `Application.EnvironmentInvalidIgnoresSettingsFile` |
| 既定10件・件数変更 | `CliOptions.Defaults[explicit-directory]`, `CliOptions.PositiveCount[1]` |
| RSSタイトル・URLとRDF記事順、件数不足 | `FeedParser.SequenceOrder[count-exact]`, `FeedParser.SequenceOrder[count-shortfall]` |
| HTML本文抽出・本文の取りこぼし防止 | `ArticleExtractor.StructuralBodyPreservation[mixed-div-and-paragraph]`, `ArticleExtractor.StructuralBodyPreservation[multiple-body-blocks]`, `ArticleExtractor.TextLayout[br-p]` |
| 日本語要約の応答検証・カテゴリ分類 | `AnalysisParser.Japanese[hiragana]`, `AnalysisParser.ValidCategory[ai]`, `AnalysisParser.SummaryReject[non-japanese]` |
| 固定カテゴリ順・h1/h2/h3・記事順・タイトル/URL/要約 | `MarkdownDocument.AllCategoriesFullOracle`, `MarkdownDocument.MultiArticleFullOracle` |
| JST・24時間のファイル名、日付境界 | `MarkdownDocument.TimeAndPath[hour13]`, `MarkdownDocument.TimeAndPath[utc1500]`, `MarkdownDocument.TimeAndPath[year-rollover]` |
| UTF-8保存・上書き禁止・一時ファイルcleanup | `MarkdownDocument.SaveUtf8Nested[japanese-emoji]`, `MarkdownDocument.SaveNoOverwrite`, `MarkdownDocument.SaveMoveFailureCleanup` |
| 個別失敗の記事保持と処理継続 | `ArticlePipeline.MultiArticleOrder`, `ArticlePipeline.IndividualFailure[analyze-derived-json]`, `MarkdownDocument.PipelineToFullOracle` |
| 解析から保存までのoffline連携 | `MarkdownDocument.PipelineRenderSaveIntegration` |
| キャンセル時の通信抑止・終了 | `PublicHttpDownloader.Cancellation[pre-canceled]`, `ArticlePipeline.Cancellation[during-analyze]`, `MarkdownDocument.SavePreCanceled` |

実RSSの取得・解析と2記事の公開HTML取得・本文抽出も確認済み。実Foundryでの分類・要約は未検証。上記の応答検証・offline連携は、実モデルの精度やDeployment互換性を保証するものではない。

## 設計判断

- [ADR-0001: C# file-based app](docs/adr/0001-use-csharp-file-based-app.md)
- [ADR-0002: Foundryによる分類・要約](docs/adr/0002-use-foundry-for-article-analysis.md)
- [ADR-0003: Entra ID認証（superseded）](docs/adr/0003-use-entra-id-for-foundry-authentication.md)
- [ADR-0004: appsettingsによる接続設定（superseded）](docs/adr/0004-use-appsettings-for-foundry-configuration.md)
- [ADR-0005: GitHub Actionsで毎時実行（superseded）](docs/adr/0005-use-github-actions-for-hourly-execution.md)
- [ADR-0006: Job summaryでMarkdownを公開](docs/adr/0006-publish-markdown-in-workflow-job-summaries.md)
- [ADR-0007: GitHub OIDCによるFoundry認証](docs/adr/0007-use-github-oidc-for-foundry-authentication.md)
- [ADR-0008: 環境変数による接続設定へ戻す](docs/adr/0008-restore-environment-based-foundry-configuration.md)
- [ADR-0009: Ubuntu runnerで毎時実行（superseded）](docs/adr/0009-use-ubuntu-for-hourly-github-actions-execution.md)
- [ADR-0010: Workflow内のshellをBashへ変更](docs/adr/0010-use-bash-in-hourly-github-actions-workflow.md)

[Microsoft Foundry SDKs and endpoints](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/sdk-overview)
