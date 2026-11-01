using System.ClientModel;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using FeedToMarkdown;
using static TestSupport;
using static Phase3Fixtures;

public static class ArticlePipelineTests
{
    private const string Summary = "実装の検証方法を説明します。";
    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("ArticlePipeline." + name, body));
        Add("Empty", () =>
        {
            int calls = 0;
            var pipeline = new ArticlePipeline((_, _) => { calls++; return Task.FromResult(new ArticleText(Body, false)); },
                (_, _, _) => { calls++; return Task.FromResult(new Analysis("開発", Summary)); }, (_, _) => calls++);
            Equal(0, pipeline.ProcessAsync([]).GetAwaiter().GetResult().Count); Equal(0, calls);
        });
        Add("MultiArticleOrder", () =>
        {
            using var source = new CancellationTokenSource();
            var items = FiveItems(); var trace = new List<string>();
            var actual = RunFive(items, trace, source.Token);
            Equal(5, actual.Count);
            CheckSuccess(items[0], actual[0], "開発", "実装を説明します。");
            CheckSuccess(items[1], actual[1], "AI・機械学習", "学習方法を説明します。");
            CheckFailure(items[2], actual[2]);
            CheckSuccess(items[3], actual[3], "開発", "設計を説明します。");
            CheckSuccess(items[4], actual[4], "その他", "技術を説明します。");
            Sequence(new[] { "D1", "A1", "D2", "A2", "D3", "W3", "D4", "A4", "D5", "A5" }, trace);
        });
        Add("IndividualFailure[analyze-derived-json]", () =>
        {
            var items = ThreeItems(); var trace = new List<string>();
            var pipeline = new ArticlePipeline(
                (item, _) => { trace.Add("D" + (Array.IndexOf(items, item) + 1)); return Task.FromResult(new ArticleText(Body, false)); },
                (item, _, _) =>
                {
                    int index = Array.IndexOf(items, item); trace.Add("A" + (index + 1));
                    return Task.FromResult(index == 1 ? AnalysisParser.Parse("{not-json}") : new Analysis("開発", Summary));
                },
                (index, _) => { Equal(2, index); trace.Add("W" + index); });
            var result = pipeline.ProcessAsync(items).GetAwaiter().GetResult();
            Equal(3, result.Count);
            CheckSuccess(items[0], result[0]); CheckFailure(items[1], result[1]); CheckSuccess(items[2], result[2]);
            Sequence(new[] { "D1", "A1", "D2", "A2", "W2", "D3", "A3" }, trace);
        });
        foreach (var (label, factory) in new (string, Func<Exception>)[]
        {
            ("http401", () => new HttpRequestException("PRIVATE_SENTINEL", null, HttpStatusCode.Unauthorized)),
            ("http403", () => new HttpRequestException("PRIVATE_SENTINEL", null, HttpStatusCode.Forbidden)),
            ("http404", () => new HttpRequestException("PRIVATE_SENTINEL", null, HttpStatusCode.NotFound)),
            ("http500", () => new HttpRequestException("PRIVATE_SENTINEL", null, HttpStatusCode.InternalServerError)),
            ("invaliddata", () => new InvalidDataException("PRIVATE_SENTINEL")), ("format", () => new FormatException("PRIVATE_SENTINEL")),
            ("json", () => new JsonException("PRIVATE_SENTINEL")), ("io", () => new IOException("PRIVATE_SENTINEL")),
            ("sdk400", () => new RequestFailedException(400, "PRIVATE_SENTINEL")), ("sdk429", () => new RequestFailedException(429, "PRIVATE_SENTINEL")),
            ("sdk500", () => new RequestFailedException(500, "PRIVATE_SENTINEL")), ("client400", () => ClientError(400, "PRIVATE_SENTINEL")),
            ("client429", () => ClientError(429, "PRIVATE_SENTINEL")), ("client500", () => ClientError(500, "PRIVATE_SENTINEL")),
            ("timeout", () => new OperationCanceledException("PRIVATE_SENTINEL"))
        })
        {
            Add($"IndividualFailure[download-{label}]", () => Individual("download", factory(), null, null));
            // All ordinary HTTP statuses share the analyzer HttpRequestException partition.
            if (label is not ("http403" or "http404" or "http500"))
                Add($"IndividualFailure[analyze-{(label == "http401" ? "http" : label)}]", () => Individual("analyze", factory(), null, null));
        }
        foreach (var (label, body, summary) in new (string, string?, string?)[]
        {
            ("body-empty", "", null), ("body-whitespace", " \t\r\n", null),
            ("summary-empty", null, ""), ("summary-whitespace", null, " \t\r\n")
        }) Add($"IndividualFailure[{label}]", () => Individual(body is null ? "analyze" : "download", null, body, summary));
        foreach (int target in new[] { 0, 1 }) Add($"Warning[truncated-{(target == 0 ? "first" : "second")}]", () =>
        {
            var items = ThreeItems(); var trace = new List<string>(); using var source = new CancellationTokenSource();
            var pipeline = new ArticlePipeline((item, token) => { Equal(source.Token, token); int i = Array.IndexOf(items, item); trace.Add("D" + (i + 1)); return Task.FromResult(new ArticleText(Body, i == target)); },
                (item, text, token) => { Equal(source.Token, token); int i = Array.IndexOf(items, item); Equal(Body, text.Text); Equal(i == target, text.Truncated); trace.Add("A" + (i + 1)); return Task.FromResult(new Analysis("開発", Summary)); },
                (index, message) => { Equal(target + 1, index); Equal("記事本文を入力上限で切り詰めました。", message); trace.Add("W" + index); });
            var result = pipeline.ProcessAsync(items, source.Token).GetAwaiter().GetResult();
            for (int i = 0; i < 3; i++) CheckSuccess(items[i], result[i]);
            Sequence(target == 0 ? new[] { "D1", "W1", "A1", "D2", "A2", "D3", "A3" } : new[] { "D1", "A1", "D2", "W2", "A2", "D3", "A3" }, trace);
        });
        Add("Warning[failure-message-redacted]", () => Individual("download", new IOException("TOKEN_AND_ARTICLE_PRIVATE_SENTINEL"), null, null));
        Add("Warning[no-warning-callback]", () =>
        {
            var items = ThreeItems(); var trace = new List<string>();
            var pipeline = new ArticlePipeline((item, _) => { int i = Array.IndexOf(items, item); trace.Add("D" + (i + 1)); if (i == 1) throw new IOException("PRIVATE"); return Task.FromResult(new ArticleText(Body, true)); },
                (item, text, _) => { Equal(true, text.Truncated); trace.Add("A" + (Array.IndexOf(items, item) + 1)); return Task.FromResult(new Analysis("開発", Summary)); });
            var result = pipeline.ProcessAsync(items).GetAwaiter().GetResult();
            Equal(3, result.Count); CheckSuccess(items[0], result[0]); CheckFailure(items[1], result[1]); CheckSuccess(items[2], result[2]);
            Sequence(new[] { "D1", "A1", "D2", "D3", "A3" }, trace);
        });
        foreach (var (label, input, expected, truncated) in new[]
        {
            ("20000", new string('日', 20000), new string('日', 20000), false),
            ("20001", new string('日', 20000) + "後", new string('日', 20000), true),
            ("surrogate-cutoff", new string('日', 19999) + "😀後", new string('日', 19999), true)
        }) Add($"ExtractionTruncation[{label}]", () =>
        {
            using var source = new CancellationTokenSource(); var items = ThreeItems()[..1]; var trace = new List<string>(); ArticleText? extracted = null;
            var pipeline = new ArticlePipeline((item, token) => { Same(items[0], item); Equal(source.Token, token); trace.Add("D"); extracted = ArticleExtractor.Extract("<article><p>" + input + "</p></article>"); return Task.FromResult(extracted!); },
                (item, text, token) => { Same(items[0], item); Same(extracted, text); Equal(source.Token, token); Equal(expected, text.Text); Equal(truncated, text.Truncated); trace.Add("A"); return Task.FromResult(new Analysis("開発", Summary)); },
                (index, message) => { Equal(1, index); Equal("記事本文を入力上限で切り詰めました。", message); trace.Add("W"); });
            var result = pipeline.ProcessAsync(items, source.Token).GetAwaiter().GetResult(); CheckSuccess(items[0], result.Single());
            Sequence(truncated ? new[] { "D", "W", "A" } : new[] { "D", "A" }, trace);
        });
        foreach (var (label, factory) in new (string, Func<Exception>)[]
        {
            ("authentication", () => new AuthenticationFailedException("PRIVATE")), ("credential-unavailable", () => new CredentialUnavailableException("PRIVATE")),
            ("unauthorized", () => new UnauthorizedAccessException("PRIVATE")), ("fatal-wrapper", () => new FatalApplicationException("PRIVATE", new IOException())),
            ("nested-fatal", () => new IOException("PRIVATE", new Exception("PRIVATE", new AuthenticationFailedException("PRIVATE")))),
            ("aggregate-fatal", () => new AggregateException(new IOException(), new UnauthorizedAccessException())),
            ("sdk401", () => new RequestFailedException(401, "PRIVATE")), ("sdk403", () => new RequestFailedException(403, "PRIVATE")), ("sdk404", () => new RequestFailedException(404, "PRIVATE")),
            ("client401", () => ClientError(401, "PRIVATE")), ("client403", () => ClientError(403, "PRIVATE")), ("client404", () => ClientError(404, "PRIVATE"))
        }) foreach (string stage in new[] { "download", "analyze" })
            Add($"Fatal[{label}-{stage}]", () => Escapes(stage, factory));
        foreach (string stage in new[] { "download", "analyze" }) Add($"UnsupportedException[{stage}]", () => Escapes(stage, () => new Exception("PRIVATE")));
        foreach (string label in new[] { "pre-canceled", "during-download", "during-analyze" }) Add($"Cancellation[{label}]", () => Cancellation(label));
        foreach (string label in new[] { "truncation", "failure" }) Add($"WarningCallbackThrows[{label}]", () =>
        {
            var items = ThreeItems(); var trace = new List<string>(); var error = new InvalidOperationException("CALLBACK_PRIVATE");
            var pipeline = new ArticlePipeline((_, _) => { trace.Add("D1"); if (label == "failure") throw new IOException("PRIVATE"); return Task.FromResult(new ArticleText(Body, true)); },
                (_, _, _) => { trace.Add("A1"); return Task.FromResult(new Analysis("開発", Summary)); },
                (index, message) => { Equal(1, index); Equal(label == "failure" ? "本文取得または分析に失敗しました（IOException）。" : "記事本文を入力上限で切り詰めました。", message); trace.Add("W1"); throw error; });
            Same(error, Throws<InvalidOperationException>(() => pipeline.ProcessAsync(items).GetAwaiter().GetResult())); Sequence(new[] { "D1", "W1" }, trace);
        });
        Add("WarningCallbackThrows[permitted-truncation-callback-io]", () =>
        {
            var trace = new List<string>(); var error = new IOException("CALLBACK_PRIVATE");
            var pipeline = new ArticlePipeline((_, _) => { trace.Add("D"); return Task.FromResult(new ArticleText(Body, true)); }, (_, _, _) => { trace.Add("A"); return Task.FromResult(new Analysis("開発", Summary)); },
                (index, message) => { Equal(1, index); trace.Add(message); throw error; });
            Same(error, Throws<IOException>(() => pipeline.ProcessAsync(ThreeItems()).GetAwaiter().GetResult()));
            Sequence(new[] { "D", "記事本文を入力上限で切り詰めました。", "本文取得または分析に失敗しました（IOException）。" }, trace);
        });
        foreach (string label in new[] { "http401", "http403", "http404", "synthetic-timeout", "pdf-no-analyzer" })
            Add($"OfflineTransport[{label}]", () => Offline(label));
    }
    private static void Individual(string stage, Exception? error, string? body, string? summary)
    {
        var items = ThreeItems(); var trace = new List<string>(); using var source = new CancellationTokenSource();
        var texts = items.Select(_ => new ArticleText(Body, false)).ToArray();
        var pipeline = new ArticlePipeline((item, token) =>
        {
            Equal(source.Token, token); int i = Array.IndexOf(items, item); Same(items[i], item); trace.Add("D" + (i + 1));
            if (i == 1 && stage == "download") { if (error is not null) throw error; return Task.FromResult(new ArticleText(body!, false)); }
            return Task.FromResult(texts[i]);
        }, (item, text, token) =>
        {
            Equal(source.Token, token); int i = Array.IndexOf(items, item); Same(items[i], item); Same(texts[i], text); Equal(Body, text.Text); trace.Add("A" + (i + 1));
            if (i == 1 && stage == "analyze") { if (error is not null) throw error; return Task.FromResult(new Analysis("開発", summary!)); }
            return Task.FromResult(new Analysis("開発", Summary));
        }, (index, message) =>
        {
            Equal(2, index);
            // Independent complete warning literals for the supported public exception partitions.
            string expected = error switch
            {
                HttpRequestException => "本文取得または分析に失敗しました（HttpRequestException）。",
                InvalidDataException => "本文取得または分析に失敗しました（InvalidDataException）。",
                JsonException => "本文取得または分析に失敗しました（JsonException）。",
                RequestFailedException => "本文取得または分析に失敗しました（RequestFailedException）。",
                ClientResultException => "本文取得または分析に失敗しました（ClientResultException）。",
                OperationCanceledException => "本文取得または分析に失敗しました（OperationCanceledException）。",
                IOException => "本文取得または分析に失敗しました（IOException）。",
                FormatException => "本文取得または分析に失敗しました（FormatException）。",
                _ => body is not null || error is InvalidDataException ? "本文取得または分析に失敗しました（InvalidDataException）。" : "本文取得または分析に失敗しました（FormatException）。"
            };
            Equal(expected, message); Equal(false, message.Contains("PRIVATE", StringComparison.Ordinal)); trace.Add("W2");
        });
        var result = pipeline.ProcessAsync(items, source.Token).GetAwaiter().GetResult();
        Equal(false, source.IsCancellationRequested); Equal(3, result.Count);
        CheckSuccess(items[0], result[0]); CheckFailure(items[1], result[1]); CheckSuccess(items[2], result[2]);
        Sequence(stage == "download" ? new[] { "D1", "A1", "D2", "W2", "D3", "A3" } : new[] { "D1", "A1", "D2", "A2", "W2", "D3", "A3" }, trace);
    }
    private static void Escapes(string stage, Func<Exception> factory)
    {
        var items = ThreeItems(); var trace = new List<string>(); var error = factory(); using var source = new CancellationTokenSource();
        var pipeline = new ArticlePipeline((item, token) => { Equal(source.Token, token); int i = Array.IndexOf(items, item); trace.Add("D" + (i + 1)); if (i == 1 && stage == "download") throw error; return Task.FromResult(new ArticleText(Body, false)); },
            (item, text, token) => { Equal(source.Token, token); Equal(Body, text.Text); int i = Array.IndexOf(items, item); trace.Add("A" + (i + 1)); if (i == 1 && stage == "analyze") throw error; return Task.FromResult(new Analysis("開発", Summary)); },
            (_, _) => trace.Add("W"));
        Exception? observed = null;
        try { pipeline.ProcessAsync(items, source.Token).GetAwaiter().GetResult(); } catch (Exception caught) { observed = caught; }
        Same(error, observed);
        Sequence(stage == "download" ? new[] { "D1", "A1", "D2" } : new[] { "D1", "A1", "D2", "A2" }, trace);
    }
    private static void Cancellation(string label)
    {
        using var source = new CancellationTokenSource(); var items = ThreeItems(); var trace = new List<string>();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new ArticlePipeline(async (item, token) =>
        {
            Equal(source.Token, token); int i = Array.IndexOf(items, item); trace.Add("D" + (i + 1));
            if (i == 1 && label == "during-download") { entered.SetResult(true); await release.Task; token.ThrowIfCancellationRequested(); }
            return new ArticleText(Body, false);
        }, async (item, text, token) =>
        {
            Equal(source.Token, token); Equal(Body, text.Text); int i = Array.IndexOf(items, item); trace.Add("A" + (i + 1));
            if (i == 1 && label == "during-analyze") { entered.SetResult(true); await release.Task; token.ThrowIfCancellationRequested(); }
            return new Analysis("開発", Summary);
        }, (_, _) => trace.Add("W"));
        if (label == "pre-canceled") source.Cancel();
        var task = pipeline.ProcessAsync(items, source.Token);
        if (label != "pre-canceled") { entered.Task.GetAwaiter().GetResult(); source.Cancel(); release.SetResult(true); }
        var error = Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult()); Equal(source.Token, error.CancellationToken);
        Sequence(label == "pre-canceled" ? Array.Empty<string>() : label == "during-download" ? new[] { "D1", "A1", "D2" } : new[] { "D1", "A1", "D2", "A2" }, trace);
    }
    private static void Offline(string label)
    {
        var items = ThreeItems(); var trace = new List<string>(); using var source = new CancellationTokenSource();
        var handler = new OfflineHttpFixtures.Handler(); var bytes = Encoding.UTF8.GetBytes("<article><p>" + Body + "</p></article>");
        var before = new OfflineHttpFixtures.Response(bytes); var after = new OfflineHttpFixtures.Response(bytes);
        var middle = new OfflineHttpFixtures.Response(bytes, label == "pdf-no-analyzer" ? "application/pdf" : "text/html", status: label switch { "http401" => HttpStatusCode.Unauthorized, "http403" => HttpStatusCode.Forbidden, "http404" => HttpStatusCode.NotFound, _ => HttpStatusCode.OK });
        handler.Responses.AddRange([before, middle, after]);
        handler.Send = (i, _) => i == 1 && label == "synthetic-timeout" ? Task.FromException<HttpResponseMessage>(new OperationCanceledException("PRIVATE_TIMEOUT")) : Task.FromResult<HttpResponseMessage>(handler.Responses[i]);
        using var downloader = new PublicHttpDownloader(handler);
        var pipeline = new ArticlePipeline(async (item, token) => { Equal(source.Token, token); trace.Add("D" + (Array.IndexOf(items, item) + 1)); var page = await downloader.DownloadAsync(item.Url, true, token); return ArticleExtractor.Extract(ArticleExtractor.DecodeHtml(page.Bytes, page.Charset)); },
            (item, text, token) => { Equal(source.Token, token); Equal(Body, text.Text); Equal(false, text.Truncated); trace.Add("A" + (Array.IndexOf(items, item) + 1)); return Task.FromResult(new Analysis("開発", Summary)); },
            (index, message) => { Equal(2, index); Equal(label == "pdf-no-analyzer" ? "本文取得または分析に失敗しました（InvalidDataException）。" : label == "synthetic-timeout" ? "本文取得または分析に失敗しました（OperationCanceledException）。" : "本文取得または分析に失敗しました（HttpRequestException）。", message); trace.Add("W2"); });
        var result = pipeline.ProcessAsync(items, source.Token).GetAwaiter().GetResult();
        Equal(3, result.Count); CheckSuccess(items[0], result[0]); CheckFailure(items[1], result[1]); CheckSuccess(items[2], result[2]); Equal(false, source.IsCancellationRequested);
        Sequence(new[] { "D1", "A1", "D2", "W2", "D3", "A3" }, trace); Sequence(items.Select(i => i.Url.AbsoluteUri), handler.Requests.Select(r => r.Uri));
        if (label == "pdf-no-analyzer") Equal(0, middle.Probe.Stream.ReadCalls);
    }
}
