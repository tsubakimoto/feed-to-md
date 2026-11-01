---
status: accepted
date: 2026-11-01
deciders: yuta
---

# ADR-0007: Use GitHub OIDC for Foundry authentication

## Context and Problem Statement

GitHub Actions runnerで既存Foundry Projectを利用するには、対話的なloginに依存しない認証が必要となる。ユーザーはOIDCとclient secret方式の比較から、OIDCを選択した。既存アプリはDefaultAzureCredentialによるEntra ID認証を使用する。

## Decision Drivers

- ユーザーがclient secret不要のOIDC方式を選択した。
- appsettingsとDefaultAzureCredentialによる既存の接続・認証方針を維持する。
- 今回はWorkflowと設定手順のみ作成し、Azure側の変更を行わない。

## Considered Options

- OIDC（推奨、client secret不要）
- Service Principalのclient secret

## Decision Outcome

選択肢: **"OIDC（推奨、client secret不要）"**

理由: ユーザーがこの方式を明示的に選択したため。azure/loginでOIDCを使ってAzure CLIへloginし、既存DefaultAzureCredentialがその資格情報を利用する。対象Repositoryのdefault branchに信頼範囲を限定する。接続設定はRepositoryのappsettings.jsonを使用し、Identity情報はGitHub Secretsから渡す。

### Consequences

- 良い点: 長期のclient secretやAPIキーを保存せず、既存Entra ID認証をCIでも使用できる。
- 悪い点: Identity、branchに一致するFederated Credential、Foundryへの権限、GitHub Secretsの事前準備が必要。Workflowにid-token: write権限が必要。

## Validation

WorkflowのOIDC inputsと最小権限を確認する。設定完了後にGitHub上でloginとFoundry呼び出しを確認する。ローカルの構文検証を実認証成功とは扱わない。

## Pros and Cons of the Options

### OIDC（推奨、client secret不要）

- 良い点: client secretの保管が不要。
- 悪い点: Federated CredentialとFoundryへの権限が必要。

### Service Principalのclient secret

- 悪い点: client secretの保管が必要。

## More Information

接続設定の入力元は[ADR-0008](0008-restore-environment-based-foundry-configuration.md)で環境変数に変更された。本ADRのOIDC認証のDecisionは維持する。

[ADR-0004](0004-use-appsettings-for-foundry-configuration.md)のEntra ID/DefaultAzureCredentialとJSON接続設定は維持する。既存Decisionを置き換えない。

[Azure Login OIDC](https://github.com/Azure/login#login-with-openid-connect-oidc-recommended)
