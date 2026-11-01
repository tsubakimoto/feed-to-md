using System.Text.Json;
using FeedToMarkdown;
using static TestSupport;

public static class AnalysisParserTests
{
    public const string LiteralMarkup = "日本語の要約 # [リンク](https://example.com) <b>&本文</b>";
    private const string Summary = "この記事は新しい技術の仕組みを説明しています。導入時の注意点と具体的な活用例を紹介しています。";
    private static string Json(string summary) => "{\"category\":\"Web\",\"summary\":" + JsonSerializer.Serialize(summary) + "}";
    private static void Check(string json, string category, string summary)
    {
        var result = AnalysisParser.Parse(json);
        Equal(category, result.Category);
        Equal(summary, result.Summary);
    }
    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("AnalysisParser." + name, body));
        foreach (var (label, category) in new[] { ("ai", "AI・機械学習"), ("development", "開発"), ("web", "Web"), ("cloud", "Cloud・Infrastructure"), ("security", "Security"), ("data", "Data・DB"), ("hardware", "Hardware"), ("other", "その他") })
            Add($"ValidCategory[{label}]", () => Check("{\"category\":" + JsonSerializer.Serialize(category) + ",\"summary\":" + JsonSerializer.Serialize(Summary) + "}", category, Summary));
        foreach (var (label, property) in new[] { ("unknown", "\"category\":\"Robotics\","), ("missing", ""), ("null", "\"category\":null,"), ("number", "\"category\":17,"), ("object", "\"category\":{},") })
            Add($"CategoryFallback[{label}]", () => Check("{" + property + "\"summary\":" + JsonSerializer.Serialize(Summary) + "}", "その他", Summary));
        Add("SummaryNormalized", () => Check(Json(" \t日本語\r\nの\u0001要約\u202eです。  実例を説明します。 \n"), "Web", "日本語 の要約です。 実例を説明します。"));
        foreach (var (label, summary) in new[] { ("hiragana", "あいうえお"), ("katakana", "アイウエオ"), ("cjk", "技術解説"), ("short-valid", "日") })
            Add($"Japanese[{label}]", () => Check(Json(summary), "Web", summary));
        foreach (var (label, json) in new[] { ("null", "null"), ("array", "[]"), ("string", "\"日本語\""), ("number", "7") })
            Add($"RootReject[{label}]", () => Throws<FormatException>(() => AnalysisParser.Parse(json)));
        foreach (var (label, property) in new[] { ("missing", ""), ("null", "\"summary\":null"), ("number", "\"summary\":12"), ("object", "\"summary\":{}"), ("empty", "\"summary\":\"\""), ("whitespace", "\"summary\":\" \\t\\r\\n\""), ("control-only", "\"summary\":\"\\u0001\\u202e\""), ("non-japanese", "\"summary\":\"English only with emoji 😀\"") })
            Add($"SummaryReject[{label}]", () => Throws<FormatException>(() => AnalysisParser.Parse("{" + property + "}")));
        foreach (var (label, json, guard) in new[] { ("invalid", "{not-json}", false), ("fenced", "```json\n" + Json(Summary) + "\n```", false), ("trailing-data", Json(Summary) + " false", false), ("empty-response", "", true), ("whitespace-response", " \t\r\n", true) })
            Add($"SyntaxReject[{label}]", () => { if (guard) Throws<FormatException>(() => AnalysisParser.Parse(json)); else ThrowsAssignable<JsonException>(() => AnalysisParser.Parse(json)); });
        foreach (int length in new[] { 8192, 8193 }) Add($"ResponseSize[{length}]", () =>
        {
            Equal(8192, AnalysisParser.MaxResponseCharacters);
            string json = Json(Summary).PadRight(length, ' ');
            Equal(length, json.Length);
            if (length == 8192) Check(json, "Web", Summary); else Throws<FormatException>(() => AnalysisParser.Parse(json));
        });
        foreach (int depth in new[] { 8, 9 }) Add($"Depth[{depth}]", () =>
        {
            string json = Json(Summary)[..^1] + ",\"ignored\":" + new string('[', depth - 1) + "0" + new string(']', depth - 1) + "}";
            if (depth == 8) Check(json, "Web", Summary); else ThrowsAssignable<JsonException>(() => AnalysisParser.Parse(json));
        });
        foreach (int length in new[] { 2000, 2001 }) Add($"SummaryLength[{length}]", () =>
        {
            // Literal Japanese avoids escaped-JSON growth masking the summary-length guard.
            string summary = new string('日', length);
            string json = "{\"category\":\"Web\",\"summary\":\"" + summary + "\"}";
            if (length == 2000) Check(json, "Web", summary); else Throws<FormatException>(() => AnalysisParser.Parse(json));
        });
        Add("MarkupRemainsLiteral", () => Check(Json(LiteralMarkup), "Web", LiteralMarkup));
        Add("SummaryLength[normalization-before-limit]", () => Check("{\"summary\":\"" + new string(' ', 2500) + "日\"}", "その他", "日"));
        Add("CategoryFallback[array]", () => Check("{\"category\":[],\"summary\":\"日\"}", "その他", "日"));
        Add("CategoryFallback[boolean]", () => Check("{\"category\":true,\"summary\":\"日\"}", "その他", "日"));
    }
}
