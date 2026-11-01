---
status: accepted
date: 2026-11-01
deciders: yuta
---

# ADR-0001: Use CSharp file-based app

## Context and Problem Statement

RSSの記事を分類・要約してMarkdownへ出力するCLIアプリを新規作成する。ユーザーはC# file-based appを明示的に指定した。

## Decision Drivers

- C# file-based appというユーザー指定を満たす。

## Considered Options

- C# file-based app

## Decision Outcome

選択肢: **"C# file-based app"**

理由: ユーザーがこの実装形式を指定したため。

### Consequences

- 良い点: 本体のcsprojを作成せず、dotnet CLIでソースファイルから実行できる。
- 悪い点: 対応する.NET SDKが必要。複数ファイルのincludeを使う場合はSDK 10.0.300以降が必要になる。

## Validation

本体にcsprojを作成せず、dotnet CLIでentry pointの.csファイルをbuild・実行できることを確認する。

## Pros and Cons of the Options

### C# file-based app

- 良い点: 本体のプロジェクトファイルが不要。
- 中立: ユーザーが指定した方式。他方式との比較は行っていない。
- 悪い点: 対応SDKが必要。

## More Information

[ADR-0002](0002-use-foundry-for-article-analysis.md)と[ADR-0003](0003-use-entra-id-for-foundry-authentication.md)も適用する。
