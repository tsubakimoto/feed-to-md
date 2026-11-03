---
status: superseded
date: 2026-11-01
deciders: yuta
---

# ADR-0006: Publish Markdown in workflow job summaries

## Context and Problem Statement

毎時実行するWorkflowでは、runner上で生成したMarkdownをどこへ公開するか決める必要がある。ArtifactとRepositoryへのcommitを提示したところ、ユーザーが「ワークフローのjob summary」を指定した。

## Decision Drivers

- 生成MarkdownをJob summaryに掲載するというユーザーの明示指定。
- ArtifactアップロードとRepositoryへのcommitは追加しない。

## Considered Options

- GitHub Actions Artifactに保存（推奨）
- Repositoryのoutputフォルダへcommit
- ワークフローのjob summary

## Decision Outcome

選択肢: **"ワークフローのjob summary"**

理由: ユーザーが公開先を指定したため。生成MarkdownをGITHUB_STEP_SUMMARYへ書き込み、Workflow runの結果ページで表示する。今回生成したファイルだけを対象にし、全体失敗時は成功結果として掲載しない。

### Consequences

- 良い点: MarkdownをWorkflow runの結果ページで直接確認できる。Repositoryへの書き込み権限が不要。
- 悪い点: Markdownファイル自体は保存されず、runnerの終了時に破棄される。SummaryはWorkflow runに依存し、runの削除で失われる。step当たり1 MiBの上限がある。

## Validation

synthetic Markdownを使ってUTF-8の内容一致、ファイル欠損・サイズ超過の検出、失敗時の成功結果抑止をローカル確認する。実際の表示はGitHub上で確認する。

## Pros and Cons of the Options

### GitHub Actions Artifactに保存（推奨）

- 良い点: Repositoryへの書き込み権限が不要。
- 悪い点: 保存期限がある。

### Repositoryのoutputフォルダへcommit

- 良い点: MarkdownファイルをRepositoryの履歴として保持できる。
- 悪い点: Repositoryへの書き込み権限が必要。

### ワークフローのjob summary

- 良い点: 実行結果ページでMarkdownを直接表示できる。
- 悪い点: ファイルの長期保存手段ではない。step当たりのサイズ上限がある。

## More Information

Superseded by [ADR-0011](0011-save-markdown-as-workflow-artifacts.md)。

毎時実行は[ADR-0005](0005-use-github-actions-for-hourly-execution.md)で記録する。

[GitHub Actions job summaries](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-commands#adding-a-job-summary)
