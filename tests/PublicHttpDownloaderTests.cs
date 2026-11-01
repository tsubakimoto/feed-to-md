using System.Net;
using System.Text;
using FeedToMarkdown;
using static TestSupport;
using static OfflineHttpFixtures;

public static class PublicHttpDownloaderTests
{
    private static readonly Uri Start = new("https://example.com/articles/start");
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("公開記事の本文です。😀");
    private static void Add(List<TestCase> cases, string name, Func<Task> body) => cases.Add(new("PublicHttpDownloader." + name, () => body().GetAwaiter().GetResult()));
    private static Handler Script(params Response[] responses) { var handler = new Handler(); handler.Responses.AddRange(responses); return handler; }
    private static Response Redirect(int status, string? location)
    {
        var response = new Response([99], status: (HttpStatusCode)status);
        if (location is not null) response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }
    private static void Disposed(Handler handler)
    {
        foreach (var response in handler.Responses) { Equal(true, response.Disposed); Equal(true, response.Probe.Disposed); Equal(true, response.Probe.Stream.Disposed); }
        foreach (var probe in handler.RequestProbes) Equal(true, probe.Disposed);
    }
    private static void Result(DownloadedContent actual, byte[] bytes, string? media, string? charset)
    { Bytes(bytes, actual.Bytes); Equal(media, actual.MediaType); Equal(charset, actual.Charset); }
    private static async Task<T> Reject<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (Exception error) { if (error.GetType() == typeof(T)) return (T)error; throw new TestAssertionException($"Expected {typeof(T).Name}; actual {error.GetType().Name}"); }
        throw new TestAssertionException($"Expected {typeof(T).Name}; no exception");
    }
    private static async Task<OperationCanceledException> Canceled(Task task)
    {
        try { await task; }
        catch (OperationCanceledException error) { return error; } // TaskCanceledException is the framework's cancellation subtype.
        throw new TestAssertionException("Expected cancellation; task completed");
    }
    private static byte[] Large(int size) => Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray();
    public static void Register(List<TestCase> cases)
    {
        foreach (var (label, media, charset, html, bytes) in new (string, string?, string?, bool, byte[])[]
        {
            ("html", "Text/HTML", "\"utf-8\"", true, Payload), ("xhtml", "Application/XHTML+XML", "shift_jis", true, Payload),
            ("feed-nonhtml", "Application/RSS+XML", null, false, [60, 114, 100, 102, 47, 62]), ("zero-bytes", "text/html", null, true, [])
        }) Add(cases, $"Success[{label}]", async () =>
        {
            var response = new Response(bytes, media, charset);
            var handler = Script(response);
            using (var downloader = new PublicHttpDownloader(handler))
            {
                Result(await downloader.DownloadAsync(Start, html), bytes, media?.ToLowerInvariant(), charset);
                Sequence(new[] { new RequestSnapshot("GET", Start.AbsoluteUri, html ? "text/html,application/xhtml+xml" : "application/rss+xml,application/xml,text/xml", "FeedToMarkdown/1.0", true) }, handler.Requests);
                Equal(bytes.Length, response.Probe.Stream.BytesRead);
                Equal(true, response.Probe.Stream.LastToken.CanBeCanceled);
                Disposed(handler); Equal(false, handler.Disposed);
            }
            Equal(true, handler.Disposed);
        });
        foreach (var (label, media) in new (string, string?)[] { ("missing", null), ("pdf", "application/pdf"), ("json", "application/json") })
        {
            Add(cases, $"HtmlMimeReject[{label}]", async () =>
            {
                var response = new Response(Payload, media); var handler = Script(response);
                using var downloader = new PublicHttpDownloader(handler);
                Equal("公開HTMLではありません。", (await Reject<InvalidDataException>(() => downloader.DownloadAsync(Start, true))).Message);
                Equal(1, handler.Requests.Count); Equal(0, response.Probe.Stream.ReadCalls); Equal(0, response.Probe.StreamRequests); Disposed(handler);
            });
            Add(cases, $"FeedMime[{label}]", async () =>
            {
                var response = new Response(Payload, media); var handler = Script(response);
                using var downloader = new PublicHttpDownloader(handler);
                Result(await downloader.DownloadAsync(Start, false), Payload, media, media is null ? null : "utf-8");
                Equal("application/rss+xml,application/xml,text/xml", handler.Requests.Single().Accept); Disposed(handler);
            });
        }
        foreach (int status in new[] { 301, 302, 303, 307, 308 }) foreach (bool absolute in new[] { false, true })
            Add(cases, $"Redirect[{status}-{(absolute ? "absolute" : "relative")}]", async () =>
            {
                string location = absolute ? "https://other.example.com/final" : "../final?from=rss";
                string expected = absolute ? "https://other.example.com/final" : "https://example.com/final?from=rss";
                var redirect = Redirect(status, location); var final = new Response(Payload); var handler = Script(redirect, final);
                using var downloader = new PublicHttpDownloader(handler);
                Result(await downloader.DownloadAsync(Start, true), Payload, "text/html", "utf-8");
                Sequence(new[] { Start.AbsoluteUri, expected }, handler.Requests.Select(r => r.Uri));
                Sequence(new[] { "GET", "GET" }, handler.Requests.Select(r => r.Method));
                Equal(0, redirect.Probe.Stream.ReadCalls); Disposed(handler);
            });
        foreach (var (label, success) in new[] { ("five-then-success", true), ("sixth-redirect", false) }) Add(cases, $"RedirectLimit[{label}]", async () =>
        {
            Equal(5, PublicHttpDownloader.MaxRedirects);
            var handler = new Handler();
            for (int i = 1; i <= 5; i++) handler.Responses.Add(Redirect(302, "/hop" + i));
            handler.Responses.Add(success ? new Response(Payload) : Redirect(302, "/hop6"));
            using var downloader = new PublicHttpDownloader(handler);
            if (success) Result(await downloader.DownloadAsync(Start, true), Payload, "text/html", "utf-8");
            else Equal("リダイレクト上限または不正な転送先です。", (await Reject<HttpRequestException>(() => downloader.DownloadAsync(Start, true))).Message);
            Sequence(new[] { Start.AbsoluteUri, "https://example.com/hop1", "https://example.com/hop2", "https://example.com/hop3", "https://example.com/hop4", "https://example.com/hop5" }, handler.Requests.Select(r => r.Uri));
            Disposed(handler);
        });
        foreach (var (label, location) in new (string, string?)[] { ("missing-location", null), ("localhost", "https://localhost/x"), ("private-ip", "https://10.0.0.1/x"), ("unsafe-scheme", "ftp://example.com/x"), ("unsafe-port", "https://example.com:444/x") })
            Add(cases, $"RedirectReject[{label}]", async () =>
            {
                var handler = Script(Redirect(302, location)); using var downloader = new PublicHttpDownloader(handler);
                if (location is null) await Reject<HttpRequestException>(() => downloader.DownloadAsync(Start, true));
                else await Reject<InvalidDataException>(() => downloader.DownloadAsync(Start, true));
                Sequence(new[] { Start.AbsoluteUri }, handler.Requests.Select(r => r.Uri)); Disposed(handler);
                Equal(0, handler.Responses[0].Probe.Stream.ReadCalls);
            });
        foreach (var (label, uri) in new[] { ("localhost", "https://localhost/x"), ("private-ip", "https://10.0.0.1/x"), ("unsafe-scheme", "ftp://example.com/x"), ("unsafe-port", "https://example.com:444/x") })
            Add(cases, $"InitialUriReject[{label}]", async () =>
            {
                var handler = new Handler(); using var downloader = new PublicHttpDownloader(handler);
                await Reject<InvalidDataException>(() => downloader.DownloadAsync(new Uri(uri), true)); Equal(0, handler.Requests.Count); Equal(0, handler.RequestProbes.Count);
            });
        foreach (int status in new[] { 401, 403, 404, 500 }) Add(cases, $"HttpFailure[{status}]", async () =>
        {
            var response = new Response(Payload, status: (HttpStatusCode)status); var handler = Script(response); using var downloader = new PublicHttpDownloader(handler);
            var error = await Reject<HttpRequestException>(() => downloader.DownloadAsync(Start, true));
            Equal((HttpStatusCode?)status, error.StatusCode); Equal(false, FailurePolicy.IsFatal(error));
            Equal(0, response.Probe.Stream.ReadCalls); Disposed(handler);
        });
        foreach (var (label, length) in new[] { ("zero", 0), ("exact-limit", 2097152), ("over-limit", 2097153) }) Add(cases, $"HeaderSize[{label}]", async () =>
        {
            Equal(2097152, PublicHttpDownloader.MaxBodyBytes);
            byte[] bytes = Large(length); var response = new Response(bytes, length: length); var handler = Script(response); using var downloader = new PublicHttpDownloader(handler);
            if (length <= 2097152) { Result(await downloader.DownloadAsync(Start, true), bytes, "text/html", "utf-8"); Equal(length, response.Probe.Stream.BytesRead); }
            else { await Reject<InvalidDataException>(() => downloader.DownloadAsync(Start, true)); Equal(0, response.Probe.Stream.ReadCalls); Equal(0, response.Probe.StreamRequests); }
            Disposed(handler);
        });
        foreach (var (label, length, declared, chunks) in new (string, int, long?, int[]?)[]
        {
            ("absent-header-exact", 2097152, null, null), ("absent-header-over", 2097153, null, null),
            ("underreported-header-over", 2097153, 2, null), ("partial-chunks", 41, null, [1, 7, 3, 11])
        }) Add(cases, $"StreamSize[{label}]", async () =>
        {
            byte[] bytes = Large(length); var response = new Response(bytes, length: declared, chunks: chunks); var handler = Script(response); using var downloader = new PublicHttpDownloader(handler);
            if (length <= 2097152) Result(await downloader.DownloadAsync(Start, true), bytes, "text/html", "utf-8");
            else await Reject<InvalidDataException>(() => downloader.DownloadAsync(Start, true));
            Equal(length, response.Probe.Stream.BytesRead);
            Equal(length > 2097152 ? 129 : length == 2097152 ? 129 : 9, response.Probe.Stream.ReadCalls);
            Disposed(handler);
        });
        foreach (string label in new[] { "success", "redirect", "mime-error", "http-error", "size-error", "cancel", "owner-dispose" }) Add(cases, $"Disposal[{label}]", async () =>
        {
            var response = new Response(Payload, label == "mime-error" ? "application/pdf" : "text/html", length: label == "size-error" ? 2097153 : null, status: label == "http-error" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
            var handler = label == "redirect" ? Script(Redirect(301, "/next"), response) : Script(response);
            var downloader = new PublicHttpDownloader(handler);
            using var caller = new CancellationTokenSource();
            try
            {
                if (label == "cancel")
                {
                    response.Probe.Stream.BlockRead = true;
                    Task task = downloader.DownloadAsync(Start, true, caller.Token);
                    Equal(true, response.Probe.Stream.ReadEntered.Task.IsCompletedSuccessfully);
                    caller.Cancel(); await Canceled(task);
                }
                else if (label == "http-error") await Reject<HttpRequestException>(() => downloader.DownloadAsync(Start, true));
                else if (label is "mime-error" or "size-error") await Reject<InvalidDataException>(() => downloader.DownloadAsync(Start, true));
                else Result(await downloader.DownloadAsync(Start, true), Payload, "text/html", "utf-8");
                Disposed(handler); Equal(false, handler.Disposed);
            }
            finally { downloader.Dispose(); }
            Equal(true, handler.Disposed);
        });
        Add(cases, "Cancellation[pre-canceled]", async () =>
        {
            var handler = new Handler(); using var downloader = new PublicHttpDownloader(handler); using var caller = new CancellationTokenSource(); caller.Cancel();
            await Canceled(downloader.DownloadAsync(Start, true, caller.Token)); Equal(0, handler.Requests.Count);
        });
        Add(cases, "Cancellation[during-send]", async () =>
        {
            var handler = new Handler();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.Send = async (_, token) =>
            {
                var canceled = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() => canceled.TrySetCanceled(token));
                entered.SetResult(true); return await canceled.Task;
            };
            using var downloader = new PublicHttpDownloader(handler); using var caller = new CancellationTokenSource();
            Task task = downloader.DownloadAsync(Start, true, caller.Token);
            Equal(true, entered.Task.IsCompletedSuccessfully); Equal(true, handler.LastToken.CanBeCanceled);
            caller.Cancel(); await Canceled(task);
            Equal(true, handler.LastToken.IsCancellationRequested); Equal(1, handler.Requests.Count); Equal(true, handler.RequestProbes.Single().Disposed);
        });
        Add(cases, "Cancellation[during-read]", async () =>
        {
            var response = new Response(Payload); response.Probe.Stream.BlockRead = true;
            var handler = Script(response); using var downloader = new PublicHttpDownloader(handler); using var caller = new CancellationTokenSource();
            Task task = downloader.DownloadAsync(Start, true, caller.Token);
            Equal(true, response.Probe.Stream.ReadEntered.Task.IsCompletedSuccessfully);
            Equal(true, response.Probe.Stream.LastToken.CanBeCanceled);
            caller.Cancel(); await Canceled(task);
            Equal(true, response.Probe.Stream.LastToken.IsCancellationRequested); Equal(1, response.Probe.Stream.ReadCalls); Equal(0, response.Probe.Stream.BytesRead); Equal(1, handler.Requests.Count); Disposed(handler);
        });
        Add(cases, "SyntheticTimeout", async () =>
        {
            var timeout = new OperationCanceledException("BENIGN_TIMEOUT_SENTINEL");
            var handler = new Handler { Send = (_, _) => Task.FromException<HttpResponseMessage>(timeout) };
            using var downloader = new PublicHttpDownloader(handler); using var caller = new CancellationTokenSource();
            var actual = await Canceled(downloader.DownloadAsync(Start, true, caller.Token));
            Same(timeout, actual); Equal(false, caller.IsCancellationRequested); Equal(1, handler.Requests.Count); Equal(true, handler.RequestProbes.Single().Disposed);
        });
        cases.Add(new("PublicHttpDownloader.TimeoutConstant", () => Equal(TimeSpan.FromSeconds(30), PublicHttpDownloader.RequestTimeout)));
        Add(cases, "Success[declared-charset-preserved]", async () =>
        {
            var handler = Script(new Response(Payload, "text/html", "UTF-8")); using var downloader = new PublicHttpDownloader(handler);
            Result(await downloader.DownloadAsync(Start, true), Payload, "text/html", "UTF-8"); Disposed(handler);
        });
        Add(cases, "StreamSize[underreported-header-within-limit]", async () =>
        {
            var handler = Script(new Response(Payload, length: 1)); using var downloader = new PublicHttpDownloader(handler);
            Result(await downloader.DownloadAsync(Start, true), Payload, "text/html", "utf-8"); Disposed(handler);
        });
        Add(cases, "Redirect[non-redirect-300]", async () =>
        {
            var handler = Script(Redirect(300, "/not-followed")); using var downloader = new PublicHttpDownloader(handler);
            Equal((HttpStatusCode?)300, (await Reject<HttpRequestException>(() => downloader.DownloadAsync(Start, true))).StatusCode);
            Equal(1, handler.Requests.Count); Disposed(handler);
        });
    }
}
