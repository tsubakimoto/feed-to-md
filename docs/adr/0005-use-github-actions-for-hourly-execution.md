---
status: accepted
date: 2026-11-01
deciders: yuta
---

# ADR-0005: Use GitHub Actions for hourly execution

## Context and Problem Statement

RSSからMarkdownを生成するC# file-based appは、手動で実行するコマンドを提供している。ユーザーが`dotnet run --file .\app.cs -- --count 10 --output-dir .\output`を1時間に1回実行するGitHub Actions Workflowを要求した。

## Decision Drivers

- 既存コマンドをGitHub Actionsで毎時実行するという明示要件。
- C# file-based appとappsettingsによる接続設定を維持する。

## Considered Options

- GitHub Actionsで1時間に1回実行する

## Decision Outcome

選択肢: **"GitHub Actionsで1時間に1回実行する"**

理由: ユーザーが実行基盤と実行間隔を指定したため。承認済みPlanに従いWindows hosted runnerとpwshを使用し、毎時17分のscheduleと手動実行を用意する。アプリ本体を変更せず、対応する.NET SDKをrunnerに導入する。

### Consequences

- 良い点: GitHub Actions上で既存アプリを定期実行できる。
- 悪い点: scheduleはdefault branchへの配置が必要であり、GitHubの負荷による遅延・欠落がある。厳密な時刻や実行間隔を保証しない。public Repositoryでは60日間の活動停止によりscheduleが自動無効化される。

## Validation

Workflow構文とPowerShellをローカル検証する。default branchへ配置し、必要な認証設定を準備した後、手動・定期実行をGitHub上で確認する。

## Pros and Cons of the Options

### GitHub Actionsで1時間に1回実行する

- 良い点: 既存CLIを変更せずGitHub Actionsで定期実行できる。
- 中立: Azureの事前準備とRepositoryへのWorkflow配置は別途必要。
- 悪い点: scheduleの遅延・欠落と自動無効化に対応する必要がある。

## More Information

接続設定の入力元は[ADR-0008](0008-restore-environment-based-foundry-configuration.md)で環境変数に変更された。本ADRの毎時実行のDecisionは維持する。

[ADR-0001](0001-use-csharp-file-based-app.md)、[ADR-0004](0004-use-appsettings-for-foundry-configuration.md)の既存Decisionを維持する。結果の公開先は[ADR-0006](0006-publish-markdown-in-workflow-job-summaries.md)、CIの認証は[ADR-0007](0007-use-github-oidc-for-foundry-authentication.md)で記録する。

[GitHub Actions schedule](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#schedule)
