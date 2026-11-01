---
status: accepted
date: 2026-11-01
deciders: yuta
---

# ADR-0008: Restore environment-based Foundry configuration

## Context and Problem Statement

[ADR-0004](0004-use-appsettings-for-foundry-configuration.md)では既存Foundry Projectの接続設定をappsettings.jsonから読み込む方式に変更した。ユーザーが「appsettings.json を廃止し、環境変数での指定に戻す」と明示した。GitHub Actionsも現在はRepositoryのappsettings.jsonを使用している。

## Decision Drivers

- appsettings.jsonを廃止し、従来の環境変数指定に戻すという明示要件。
- 接続設定の入力元のみ変更し、既存の検証と認証を維持する。

## Considered Options

- appsettings.jsonを廃止し、環境変数での指定に戻す

## Decision Outcome

選択肢: **"appsettings.jsonを廃止し、環境変数での指定に戻す"**

理由: ユーザーが変更を明示したため。Project endpointはAZURE_AI_PROJECT_ENDPOINT、モデルDeployment名はAZURE_AI_MODEL_DEPLOYMENT_NAMEから起動ごとに取得する。appsettings.jsonの読み込みとfallbackを廃止し、同梱ファイルを削除する。DefaultAzureCredentialとGitHub ActionsのOIDC認証は変更しない。

### Consequences

- 良い点: 設定ファイルなしで実行でき、GitHub Actionsでも環境変数を渡せる。
- 悪い点: 実行環境で環境変数の設定が必要。未設定・空値・不正値は非0終了する。アプリ自体は環境変数を永続化しない。

## Validation

環境変数の読み込み・再読込、既存のendpointとDeployment検証、未設定時の非0終了、appsettings.jsonが残っていても参照しないこと、helpが設定なしで動作することをoffline testsで確認する。Workflowが同じ環境変数を渡すことも確認する。

## Pros and Cons of the Options

### appsettings.jsonを廃止し、環境変数での指定に戻す

- 良い点: 接続設定の入力元を環境変数に統一できる。
- 中立: Entra ID認証と既存の入力検証は維持する。
- 悪い点: 実行環境ごとに環境変数の設定が必要。

## More Information

Supersedes [ADR-0004](0004-use-appsettings-for-foundry-configuration.md)。[ADR-0005](0005-use-github-actions-for-hourly-execution.md)と[ADR-0007](0007-use-github-oidc-for-foundry-authentication.md)の接続設定に関するappsettings参照も本ADRに従う。毎時実行、Job summary、OIDC認証のDecisionは維持する。

GitHub Actionsでは非secretの接続設定をRepository variablesからjobの環境変数へ渡す。これは入力元の変更に伴う実装上の対応であり、ユーザーがGitHubでの保管先を独立したDecisionとして選択したことは意味しない。
