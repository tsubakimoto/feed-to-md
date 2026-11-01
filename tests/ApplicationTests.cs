using FeedToMarkdown;
using static TestSupport;

public static class ApplicationTests
{
    private static readonly object ConsoleGate = new();
    private const string ExpectedHelp = "RSSの記事を分類し日本語要約付きMarkdownに保存します。\n"
        + "dotnet run --file .\\app.cs -- [--count <正の整数>] [--output-dir <基点>] [--help]\n"
        + "既定件数: 10、出力基点: 作業ディレクトリ。出力: yyyy\\MM\\yyyyMMddHHmm.md (Asia/Tokyo、上書き禁止)。\n"
        + "設定: 環境変数 AZURE_AI_PROJECT_ENDPOINT、AZURE_AI_MODEL_DEPLOYMENT_NAME。DefaultAzureCredentialで認証。\n"
        + "公開HTML本文をFoundryへ送信します。JS/PDF/有料記事の回避は行いません。個別失敗は記事を保持します。";
    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("Application." + name, body));
        Add("HelpText", () => Equal(ExpectedHelp, Application.HelpText));
        foreach (var (label, arguments) in new (string, Func<string, string[]>)[]
        {
            ("alone", _ => ["--help"]),
            ("help-with-invalid-option", root => ["--output-dir", root, "--unsupported", "PRIVATE_ARGUMENT", "--help"]),
            ("help-with-invalid-count", root => ["--output-dir", root, "--count", "invalid", "--help"]),
            ("help-with-output-path", root => ["--output-dir", root, "--help"])
        }) Add($"Help[{label}]", () => Capture(arguments, 0, ExpectedHelp + Environment.NewLine, ""));
        foreach (var (label, arguments, diagnostic) in new (string, Func<string, string[]>, string)[]
        {
            ("unknown", root => ["--output-dir", root, "--PRIVATE_ARGUMENT"], "引数が不正です。--help を確認してください。"),
            ("missing-count", root => ["--output-dir", root, "--count"], "--count には値が必要です。"),
            ("invalid-count", root => ["--output-dir", root, "--count", "invalid"], "--count は正の整数で指定してください。"),
            ("duplicate-count", root => ["--output-dir", root, "--count", "1", "--count", "2"], "引数が不正です。--help を確認してください。"),
            ("missing-output", _ => ["--output-dir"], "--output-dir には値が必要です。"),
            ("duplicate-output", root => ["--output-dir", root, "--output-dir", root], "引数が不正です。--help を確認してください。"),
            ("blank-output", _ => ["--output-dir", " \t"], "出力先が空です。")
        }) Add($"ArgumentFailure[{label}]", () => Capture(arguments, 1, "", "エラー: " + diagnostic + Environment.NewLine));
        const string missing = "エラー: 環境変数 AZURE_AI_PROJECT_ENDPOINT と AZURE_AI_MODEL_DEPLOYMENT_NAME が必要です。";
        const string retiredSettings = "{\"Foundry\":{\"ProjectEndpoint\":\"https://unit.services.ai.azure.com/api/projects/demo\",\"ModelDeploymentName\":\"retired-model\"}}";
        Add("EnvironmentMissing", () => CaptureEnvironment(null, null, null, missing + Environment.NewLine));
        Add("EnvironmentBlank", () => CaptureEnvironment(" ", " ", null, missing + Environment.NewLine));
        Add("EnvironmentMissingIgnoresSettingsFile", () => CaptureEnvironment(null, null, retiredSettings, missing + Environment.NewLine));
        Add("EnvironmentInvalidIgnoresSettingsFile", () => CaptureEnvironment("INVALID_PRIVATE_ENDPOINT", "model", retiredSettings,
            "エラー: Foundry Project endpoint は https://<host>/api/projects/<project> 形式で指定してください。" + Environment.NewLine));
        Add("HelpWithoutEnvironment", () => CaptureEnvironment(null, null, null, "", help: true));
        Add("HelpWithInvalidEnvironment", () => CaptureEnvironment("INVALID_PRIVATE_ENDPOINT", "model", "{INVALID_JSON", "", help: true));
        Add("FeedUrl", () =>
        {
            Equal("https://b.hatena.ne.jp/hotentry/it.rss", Application.FeedUrl.AbsoluteUri);
            NetworkPolicy.ValidateUri(Application.FeedUrl); // Syntactic acceptance only: no DNS/download.
        });
    }
    private static void CaptureEnvironment(string? endpoint, string? deployment, string? json, string expectedError, bool help = false)
    {
        lock (ConsoleGate)
        {
            string root = Path.Combine(Path.GetTempPath(), "feed-to-md-application-settings-" + Guid.NewGuid().ToString("N"));
            string originalDirectory = Environment.CurrentDirectory;
            string? originalEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
            string? originalDeployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME");
            Directory.CreateDirectory(root);
            try
            {
                Environment.CurrentDirectory = root;
                if (json is not null) File.WriteAllText(Path.Combine(root, "appsettings.json"), json);
                Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", endpoint);
                Environment.SetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME", deployment);
                Capture(output => help ? ["--output-dir", output, "--help"] : ["--output-dir", output],
                    help ? 0 : 1, help ? ExpectedHelp + Environment.NewLine : "", expectedError);
                Sequence(json is null ? Array.Empty<string>() : new[] { "appsettings.json" },
                    Directory.GetFiles(root).Select(Path.GetFileName));
                Equal(0, Directory.GetDirectories(root).Length);
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
                Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", originalEndpoint);
                Environment.SetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME", originalDeployment);
                Directory.Delete(root, recursive: true);
            }
            Equal(originalDirectory, Environment.CurrentDirectory);
            Equal(originalEndpoint, Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT"));
            Equal(originalDeployment, Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME"));
        }
    }

    private static void Capture(Func<string, string[]> arguments, int expectedExit, string expectedOutput, string expectedError)
    {
        lock (ConsoleGate)
        {
            string root = Path.Combine(Path.GetTempPath(), "feed-to-md-application-unused-" + Guid.NewGuid().ToString("N"));
            TextWriter originalOutput = Console.Out, originalError = Console.Error;
            using var output = new StringWriter(); using var error = new StringWriter();
            try
            {
                Console.SetOut(output); Console.SetError(error);
                Equal(expectedExit, Application.RunAsync(arguments(root)).GetAwaiter().GetResult());
                Equal(expectedOutput, output.ToString()); Equal(expectedError, error.ToString()); Equal(false, Directory.Exists(root));
            }
            finally
            {
                Console.SetOut(originalOutput); Console.SetError(originalError);
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            Same(originalOutput, Console.Out); Same(originalError, Console.Error);
        }
    }
}
