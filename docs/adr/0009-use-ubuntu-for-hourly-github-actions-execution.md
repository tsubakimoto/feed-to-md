---
status: superseded
date: 2026-11-01
deciders: yuta
---

# ADR-0009: Use Ubuntu for hourly GitHub Actions execution

## Context and Problem Statement

[ADR-0005](0005-use-github-actions-for-hourly-execution.md)では毎時実行にWindows hosted runnerを使用した。ユーザーが「ワークフローのランナーを ubuntu-latest に変更」と明示した。現在のWorkflowはPowerShell 7を使用し、実行コマンドにWindows形式の相対パスを渡している。

## Decision Drivers

- Workflowのrunnerをubuntu-latestへ変更するという明示要件。
- 接続設定、OIDC認証、毎時実行、Job summaryの既存動作を維持する。

## Considered Options

- Workflowのrunnerをubuntu-latestへ変更する

## Decision Outcome

選択肢: **"Workflowのrunnerをubuntu-latestへ変更する"**

理由: ユーザーがrunnerの変更を明示したため。GitHub-hosted runnerをubuntu-latestへ変更し、PowerShell 7は維持する。実行コマンドは`dotnet run --file app.cs -- --count 10 --output-dir output`とし、OS依存のパス区切りを渡さない。これはrunner変更に伴う実装上の対応であり、独立したUser Decisionではない。

既存の毎時schedule、手動実行、default branch制限、対応.NET SDKの選択、環境変数による接続設定、OIDC認証、Job summaryへの結果公開は変更しない。アプリ本体も変更しない。

### Consequences

- 良い点: ユーザーが指定したUbuntu runnerで定期実行できる。
- 悪い点: Windows前提のパス指定をLinuxでも解釈できる形に合わせる必要がある。実際のrunner上での認証・変換の確認は別途必要。

## Validation

WorkflowのYAML、runner指定、PowerShell構文、実行コマンドのパス、既存schedule・認証・Job summaryの維持をローカルで確認する。実Ubuntu runnerでの実行はGitHub Actions上で確認する。

## Pros and Cons of the Options

### Workflowのrunnerをubuntu-latestへ変更する

- 良い点: runner指定がユーザー要件と一致する。
- 中立: shellはPowerShell 7、アプリはC# file-based appのままとする。
- 悪い点: Linuxでのパス解釈に対応する必要がある。

## More Information

Superseded by [ADR-0010](0010-use-bash-in-hourly-github-actions-workflow.md)。Ubuntu runnerは維持し、shellをBashへ変更する。

Supersedes [ADR-0005](0005-use-github-actions-for-hourly-execution.md)。毎時実行のDecisionを引き継ぎ、runnerをUbuntuへ変更する。[ADR-0006](0006-publish-markdown-in-workflow-job-summaries.md)、[ADR-0007](0007-use-github-oidc-for-foundry-authentication.md)、[ADR-0008](0008-restore-environment-based-foundry-configuration.md)のDecisionは維持する。
