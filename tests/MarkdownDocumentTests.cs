using System.Globalization;
using System.Text;
using System.Text.Json;
using FeedToMarkdown;
using static TestSupport;
using static Phase3Fixtures;

public static class MarkdownDocumentTests
{
    private const string AllCategoriesMarkdown =
        "# 2026-11-01\n"
        + "\n## AI・機械学習\n\n### 学習一\n\n<https://example.com/ai1>\n\n学習一の要約。\n"
        + "\n### 学習二\n\n<https://example.com/ai2>\n\n学習二の要約。\n"
        + "\n## 開発\n\n### 実装一\n\n<https://example.com/dev1>\n\n実装一の要約。\n"
        + "\n### 実装二\n\n<https://example.com/dev2>\n\n実装二の要約。\n"
        + "\n## Web\n\n### ウェブ\n\n<https://example.com/web>\n\nウェブの要約。\n"
        + "\n## Cloud・Infrastructure\n\n### クラウド\n\n<https://example.com/cloud>\n\nクラウドの要約。\n"
        + "\n## Security\n\n### 安全性\n\n<https://example.com/security>\n\n安全性の要約。\n"
        + "\n## Data・DB\n\n### データ\n\n<https://example.com/data>\n\nデータの要約。\n"
        + "\n## Hardware\n\n### 装置\n\n<https://example.com/hardware>\n\n装置の要約。\n"
        + "\n## その他\n\n### 未分類\n\n<https://example.com/other>\n\n未分類の要約。\n";
    private const string EscapedMarkdown =
        "# 2026-11-01\n\n## Web\n"
        + "\n### 見出し \\# \\[リンク\\]\\(https://example\\.com\\) &lt;b&gt;&amp;本文&lt;/b&gt; \\`コード\\` \"引用\" '引用' \\\\ 😀\n"
        + "\n<https://example.com/safe>\n\n"
        + "日本語の要約 \\# \\[リンク\\]\\(https://example\\.com\\) &lt;b&gt;&amp;本文&lt;/b&gt; \\# \\[追加\\]\\(x\\) &lt;i&gt;&amp;終&lt;/i&gt; \\`コード\\` \"引用\" '引用' \\\\ 😀\n";
    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("MarkdownDocument." + name, body));
        foreach (var (label, input, expected, relative) in new (string, string, string, string[])[]
        {
            ("utc1459", "2026-10-31T14:59:00+00:00", "2026-10-31T23:59:00+09:00", ["2026", "10", "202610312359.md"]),
            ("utc1500", "2026-10-31T15:00:00+00:00", "2026-11-01T00:00:00+09:00", ["2026", "11", "202611010000.md"]),
            ("month-rollover", "2026-01-31T15:00:00+00:00", "2026-02-01T00:00:00+09:00", ["2026", "02", "202602010000.md"]),
            ("year-rollover", "2026-12-31T15:00:00+00:00", "2027-01-01T00:00:00+09:00", ["2027", "01", "202701010000.md"]),
            ("nonzero-offset", "2026-11-01T08:05:00+02:00", "2026-11-01T15:05:00+09:00", ["2026", "11", "202611011505.md"]),
            ("hour01", "2026-10-31T16:07:00+00:00", "2026-11-01T01:07:00+09:00", ["2026", "11", "202611010107.md"]),
            ("hour13", "2026-11-01T04:07:00+00:00", "2026-11-01T13:07:00+09:00", ["2026", "11", "202611011307.md"]),
            ("research-instant", "2026-10-31T15:05:00+00:00", "2026-11-01T00:05:00+09:00", ["2026", "11", "202611010005.md"])
        }) Add($"TimeAndPath[{label}]", () =>
        {
            var time = DateTimeOffset.Parse(input, CultureInfo.InvariantCulture); var tokyo = MarkdownDocument.TokyoTime(time);
            var exact = DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture);
            Equal(exact, tokyo); Equal(TimeSpan.FromHours(9), tokyo.Offset); Equal(exact.DateTime, tokyo.DateTime);
            string root = Path.Combine(Path.GetTempPath(), "feed-to-md-time-" + Guid.NewGuid().ToString("N"));
            Equal(Path.Combine(root, relative[0], relative[1], relative[2]), MarkdownDocument.OutputPath(root, time));
            // Fixed literal dates, not a format of the actual converted result.
            string heading = label switch { "utc1459" => "# 2026-10-31\n", "month-rollover" => "# 2026-02-01\n", "year-rollover" => "# 2027-01-01\n", _ => "# 2026-11-01\n" };
            Equal(heading, MarkdownDocument.Render(time, [])); Equal(false, Directory.Exists(root));
        });
        Add("TimeAndPath[relative-base]", () =>
        {
            string relative = "feed-to-md-relative-" + Guid.NewGuid().ToString("N");
            Equal(Path.Combine(Environment.CurrentDirectory, relative, "2026", "11", "202611010005.md"), MarkdownDocument.OutputPath(relative, Instant));
            Equal(false, Directory.Exists(relative));
        });
        Add("MultiArticleFullOracle", () => Equal(FiveArticleMarkdown, MarkdownDocument.Render(Instant, FiveRecords())));
        Add("PipelineToFullOracle", () => Equal(FiveArticleMarkdown, MarkdownDocument.Render(Instant, RunFive(FiveItems()))));
        Add("AllCategoriesFullOracle", () =>
        {
            ProcessedArticle Record(string title, string slug, string category, string summary) => new(new(title, new("https://example.com/" + slug)), category, summary, false);
            ProcessedArticle[] shuffled =
            [
                Record("未分類", "other", "未知分類", "未分類の要約。"), Record("実装一", "dev1", "開発", "実装一の要約。"),
                Record("装置", "hardware", "Hardware", "装置の要約。"), Record("学習一", "ai1", "AI・機械学習", "学習一の要約。"),
                Record("安全性", "security", "Security", "安全性の要約。"), Record("実装二", "dev2", "開発", "実装二の要約。"),
                Record("クラウド", "cloud", "Cloud・Infrastructure", "クラウドの要約。"), Record("データ", "data", "Data・DB", "データの要約。"),
                Record("学習二", "ai2", "AI・機械学習", "学習二の要約。"), Record("ウェブ", "web", "Web", "ウェブの要約。")
            ];
            Equal(AllCategoriesMarkdown, MarkdownDocument.Render(Instant, shuffled));
        });
        Add("Empty", () => Equal("# 2026-11-01\n", MarkdownDocument.Render(Instant, [])));
        Add("FailedOverridesSummary", () =>
        {
            ProcessedArticle[] articles = [new(new("失敗した記事", new("https://example.com/failed")), "開発", "表示禁止の任意要約", true)];
            Equal("# 2026-11-01\n\n## 開発\n\n### 失敗した記事\n\n<https://example.com/failed>\n\n要約取得失敗\n", MarkdownDocument.Render(Instant, articles));
        });
        Add("EscapedFullDocument", () =>
        {
            string title = "見出し \n# [リンク](https://example.com) <b>&本文</b>\u202e `コード` \"引用\" '引用' \\ 😀";
            string raw = AnalysisParserTests.LiteralMarkup + "\n# [追加](x) <i>&終</i>\u2066 `コード` \"引用\" '引用' \\ 😀";
            var analysis = AnalysisParser.Parse("{\"category\":\"Web\",\"summary\":" + JsonSerializer.Serialize(raw) + "}");
            ProcessedArticle[] articles = [new(new(title, new("https://example.com/safe")), analysis.Category, analysis.Summary, false)];
            Equal(EscapedMarkdown, MarkdownDocument.Render(Instant, articles));
        });
        foreach (var (label, input, canonical, document) in new (string, string, string, string)[]
        {
            ("angle", "https://example.com/a<b>", "https://example.com/a%3Cb%3E", "# 2026-11-01\n\n## Web\n\n### URL記事\n\n<https://example.com/a%3Cb%3E>\n\n日本語要約。\n"),
            ("backtick", "https://example.com/a`b", "https://example.com/a%60b", "# 2026-11-01\n\n## Web\n\n### URL記事\n\n<https://example.com/a%60b>\n\n日本語要約。\n"),
            ("double-quote", "https://example.com/a\"b", "https://example.com/a%22b", "# 2026-11-01\n\n## Web\n\n### URL記事\n\n<https://example.com/a%22b>\n\n日本語要約。\n"),
            ("single-quote", "https://example.com/a'b", "https://example.com/a'b", "# 2026-11-01\n\n## Web\n\n### URL記事\n\n<https://example.com/a'b>\n\n日本語要約。\n"),
            ("backslash", "https://example.com/a\\b", "https://example.com/a/b", "# 2026-11-01\n\n## Web\n\n### URL記事\n\n<https://example.com/a/b>\n\n日本語要約。\n"),
            ("attribute-looking", "https://example.com/?q=\" onmouseover='x'", "https://example.com/?q=%22%20onmouseover='x'", "# 2026-11-01\n\n## Web\n\n### URL記事\n\n<https://example.com/?q=%22%20onmouseover='x'>\n\n日本語要約。\n")
        }) Add($"UrlEncoding[{label}]", () =>
        {
            var uri = new Uri(input); Equal(canonical, uri.AbsoluteUri);
            Equal(document, MarkdownDocument.Render(Instant, [new(new("URL記事", uri), "Web", "日本語要約。", false)]));
        });
        foreach (var (label, url) in new[] { ("localhost", "https://localhost/a"), ("private-ip", "https://10.0.0.1/a"), ("unsafe-scheme", "ftp://example.com/a"), ("unsafe-port", "https://example.com:444/a") })
            Add($"UnsafeUrl[{label}]", () =>
            {
                string root = Path.Combine(Path.GetTempPath(), "feed-to-md-unused-" + Guid.NewGuid().ToString("N"));
                try
                {
                    Throws<InvalidDataException>(() =>
                    {
                        string markdown = MarkdownDocument.Render(Instant, [new(new("危険なURL", new(url)), "Web", "日本語要約。", false)]);
                        MarkdownDocument.SaveAsync(Path.Combine(root, "not-written.md"), markdown).GetAwaiter().GetResult();
                    });
                    Equal(false, Directory.Exists(root));
                }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            });
        foreach (var (label, payload) in new[] { ("full-oracle", FiveArticleMarkdown), ("japanese-emoji", "日本語と😀の保存。\n次の行。\n"), ("empty", "") })
            Add($"SaveUtf8Nested[{label}]", () => WithTemp(root =>
            {
                string path = Path.Combine(root, "nested", "deep", "document.md");
                MarkdownDocument.SaveAsync(path, payload).GetAwaiter().GetResult();
                Equal(true, Directory.Exists(Path.GetDirectoryName(path))); Bytes(Encoding.UTF8.GetBytes(payload), File.ReadAllBytes(path));
                Equal(payload, File.ReadAllText(path, new UTF8Encoding(false, true))); OnlyFile(root, path);
            }));
        Add("SaveNoOverwrite", () => WithTemp(root =>
        {
            string path = Path.Combine(root, "nested", "document.md");
            MarkdownDocument.SaveAsync(path, FiveArticleMarkdown).GetAwaiter().GetResult();
            var error = Throws<IOException>(() => MarkdownDocument.SaveAsync(path, "上書き禁止😀").GetAwaiter().GetResult());
            Equal("同名ファイルが存在するため上書きしません。", error.Message);
            Bytes(Encoding.UTF8.GetBytes(FiveArticleMarkdown), File.ReadAllBytes(path)); OnlyFile(root, path);
        }));
        Add("SaveMoveFailureCleanup", () => WithTemp(root =>
        {
            string path = Path.Combine(root, "nested", "destination.md"); Directory.CreateDirectory(path);
            string sentinel = Path.Combine(path, "preserved.txt"); File.WriteAllBytes(sentinel, [7, 8, 9]);
            Throws<IOException>(() => MarkdownDocument.SaveAsync(path, "移動できない日本語😀").GetAwaiter().GetResult());
            Equal(true, Directory.Exists(path)); Bytes([7, 8, 9], File.ReadAllBytes(sentinel)); OnlyFile(root, sentinel);
        }));
        Add("SavePreCanceled", () => WithTemp(root =>
        {
            string path = Path.Combine(root, "nested", "document.md"); using var source = new CancellationTokenSource(); source.Cancel();
            OperationCanceledException? observed = null;
            try { MarkdownDocument.SaveAsync(path, FiveArticleMarkdown, source.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException error) { observed = error; }
            Equal(true, observed is not null); Equal(source.Token, observed!.CancellationToken);
            Equal(false, File.Exists(path)); Sequence(Array.Empty<string>(), Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }));
        Add("PipelineRenderSaveIntegration", () => WithTemp(root =>
        {
            string path = MarkdownDocument.OutputPath(root, Instant);
            Equal(Path.Combine(root, "2026", "11", "202611010005.md"), path);
            var result = RunFive(FiveItems()); Equal(5, result.Count);
            string markdown = MarkdownDocument.Render(Instant, result); Equal(FiveArticleMarkdown, markdown);
            MarkdownDocument.SaveAsync(path, markdown).GetAwaiter().GetResult();
            Bytes(Encoding.UTF8.GetBytes(FiveArticleMarkdown), File.ReadAllBytes(path)); Equal(FiveArticleMarkdown, File.ReadAllText(path, Encoding.UTF8)); OnlyFile(root, path);
        }));
    }
    private static void OnlyFile(string root, string expected) => Sequence(new[] { expected }, Directory.GetFiles(root, "*", SearchOption.AllDirectories));
    private static void WithTemp(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "feed-to-md-phase3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, recursive: true); }
        Equal(false, Directory.Exists(root));
    }
}
