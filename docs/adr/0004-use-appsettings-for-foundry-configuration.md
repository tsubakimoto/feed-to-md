---
status: superseded
date: 2026-11-01
deciders: yuta
---

# ADR-0004: Use appsettings for Foundry configuration

## Context and Problem Statement

[ADR-0003](0003-use-entra-id-for-foundry-authentication.md)では既存ProjectのendpointとモデルDeployment名を環境変数で指定していた。ユーザーが「設定を永続化したいから appsettings.json での指定に変更」と明示した。

## Decision Drivers

- endpointとDeployment名の設定をファイルに永続化する。
- 既存ProjectとEntra ID認証は引き続き使用する。

## Considered Options

- appsettings.jsonでの指定

## Decision Outcome

選択肢: **"appsettings.jsonでの指定"**

理由: ユーザーが設定の永続化を目的として指定したため。endpointとDeployment名の環境変数参照をappsettings.jsonに置き換える。DefaultAzureCredentialによるEntra ID認証、APIキーを保管しない方針、Azureリソース構築を対象外とする方針は維持する。

### Consequences

- 良い点: endpointとDeployment名を実行間で保持できる。
- 悪い点: 実行時に設定ファイルが必要となり、ファイル欠損・JSON不正・必須値不足はエラー終了となる。

## Validation

appsettings.jsonからendpointとDeployment名を読み込み、従来のURL・Deployment検証を適用する。endpoint・Deploymentの環境変数へのfallbackがないことと、DefaultAzureCredentialの維持を確認する。

## Pros and Cons of the Options

### appsettings.jsonでの指定

- 良い点: 設定がファイルに永続化される。
- 中立: 認証方式は変更しない。
- 悪い点: 設定ファイルの管理が必要となる。

## More Information

Superseded by [ADR-0008](0008-restore-environment-based-foundry-configuration.md)。

Supersedes [ADR-0003](0003-use-entra-id-for-foundry-authentication.md)。旧Decisionの環境変数による接続設定のみ変更し、認証方針は維持する。

読み込み位置は実装上cwdとする。これは応答不在時のAgentによる実装判断であり、ユーザーが読み込み位置を選択したDecisionとしては扱わない。
