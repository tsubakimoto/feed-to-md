using System.ClientModel;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Identity;
using FeedToMarkdown;
using static TestSupport;

public static class LeafContractTests
{
    public static void Register(List<TestCase> cases)
    {
        RegisterCli(cases);
        RegisterConfiguration(cases);
        RegisterText(cases);
        RegisterNetwork(cases);
        RegisterFailure(cases);
    }
    private static void Add(List<TestCase> cases, string name, Action body) => cases.Add(new(name, body));
    private static readonly string BaseDirectory = Path.Combine(Path.GetTempPath(), "feed-to-md-fixed-base");
    private const string Endpoint = "https://unit.services.ai.azure.com/api/projects/demo";
    private static readonly string[] CategoryNames = ["AI・機械学習", "開発", "Web", "Cloud・Infrastructure", "Security", "Data・DB", "Hardware", "その他"];

    private static void RegisterCli(List<TestCase> cases)
    {
        Add(cases, "CliOptions.Defaults[explicit-directory]", () => Equal(new CliOptions(10, BaseDirectory, false), CliOptions.Parse([], BaseDirectory)));
        Add(cases, "CliOptions.Defaults[environment-directory]", () => Equal(new CliOptions(10, Environment.CurrentDirectory, false), CliOptions.Parse([])));
        foreach (var (label, input, count) in new[] { ("1", "1", 1), ("10", "10", 10), ("int-max", "2147483647", int.MaxValue), ("leading-zero", "001", 1) })
            Add(cases, $"CliOptions.PositiveCount[{label}]", () => Equal(new CliOptions(count, BaseDirectory, false), CliOptions.Parse(["--count", input], BaseDirectory)));
        foreach (var (label, input) in new[] { ("zero", "0"), ("negative", "-1"), ("overflow", "2147483648"), ("decimal", "1.5"), ("alphabetic", "one"), ("plus-sign", "+1"), ("leading-space", " 1"), ("trailing-space", "1 ") })
            Add(cases, $"CliOptions.RejectCount[{label}]", () => Contains("--count は正の整数", Throws<ArgumentException>(() => CliOptions.Parse(["--count", input], BaseDirectory)).Message));
        foreach (var (label, args, diagnostic) in new (string, string[], string)[]
        {
            ("missing-count", ["--count"], "--count"), ("duplicate-count", ["--count", "1", "--count", "2"], "引数が不正です。--help を確認してください。"),
                        ("unknown-option", ["--unknown"], "引数が不正です。--help を確認してください。"), ("positional", ["article"], "引数が不正です。--help を確認してください。"),
                        ("missing-output", ["--output-dir"], "--output-dir"), ("duplicate-output", ["--output-dir", "out", "--output-dir", "out2"], "引数が不正です。--help を確認してください。"),
            ("empty-output", ["--output-dir", ""], "出力先が空"), ("whitespace-output", ["--output-dir", " \t"], "出力先が空")
        }) Add(cases, $"CliOptions.RejectArguments[{label}]", () =>
        {
            var error = Throws<ArgumentException>(() => CliOptions.Parse(args, BaseDirectory));
            if (label is "duplicate-count" or "duplicate-output" or "unknown-option" or "positional") Equal(diagnostic, error.Message);
            else Contains(diagnostic, error.Message);
        });
        foreach (var (label, input, expected) in new[]
        {
            ("absolute", Path.Combine(Path.GetTempPath(), "feed-to-md-fixed-absolute"), Path.Combine(Path.GetTempPath(), "feed-to-md-fixed-absolute")),
            ("relative-out", "out", Path.Combine(BaseDirectory, "out")),
            ("parent-out", Path.Combine("..", "out"), Path.Combine(Path.GetTempPath(), "out"))
        }) Add(cases, $"CliOptions.OutputResolution[{label}]", () =>
        {
            bool existed = Directory.Exists(expected);
            Equal(new CliOptions(10, expected, false), CliOptions.Parse(["--output-dir", input], BaseDirectory));
            Equal(existed, Directory.Exists(expected));
        });
        foreach (var (label, args) in new (string, string[])[]
        {
            ("count-first", ["--count", "3", "--output-dir", "out"]), ("output-first", ["--output-dir", "out", "--count", "3"])
        }) Add(cases, $"CliOptions.OptionOrder[{label}]", () => Equal(new CliOptions(3, Path.Combine(BaseDirectory, "out"), false), CliOptions.Parse(args, BaseDirectory)));
        foreach (var (label, args) in new (string, string[])[]
        {
            ("alone", ["--help"]), ("before-bad-option", ["--help", "--bad"]), ("after-bad-option", ["--bad", "--help"]),
            ("missing-count", ["--count", "--help"]), ("invalid-count", ["--count", "not-an-int", "--help"])
        }) Add(cases, $"CliOptions.HelpPrecedence[{label}]", () => Equal(new CliOptions(10, BaseDirectory, true), CliOptions.Parse(args, BaseDirectory)));
    }

    private static void RegisterConfiguration(List<TestCase> cases)
    {
        foreach (var (label, endpoint, deployment, expectedEndpoint, expectedDeployment) in new[]
        {
            ("project", Endpoint, "model-unit", Endpoint, "model-unit"),
            ("trailing-slash", Endpoint + "/", "model-unit", Endpoint + "/", "model-unit"),
            ("trimmed-fields", "  " + Endpoint + "  ", "  model-unit  ", Endpoint, "model-unit")
        }) Add(cases, $"FoundryConfiguration.Valid[{label}]", () =>
        {
            var actual = FoundryConfiguration.Parse(endpoint, deployment);
            Equal(expectedEndpoint, actual.Endpoint.AbsoluteUri);
            Equal(expectedDeployment, actual.Deployment);
        });
        foreach (var (label, endpoint, deployment, diagnostic) in new (string, string?, string?, string)[]
        {
            ("endpoint-null", null, "model", "AZURE_AI_PROJECT_ENDPOINT"), ("endpoint-empty", "", "model", "AZURE_AI_PROJECT_ENDPOINT"),
            ("endpoint-space", " \t", "model", "AZURE_AI_PROJECT_ENDPOINT"), ("deployment-null", Endpoint, null, "AZURE_AI_MODEL_DEPLOYMENT_NAME"),
            ("deployment-empty", Endpoint, "", "AZURE_AI_MODEL_DEPLOYMENT_NAME"), ("deployment-space", Endpoint, " \t", "AZURE_AI_MODEL_DEPLOYMENT_NAME")
        }) Add(cases, $"FoundryConfiguration.Missing[{label}]", () => Contains(diagnostic, Throws<ArgumentException>(() => FoundryConfiguration.Parse(endpoint, deployment)).Message));
        foreach (var (label, endpoint) in new[]
        {
            ("relative", "/api/projects/demo"), ("malformed", "https://[broken/api/projects/demo"), ("http", "http://unit.services.ai.azure.com/api/projects/demo"),
            ("wrong-project-path", "https://unit.services.ai.azure.com/projects/demo"), ("missing-project", "https://unit.services.ai.azure.com/api/projects/"),
            ("extra-segment", Endpoint + "/extra")
        }) Add(cases, $"FoundryConfiguration.Format[{label}]", () => Contains("Foundry Project endpoint", Throws<ArgumentException>(() => FoundryConfiguration.Parse(endpoint, "model")).Message));
        foreach (var (label, endpoint, policy) in new[]
        {
            ("userinfo", "https://unit-user@unit.services.ai.azure.com/api/projects/demo", false), ("query", Endpoint + "?q=unit", false), ("fragment", Endpoint + "#unit", false),
            ("localhost", "https://localhost/api/projects/demo", true), ("private-ip", "https://10.0.0.1/api/projects/demo", true),
            ("port", "https://unit.services.ai.azure.com:444/api/projects/demo", true)
        }) Add(cases, $"FoundryConfiguration.Policy[{label}]", () =>
        {
            if (policy) Throws<InvalidDataException>(() => FoundryConfiguration.Parse(endpoint, "model"));
            else Contains("Foundry Project endpoint", Throws<ArgumentException>(() => FoundryConfiguration.Parse(endpoint, "model")).Message);
        });
        foreach (var (label, input, expected) in new[] { ("length-256", new string('m', 256), new string('m', 256)), ("surrounding-spaces", "  model unit  ", "model unit") })
            Add(cases, $"FoundryConfiguration.Deployment[{label}]", () =>
            {
                var result = FoundryConfiguration.Parse(Endpoint, input);
                Equal(Endpoint, result.Endpoint.AbsoluteUri);
                Equal(expected, result.Deployment);
            });
        foreach (var (label, input) in new[] { ("length-257", new string('m', 257)), ("internal-control", "mo\u0001del"), ("internal-newline", "mo\ndel") })
            Add(cases, $"FoundryConfiguration.Deployment[{label}]", () => Contains("Deployment 名が不正", Throws<ArgumentException>(() => FoundryConfiguration.Parse(Endpoint, input)).Message));
    }

    private static void RegisterText(List<TestCase> cases)
    {
        Add(cases, "Categories.OrderedExact", () =>
        {
            Sequence(CategoryNames, Categories.Ordered);
            Equal(8, Categories.Ordered.Count);
            Equal(8, Categories.Ordered.Distinct(StringComparer.Ordinal).Count());
        });
        Add(cases, "Categories.OrderedReadOnly", () =>
        {
            if (Categories.Ordered is IList<string> list) Throws<NotSupportedException>(() => list[0] = "changed");
            Sequence(CategoryNames, Categories.Ordered);
        });
        foreach (var (label, input, expected) in new (string, string?, string)[]
        {
            ("ai", "AI・機械学習", "AI・機械学習"), ("development", "開発", "開発"), ("web", "Web", "Web"),
            ("cloud", "Cloud・Infrastructure", "Cloud・Infrastructure"), ("security", "Security", "Security"), ("data", "Data・DB", "Data・DB"),
            ("hardware", "Hardware", "Hardware"), ("other", "その他", "その他"), ("null", null, "その他"), ("empty", "", "その他"),
            ("unknown", "Robotics", "その他"), ("case-variant", "web", "その他"), ("leading-space", " Web", "その他"), ("trailing-space", "Web ", "その他")
        }) Add(cases, $"Categories.Normalize[{label}]", () => Equal(expected, Categories.Normalize(input)));
        foreach (var (label, input, expected) in new[]
        {
            ("empty", "", ""), ("repeated-space", "one   two", "one two"), ("tabs", "one\t\ttwo", "one two"), ("crlf", "one\r\ntwo", "one two"),
            ("nonbreaking", "one\u00a0two", "one two"), ("trim", " \t one two \n", "one two"), ("nonwhitespace-control", "ab\u0000\u0001\u007fcd", "abcd"),
            ("japanese", "日本語の本文。", "日本語の本文。"), ("emoji", "記事😀🚀", "記事😀🚀"), ("all-control", "\u0000\u0001\u007f", ""),
            ("markup-literal", "# [link](https://example.com) <b>&x</b>", "# [link](https://example.com) <b>&x</b>")
        }) Add(cases, $"TextUtilities.Plain[{label}]", () => Equal(expected, TextUtilities.Plain(input)));
        foreach (var (label, control) in new[] { ("202a", '\u202a'), ("202b", '\u202b'), ("202c", '\u202c'), ("202d", '\u202d'), ("202e", '\u202e'), ("2066", '\u2066'), ("2067", '\u2067'), ("2068", '\u2068'), ("2069", '\u2069') })
            Add(cases, $"TextUtilities.Plain[bidi-{label}]", () => Equal("leftright", TextUtilities.Plain("left" + control + "right")));
        foreach (var (label, input, expected) in new[]
        {
            ("empty", "", ""), ("backslash", "\\", "\\\\"), ("backtick", "`", "\\`"), ("asterisk", "*", "\\*"), ("underscore", "_", "\\_"),
            ("left-brace", "{", "\\{"), ("right-brace", "}", "\\}"), ("left-bracket", "[", "\\["), ("right-bracket", "]", "\\]"),
            ("left-paren", "(", "\\("), ("right-paren", ")", "\\)"), ("hash", "#", "\\#"), ("plus", "+", "\\+"), ("minus", "-", "\\-"),
            ("dot", ".", "\\."), ("bang", "!", "\\!"), ("pipe", "|", "\\|"), ("tilde", "~", "\\~"), ("ampersand", "&", "&amp;"),
            ("left-angle", "<", "&lt;"), ("right-angle", ">", "&gt;"), ("japanese", "記事の本文", "記事の本文"), ("emoji", "😀🚀", "😀🚀")
        }) Add(cases, $"TextUtilities.Escape[{label}]", () => Equal(expected, TextUtilities.EscapeMarkdown(input)));
        Add(cases, "TextUtilities.EscapeCombinedMaliciousLine", () =>
        {
            const string input = " \u202e# 見出し\n[リンク](https://example.com/a-b?q=x&y=z) <b>本文</b>\u0001 ";
            const string expected = "\\# 見出し \\[リンク\\]\\(https://example\\.com/a\\-b?q=x&amp;y=z\\) &lt;b&gt;本文&lt;/b&gt;";
            var actual = TextUtilities.EscapeMarkdown(input);
            Equal(expected, actual);
            Equal(false, actual.Contains('\n'));
            Equal(false, actual.Contains('<'));
        });
    }

    private static void RegisterNetwork(List<TestCase> cases)
    {
        foreach (var (label, input) in new[]
        {
            ("public-domain", "https://example.com/article"), ("public-ipv4", "https://8.8.8.8/article"), ("public-bracketed-ipv6", "https://[2606:4700:4700::1111]/article"),
            ("http-80", "http://example.com:80/a"), ("http-443", "http://example.com:443/a"), ("https-80", "https://example.com:80/a"), ("https-443", "https://example.com:443/a"),
            ("length-8192", "https://example.com/" + new string('a', 8192 - 20)), ("public-domain-query-fragment", "https://example.com/a?q=1#part")
        }) Add(cases, $"NetworkPolicy.UriAccept[{label}]", () =>
        {
            var uri = new Uri(input);
            if (label == "length-8192") Equal(8192, uri.AbsoluteUri.Length);
            NetworkPolicy.ValidateUri(uri);
        });
        foreach (var (label, input) in new[]
        {
            ("relative", "/article"), ("file", "file:///unit/article"), ("ftp", "ftp://example.com/article"), ("javascript", "javascript:unit"),
            ("userinfo", "https://unit-user@example.com/article"), ("length-8193", "https://example.com/" + new string('a', 8193 - 20)),
            ("port-81", "http://example.com:81/a"), ("port-444", "https://example.com:444/a"), ("localhost", "https://localhost/a"),
            ("localhost-case", "https://LOCALHOST/a"), ("localhost-dot", "https://localhost./a"), ("localhost-subdomain", "https://unit.localhost/a"),
            ("local-suffix", "https://unit.local/a"), ("single-label", "https://unit/a"), ("private-ipv4", "https://10.0.0.1/a"),
            ("mapped-private-ipv6", "https://[::ffff:10.0.0.1]/a")
        }) Add(cases, $"NetworkPolicy.UriReject[{label}]", () =>
        {
            var uri = new Uri(input, label == "relative" ? UriKind.Relative : UriKind.Absolute);
            if (label == "length-8193") Equal(8193, uri.AbsoluteUri.Length);
            Throws<InvalidDataException>(() => NetworkPolicy.ValidateUri(uri));
        });
        // Literal application-policy table, not a computed IANA-routability oracle.
        // The research's 192.0/192.2 shorthand denotes intentionally conservative /16 exclusions.
        // Distinguishing neighbors and the narrower 192.0.2/24 documentation block remain explicit.
        (string Address, bool Public)[] ipv4 =
        [
            ("0.0.0.0", false), ("0.100.0.1", false), ("0.255.255.255", false), ("1.0.0.0", true),
            ("9.255.255.255", true), ("10.0.0.0", false), ("10.100.0.1", false), ("10.255.255.255", false), ("11.0.0.0", true),
            ("126.255.255.255", true), ("127.0.0.0", false), ("127.100.0.1", false), ("127.255.255.255", false), ("128.0.0.0", true),
            ("100.63.255.255", true), ("100.64.0.0", false), ("100.100.0.1", false), ("100.127.255.255", false), ("100.128.0.0", true),
            ("169.253.255.255", true), ("169.254.0.0", false), ("169.254.1.1", false), ("169.254.255.255", false), ("169.255.0.0", true),
            ("172.15.255.255", true), ("172.16.0.0", false), ("172.24.0.1", false), ("172.31.255.255", false), ("172.32.0.0", true),
            ("192.167.255.255", true), ("192.168.0.0", false), ("192.168.1.1", false), ("192.168.255.255", false), ("192.169.0.0", true),
            ("191.255.255.255", true), ("192.0.0.0", false), ("192.0.0.1", false), ("192.0.1.0", false), ("192.0.1.255", false),
            ("192.0.2.0", false), ("192.0.2.1", false), ("192.0.2.255", false), ("192.0.3.0", false), ("192.0.255.255", false),
            ("192.1.0.0", true), ("192.1.255.255", true), ("192.2.0.0", false), ("192.2.1.1", false), ("192.2.255.255", false), ("192.3.0.0", true),
            ("198.17.255.255", true), ("198.18.0.0", false), ("198.19.0.1", false), ("198.19.255.255", false), ("198.20.0.0", true),
            ("198.51.99.255", true), ("198.51.100.0", false), ("198.51.100.1", false), ("198.51.100.255", false), ("198.51.101.0", true),
            ("203.0.112.255", true), ("203.0.113.0", false), ("203.0.113.1", false), ("203.0.113.255", false), ("203.0.114.0", true),
            ("223.255.255.255", true), ("224.0.0.0", false), ("239.1.1.1", false), ("240.0.0.0", false), ("255.255.255.255", false),
            ("8.8.8.8", true), ("1.1.1.1", true)
        ];
        foreach (var (address, expected) in ipv4)
        {
            Add(cases, $"NetworkPolicy.IPv4[{address}]", () => Equal(expected, NetworkPolicy.IsPublicAddress(IPAddress.Parse(address))));
            Add(cases, $"NetworkPolicy.MappedIPv4[{address}]", () => Equal(expected, NetworkPolicy.IsPublicAddress(IPAddress.Parse("::ffff:" + address))));
        }
        // Low-2001 exclusion: 2001:0000:: through 2001:01ff:ffff:... (conservative transition policy).
        // 3fff::/16 is a conservative exclusion within otherwise global 2000::/3.
        // Outside the global envelope, link-local/ULA neighbors remain nonpublic too.
        foreach (var (label, address, expected) in new (string, string, bool)[]
        {
            ("unspecified", "::", false), ("loopback", "::1", false),
            ("linklocal-first", "fe80::", false), ("linklocal-interior", "fea0::1", false), ("linklocal-last", "febf:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false),
            ("linklocal-below", "fe7f:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false), ("linklocal-above", "fec0::", false),
            ("unique-fc-first", "fc00::", false), ("unique-fc-interior", "fc80::1", false), ("unique-fd-last", "fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false),
            ("unique-below", "fbff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false), ("unique-above", "fe00::", false),
            ("multicast-first", "ff00::", false), ("multicast-interior", "ff80::1", false), ("multicast-last", "ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false),
            ("global-below", "1fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false), ("global-first", "2000::", true), ("global-interior", "2606:4700:4700::1111", true),
            ("global-last", "3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false), ("global-above", "4000::", false),
            ("transition-below", "2001:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true), ("transition-first", "2002::", false), ("transition-interior", "2002:808:808::1", false),
            ("transition-last", "2002:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false), ("transition-above", "2003::", true),
            ("low2001-first", "2001::", false), ("low2001-interior", "2001:100::1", false), ("low2001-last", "2001:1ff:ffff:ffff:ffff:ffff:ffff:ffff", false), ("low2001-above", "2001:200::", true),
            ("documentation-below", "2001:db7:ffff:ffff:ffff:ffff:ffff:ffff", true), ("documentation-first", "2001:db8::", false),
            ("documentation-interior", "2001:db8:1234::1", false), ("documentation-last", "2001:db8:ffff:ffff:ffff:ffff:ffff:ffff", false), ("documentation-above", "2001:db9::", true),
            ("excluded3fff-below", "3ffe:ffff:ffff:ffff:ffff:ffff:ffff:ffff", true), ("excluded3fff-first", "3fff::", false),
            ("excluded3fff-interior", "3fff:1234::1", false), ("excluded3fff-last", "3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff", false),
            ("mapped-public", "::ffff:8.8.8.8", true), ("mapped-private", "::ffff:10.0.0.1", false)
        }) Add(cases, $"NetworkPolicy.IPv6[{label}]", () => Equal(expected, NetworkPolicy.IsPublicAddress(IPAddress.Parse(address))));
    }

    private static void RegisterFailure(List<TestCase> cases)
    {
        Add(cases, "FailurePolicy.FatalWrapperPreservesMessageAndInner", () =>
        {
            var inner = new IOException("benign-inner");
            var exception = new FatalApplicationException("benign-wrapper", inner);
            Equal("benign-wrapper", exception.Message);
            Same(inner, exception.InnerException);
            Equal(true, FailurePolicy.IsFatal(exception));
        });
        Add(cases, "FailurePolicy.FatalWrapperNoInner", () =>
        {
            var exception = new FatalApplicationException("benign-wrapper");
            Equal("benign-wrapper", exception.Message);
            Same(null, exception.InnerException);
            Equal(true, FailurePolicy.IsFatal(exception));
        });
        foreach (var (label, factory, expected) in new (string, Func<Exception>, bool)[]
        {
            ("FatalApplicationException", () => new FatalApplicationException("unit"), true),
            ("AuthenticationFailedException", () => new AuthenticationFailedException("unit"), true),
            ("CredentialUnavailableException", () => new CredentialUnavailableException("unit"), true),
            ("UnauthorizedAccessException", () => new UnauthorizedAccessException("unit"), true),
            ("InvalidDataException", () => new InvalidDataException("unit"), false), ("FormatException", () => new FormatException("unit"), false),
            ("JsonException", () => new JsonException("unit"), false), ("IOException", () => new IOException("unit"), false),
            ("OperationCanceledException", () => new OperationCanceledException("unit"), false), ("Exception", () => new Exception("unit"), false)
        }) Add(cases, $"FailurePolicy.Direct[{label}]", () => Equal(expected, FailurePolicy.IsFatal(factory())));
        foreach (var (status, expected) in new[] { (401, true), (403, true), (404, true), (400, false), (429, false), (500, false) })
        {
            Add(cases, $"FailurePolicy.Direct[RequestFailedException-{status}]", () => Equal(expected, FailurePolicy.IsFatal(new RequestFailedException(status, "unit"))));
            Add(cases, $"FailurePolicy.Direct[ClientResultException-{status}]", () => Equal(expected, FailurePolicy.IsFatal(ClientError(status, "unit"))));
        }
        foreach (int status in new[] { 401, 403, 404, 500 })
            Add(cases, $"FailurePolicy.Direct[HttpRequestException-{status}]", () => Equal(false, FailurePolicy.IsFatal(new HttpRequestException("unit", null, (HttpStatusCode)status))));
        foreach (var (label, factory, expected) in new (string, Func<Exception>, bool)[]
        {
            ("fatal-inner", () => new Exception("unit", new UnauthorizedAccessException()), true),
            ("nested-fatal", () => new IOException("unit", new Exception("unit", new AuthenticationFailedException("unit"))), true),
            ("all-nonfatal", () => new Exception("unit", new IOException("unit", new FormatException())), false)
        }) Add(cases, $"FailurePolicy.InnerChains[{label}]", () => Equal(expected, FailurePolicy.IsFatal(factory())));
        foreach (var (label, factory, expected) in new (string, Func<Exception>, bool)[]
        {
            ("any-fatal-first", () => new AggregateException(new UnauthorizedAccessException(), new IOException()), true),
            ("any-fatal-last", () => new AggregateException(new IOException(), new UnauthorizedAccessException()), true),
            ("all-nonfatal", () => new AggregateException(new IOException(), new FormatException()), false),
            ("nested-fatal", () => new AggregateException(new IOException(), new AggregateException(new FormatException(), new AuthenticationFailedException("unit"))), true),
            ("empty", () => new AggregateException(), false)
        }) Add(cases, $"FailurePolicy.Aggregate[{label}]", () => Equal(expected, FailurePolicy.IsFatal(factory())));
    }
}
