---
status: superseded
date: 2026-11-01
deciders: yuta
---

# ADR-0003: Use Entra ID for Foundry authentication

## Context and Problem Statement

ローカルCLIアプリから既存Microsoft Foundry Projectのモデルを呼び出す。接続設定と認証方式を決める必要がある。Azureリソース構築は本アプリの対象外とすることをユーザーが選択した。

## Decision Drivers

- APIキーの保管を避ける。
- 既存Projectを使用する。

## Considered Options

- 既存Project＋環境変数＋Entra ID（推奨）
- 既存endpoint＋APIキー認証
- Azureリソースの構築も対象に含める

## Decision Outcome

選択肢: **"既存Project＋環境変数＋Entra ID（推奨）"**

理由: ユーザーがAPIキー保管の回避を判断理由として明示したため。Project endpointとモデルDeployment名は環境変数で指定し、Azure SDKのDefaultAzureCredentialを使用する。

### Consequences

- 良い点: アプリでAPIキーを取得・保管する必要がない。
- 悪い点: 実行者のEntra ID認証と既存Projectに対する必要な権限が前提となる。アプリはリソース作成や権限付与を行わない。

## Validation

環境変数とDefaultAzureCredentialを使用し、APIキー認証を実装しないことを確認する。

## Pros and Cons of the Options

### 既存Project＋環境変数＋Entra ID（推奨）

- 良い点: APIキー保管を避けられる。
- 中立: 既存Projectと接続設定を使用する。
- 悪い点: 認証と権限設定が必要。

### 既存endpoint＋APIキー認証

- 良い点: 既存endpointを使用できる。
- 中立: APIキーで認証する。
- 悪い点: キーの取得・保管・管理が必要。

### Azureリソースの構築も対象に含める

- 良い点: 新規環境を用意できる。
- 中立: Infrastructure変更を伴う。
- 悪い点: アプリ作成からInfrastructure構築へScopeが広がる。

## More Information

[ADR-0002](0002-use-foundry-for-article-analysis.md)のモデル呼び出しに適用する。

Superseded by [ADR-0004](0004-use-appsettings-for-foundry-configuration.md)。接続設定をappsettings.jsonへ移行し、Entra ID認証は維持する。
