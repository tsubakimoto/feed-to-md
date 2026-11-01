using FeedToMarkdown;
using static TestSupport;

public static class Phase3Fixtures
{
    public static readonly DateTimeOffset Instant = new(2026, 10, 31, 15, 5, 0, TimeSpan.Zero);
    public const string Body = "この記事はソフトウェア開発で使う設計手法を説明しています。実装例を比較しながら、保守性と安全性を両立する方法を紹介します。導入時には依存関係を整理し、変更の影響を確認する必要があります。具体的な検証手順と運用上の注意点もまとめています。";
    public const string FiveArticleMarkdown =
        "# 2026-11-01\n"
        + "\n## AI・機械学習\n\n### AI入門\n\n<https://example.com/ai>\n\n学習方法を説明します。\n"
        + "\n## 開発\n\n### 開発メモ\n\n<https://example.com/dev1>\n\n実装を説明します。\n"
        + "\n### 設計ノート\n\n<https://example.com/dev2>\n\n設計を説明します。\n"
        + "\n## その他\n\n### 取得失敗\n\n<https://example.com/fail>\n\n要約取得失敗\n"
        + "\n### 分類不明\n\n<https://example.com/other>\n\n技術を説明します。\n";
    public static FeedItem[] FiveItems() =>
    [
        new("開発メモ", new("https://example.com/dev1")),
        new("AI入門", new("https://example.com/ai")),
        new("取得失敗", new("https://example.com/fail")),
        new("設計ノート", new("https://example.com/dev2")),
        new("分類不明", new("https://example.com/other"))
    ];
    public static ProcessedArticle[] FiveRecords()
    {
        var items = FiveItems();
        return [new(items[0], "開発", "実装を説明します。", false), new(items[1], "AI・機械学習", "学習方法を説明します。", false),
            new(items[2], "その他", "要約取得失敗", true), new(items[3], "開発", "設計を説明します。", false), new(items[4], "その他", "技術を説明します。", false)];
    }
    public static IReadOnlyList<ProcessedArticle> RunFive(FeedItem[] items, List<string>? trace = null, CancellationToken token = default)
    {
        Analysis[] analyses = [new("開発", "実装を説明します。"), new("AI・機械学習", "学習方法を説明します。"),
            new("その他", "使われない要約。"), new("開発", "設計を説明します。"), new("未知分類", "技術を説明します。")];
        ArticleText[] bodies = Enumerable.Range(1, 5).Select(i => new ArticleText(Body + " 記事" + i, false)).ToArray();
        var pipeline = new ArticlePipeline((item, caller) =>
        {
            int index = Array.IndexOf(items, item);
            Same(items[index], item); Equal(token, caller);
            trace?.Add("D" + (index + 1));
            if (index == 2) throw new IOException("PRIVATE_ARTICLE_SENTINEL");
            return Task.FromResult(bodies[index]);
        }, (item, text, caller) =>
        {
            int index = Array.IndexOf(items, item);
            Same(items[index], item); Same(bodies[index], text); Equal(token, caller);
            trace?.Add("A" + (index + 1));
            return Task.FromResult(analyses[index]);
        }, (index, message) =>
        {
            Equal(3, index);
            Equal("本文取得または分析に失敗しました（IOException）。", message);
            trace?.Add("W3");
        });
        return pipeline.ProcessAsync(items, token).GetAwaiter().GetResult();
    }
    public static FeedItem[] ThreeItems() => [new("前の記事", new("https://example.com/before")), new("失敗対象の記事", new("https://example.com/target")), new("後の記事", new("https://example.com/after"))];
    public static void CheckSuccess(FeedItem item, ProcessedArticle actual, string category = "開発", string summary = "実装の検証方法を説明します。")
    {
        Same(item, actual.Item); Equal(item.Title, actual.Item.Title); Equal(item.Url.AbsoluteUri, actual.Item.Url.AbsoluteUri);
        Equal(category, actual.Category); Equal(summary, actual.Summary); Equal(false, actual.Failed);
    }
    public static void CheckFailure(FeedItem item, ProcessedArticle actual)
    {
        Same(item, actual.Item); Equal(item.Title, actual.Item.Title); Equal(item.Url.AbsoluteUri, actual.Item.Url.AbsoluteUri);
        Equal("その他", actual.Category); Equal("要約取得失敗", actual.Summary); Equal(true, actual.Failed);
    }
}
