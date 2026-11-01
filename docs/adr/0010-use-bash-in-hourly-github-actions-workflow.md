---
status: accepted
date: 2026-11-01
deciders: yuta
---

# ADR-0010: Use Bash in hourly GitHub Actions workflow

## Context and Problem Statement

[ADR-0009](0009-use-ubuntu-for-hourly-github-actions-execution.md)でrunnerをubuntu-latestへ変更し、PowerShell 7を維持した。ユーザーが「ワークフロー内のスクリプトを pwsh から bash (shell) に変更」と明示した。SDK確認・出力準備とJob summary処理はPowerShell構文で実装されている。

## Decision Drivers

- Workflow内のshellとスクリプトをBashへ変更するという明示要件。
- Ubuntu runner、既存の実行コマンド、終了コード、Job summaryの検証を維持する。

## Considered Options

- Workflow内のshellとスクリプトをBashへ変更する

## Decision Outcome

選択肢: **"Workflow内のshellとスクリプトをBashへ変更する"**

理由: ユーザーがshellの変更を明示したため。jobの既定shellをbashとし、SDK確認・出力準備とJob summary処理をBashに書き換える。runnerはubuntu-latest、アプリはC# file-based appのままとする。

.NET SDK 10.0系の安定版10.0.300以上を選択し、SDK準備の失敗時は非0終了して既存出力を削除しない。Summaryは変換成功時のみ生成Markdownを掲載する。対象ファイルが1件であること、空または空白のみでないこと、UTF-8ファイルが1 MiB以内であることを検証する。変換失敗時とSummary生成失敗時のメッセージを維持する。

schedule、手動実行、default branch制限、環境変数による接続設定、OIDC認証、実行コマンド、アプリ本体は変更しない。

### Consequences

- 良い点: Workflow内のshellとスクリプトがユーザー指定のBashに統一される。
- 悪い点: PowerShellの例外処理・文字列・ファイル操作をBashとUbuntuの標準コマンドで実装し直す必要がある。

## Validation

YAMLとBash構文を検証し、SDKの境界値・失敗終了コード、Summaryの成功・失敗・ファイル件数・Unicode空白・UTF-8サイズ境界をLinux上で確認する。実GitHub ActionsでのOIDC認証とFoundry呼び出しは別途確認する。

## Pros and Cons of the Options

### Workflow内のshellとスクリプトをBashへ変更する

- 良い点: Ubuntu runner上のWorkflowを指定されたshellで実行できる。
- 中立: C#アプリ、接続設定、OIDC認証は維持する。
- 悪い点: 引数の引用と失敗時の終了コード・Summary処理を再検証する必要がある。

## More Information

Supersedes [ADR-0009](0009-use-ubuntu-for-hourly-github-actions-execution.md)。Ubuntu runnerと毎時実行のDecisionを引き継ぎ、shellをBashへ変更する。[ADR-0006](0006-publish-markdown-in-workflow-job-summaries.md)、[ADR-0007](0007-use-github-oidc-for-foundry-authentication.md)、[ADR-0008](0008-restore-environment-based-foundry-configuration.md)のDecisionは維持する。
