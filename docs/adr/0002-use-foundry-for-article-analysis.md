---
status: accepted
date: 2026-11-01
deciders: yuta
---

# ADR-0002: Use Foundry for article analysis

## Context and Problem Statement

RSSから取得した各記事を技術トピックごとに分類し、記事本文に基づく日本語要約を生成する。ユーザーはMicrosoft FoundryのモデルとAzure SDKを指定した。

## Decision Drivers

- Azure SDKを使用する。
- ローカルでのモデル運用を避ける。

## Considered Options

- ローカルLLM（Ollama等）で生成
- 外部LLM APIで生成（利用サービスを指定）
- LLMを使わず本文抽出・ルールベースで生成
- Microsoft Foundryのモデルで生成。Azure SDKを使用

## Decision Outcome

選択肢: **"Microsoft Foundryのモデルで生成。Azure SDKを使用"**

理由: ユーザーがAzure SDKの利用とモデル運用の回避を判断理由として明示したため。

### Consequences

- 良い点: ローカルのモデル実行環境を管理せずAzure SDKで推論できる。
- 悪い点: Azureへのネットワーク接続、既存モデルDeploymentと推論料金が必要。取得した公開記事本文をFoundryへ送信する。

## Validation

Azure SDKから設定された既存Deploymentを呼び出して分類・要約を生成する。認証・接続設定がない環境では実モデルによる検証が未実施であることを明示する。

## Pros and Cons of the Options

### ローカルLLM（Ollama等）で生成

- 良い点: 外部への本文送信を抑えられる。
- 中立: ローカルでモデルを実行する方式。
- 悪い点: モデル実行環境の運用が必要。

### 外部LLM APIで生成（利用サービスを指定）

- 良い点: ローカルのモデル運用を避けられる。
- 中立: 利用するサービスの指定が必要。
- 悪い点: ネットワーク接続と推論料金が必要。

### LLMを使わず本文抽出・ルールベースで生成

- 良い点: モデル推論料金を避けられる。
- 中立: 本文抽出とルールに基づく生成。
- 悪い点: 意味的な要約・分類に制約がある。

### Microsoft Foundryのモデルで生成。Azure SDKを使用

- 良い点: Azure SDK要件を満たし、ローカルのモデル運用を避けられる。
- 中立: 既存ProjectとDeploymentを使用する。
- 悪い点: Azure接続と推論料金が必要。

## More Information

[Microsoft Foundry SDKs and endpoints](https://learn.microsoft.com/en-us/azure/foundry/how-to/develop/sdk-overview)。認証は[ADR-0003](0003-use-entra-id-for-foundry-authentication.md)に従う。
