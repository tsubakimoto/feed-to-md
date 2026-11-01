using FeedToMarkdown;
using static TestSupport;

public static class ConfigurationTests
{
    private const string Endpoint = "https://unit.services.ai.azure.com/api/projects/demo";
    private const string Missing = "環境変数 AZURE_AI_PROJECT_ENDPOINT と AZURE_AI_MODEL_DEPLOYMENT_NAME が必要です。";

    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("FoundryConfiguration.FromEnvironment." + name, body));
        Add("ValidAndReload", () => WithEnvironment(Endpoint, "model-one", () =>
        {
            var first = FoundryConfiguration.FromEnvironment();
            Equal(new Uri(Endpoint), first.Endpoint); Equal("model-one", first.Deployment);
            Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", "https://other.services.ai.azure.com/api/projects/other");
            Environment.SetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME", "model-two");
            var second = FoundryConfiguration.FromEnvironment();
            Equal(new Uri("https://other.services.ai.azure.com/api/projects/other"), second.Endpoint);
            Equal("model-two", second.Deployment);
        }));
        Add("Trim", () => WithEnvironment(" " + Endpoint + " ", " model-one ", () =>
        {
            var result = FoundryConfiguration.FromEnvironment();
            Equal(new Uri(Endpoint), result.Endpoint); Equal("model-one", result.Deployment);
        }));
        foreach (var (label, endpoint, deployment) in new (string, string?, string?)[]
        {
            ("both-unset", null, null), ("endpoint-unset", null, "model-one"),
            ("endpoint-empty", "", "model-one"), ("endpoint-blank", " \t", "model-one"),
            ("deployment-unset", Endpoint, null), ("deployment-empty", Endpoint, ""),
            ("deployment-blank", Endpoint, " \t")
        }) Add($"Missing[{label}]", () => WithEnvironment(endpoint, deployment, () =>
            Equal(Missing, Throws<ArgumentException>(() => FoundryConfiguration.FromEnvironment()).Message)));
        Add("EndpointValidation", () => WithEnvironment("INVALID_PRIVATE_ENDPOINT", "model-one", () =>
            Equal("Foundry Project endpoint は https://<host>/api/projects/<project> 形式で指定してください。",
                Throws<ArgumentException>(() => FoundryConfiguration.FromEnvironment()).Message)));
        Add("DeploymentValidation", () => WithEnvironment(Endpoint, new string('x', 257), () =>
            Equal("モデル Deployment 名が不正です。", Throws<ArgumentException>(() => FoundryConfiguration.FromEnvironment()).Message)));
        Add("IgnoresSettingsFile", () => WithEnvironment(Endpoint, "model-one", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "feed-to-md-retired-settings-" + Guid.NewGuid().ToString("N"));
            string originalDirectory = Environment.CurrentDirectory;
            Directory.CreateDirectory(root);
            try
            {
                Environment.CurrentDirectory = root;
                File.WriteAllText(Path.Combine(root, "appsettings.json"), "{INVALID_JSON");
                var result = FoundryConfiguration.FromEnvironment();
                Equal(new Uri(Endpoint), result.Endpoint); Equal("model-one", result.Deployment);
            }
            finally { Environment.CurrentDirectory = originalDirectory; Directory.Delete(root, recursive: true); }
        }));
    }

    private static void WithEnvironment(string? endpoint, string? deployment, Action body)
    {
        string? originalEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
        string? originalDeployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME");
        try
        {
            Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", endpoint);
            Environment.SetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME", deployment);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", originalEndpoint);
            Environment.SetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME", originalDeployment);
        }
    }
}
