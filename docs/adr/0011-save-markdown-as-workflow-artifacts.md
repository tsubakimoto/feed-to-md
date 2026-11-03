---
status: accepted
date: 2026-11-03
deciders: yuta
---

# ADR-0011: Save Markdown as workflow artifacts

## Context and Problem Statement

[ADR-0006](0006-publish-markdown-in-workflow-job-summaries.md)に従い、生成MarkdownをJob summaryへ掲載している。ユーザーが「ジョブサマリーへの保存から、Artifactsへの保存に変更する」と明示した。

## Decision Drivers

- 生成Markdownの保存先をArtifactsへ変更するという明示要件。
- 今回生成したファイルだけを対象とし、全体失敗時は成功結果として公開しないという既存の契約。

## Considered Options

- ジョブサマリーへの保存から、Artifactsへの保存に変更する

## Decision Outcome

選択肢: **"ジョブサマリーへの保存から、Artifactsへの保存に変更する"**

理由: ユーザーが保存先の変更を明示したため。Job summaryへのMarkdown書き込みを廃止し、生成したMarkdownファイルをGitHub Actions Artifactとして保存する。Repositoryへのcommitは行わない。

### Consequences

- 良い点: runner終了後もMarkdownファイルをダウンロードできる。Job summary固有の1 MiB制限は不要になる。Repositoryへの書き込み権限は不要。
- 悪い点: Artifactには保存期限がある。Job summaryでのMarkdown直接表示はなくなる。

## Validation

WorkflowのYAMLとアップロードActionの入力を確認する。変換成功時のみ検証・アップロードすること、生成ファイル1件・非空の検証、日付ディレクトリの保持、Job summaryへの書き込みと1 MiB制限の撤去を確認する。Bash検証はsynthetic Markdownで実行し、実GitHubへのアップロードは別途確認する。

## Pros and Cons of the Options

### ジョブサマリーへの保存から、Artifactsへの保存に変更する

- 良い点: 生成Markdownをファイルとして取得できる。
- 中立: アプリ本体、接続設定、OIDC認証は変更しない。
- 悪い点: Artifactの保存期限を過ぎると取得できなくなる。

## More Information

Supersedes [ADR-0006](0006-publish-markdown-in-workflow-job-summaries.md)。[ADR-0010](0010-use-bash-in-hourly-github-actions-workflow.md)のBash実行は維持し、Job summary処理のみArtifacts保存へ置き換える。

[GitHub Actions upload-artifact](https://github.com/actions/upload-artifact)
