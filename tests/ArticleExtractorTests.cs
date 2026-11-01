using System.Text;
using FeedToMarkdown;
using static TestSupport;

public static class ArticleExtractorTests
{
    public const string Prose = "この記事では新しい技術を日常の開発に取り入れる方法を紹介します。まず目的を整理し、小さな実験で効果を確かめることが大切です。運用を始めた後も記録を残し、利用者の意見をもとに改善を続けます。具体的な手順と注意点を理解することで、安全で持続可能な仕組みを設計できます。";
    private const string Second = "次の段落では検証の結果を説明します。担当者は複数の事例を比較し、導入前後の違いを記録しました。";
    public static string Paragraph(string text) => "<html><body><article><p>" + text + "</p></article></body></html>";
    private static void Check(string html, string expected, bool truncated = false)
    {
        var result = ArticleExtractor.Extract(html);
        Equal(expected, result.Text);
        Equal(truncated, result.Truncated);
    }
    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("ArticleExtractor." + name, body));
        foreach (var (label, bytes, charset, expected) in new (string, byte[], string?, string)[]
        {
            ("utf8-default", Encoding.UTF8.GetBytes("<p>日本語😀</p>"), null, "<p>日本語😀</p>"),
            ("utf8-bom", new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("日本語")).ToArray(), null, "日本語"),
            ("bom-overrides-declaration", new byte[] { 239, 187, 191 }.Concat(Encoding.UTF8.GetBytes("日本語")).ToArray(), "unknown-charset", "日本語"),
            ("quoted-declaration", Encoding.UTF8.GetBytes("日本語"), "\"utf-8\"", "日本語"),
            ("padded-declaration", Encoding.UTF8.GetBytes("日本語"), "  'utf-8'  ", "日本語"),
            ("meta-lowercase", Encoding.ASCII.GetBytes("<meta charset=shift_jis>").Concat(new byte[] { 0x82, 0xa0 }).ToArray(), null, "<meta charset=shift_jis>あ"),
            ("meta-uppercase", Encoding.ASCII.GetBytes("<META CHARSET='SHIFT_JIS'>").Concat(new byte[] { 0x82, 0xa0 }).ToArray(), "", "<META CHARSET='SHIFT_JIS'>あ"),
            ("declared-over-meta", Encoding.UTF8.GetBytes("<meta charset=shift_jis>日本語"), "utf-8", "<meta charset=shift_jis>日本語"),
            ("shift-jis", [0x93, 0xfa, 0x96, 0x7b, 0x8c, 0xea], "shift_jis", "日本語")
        }) Add($"Decode[{label}]", () => Equal(expected, ArticleExtractor.DecodeHtml(bytes, charset)));
        Add("Decode[unknown-encoding]", () => Throws<InvalidDataException>(() => ArticleExtractor.DecodeHtml([65], "not-a-real-charset")));
        foreach (var (label, start, decoded) in new[] { ("within-4096", 10, true), ("at-scan-boundary", 4096 - "<meta charset=shift_jis>".Length, true), ("crossing-4096", 4089, false), ("beyond-4096", 4096, false) })
            Add($"MetaScan[{label}]", () =>
            {
                string prefix = new string(' ', start) + "<meta charset=shift_jis>";
                byte[] bytes = Encoding.ASCII.GetBytes(prefix).Concat(new byte[] { 0x82, 0xa0 }).ToArray();
                Equal(prefix + (decoded ? "あ" : "��"), ArticleExtractor.DecodeHtml(bytes, null));
            });
        foreach (var (label, html, expected) in new[]
        {
            ("largest-article", "<body><article><p>" + Second + "</p></article><article><p>" + Prose + "</p></article><main>" + new string('M', 500) + "</main></body>", Prose),
            ("article-over-main", "<body><article><p>" + Prose + "</p></article><main><p>" + new string('M', 500) + "</p></main></body>", Prose),
            ("main-over-body", "<body><div>" + new string('B', 500) + "</div><main><p>" + Prose + "</p></main></body>", Prose),
            ("body-fallback", "<body><p>" + Prose + "</p></body>", Prose)
        }) Add($"RootSelection[{label}]", () => Check(html, expected));
        Add("BodyMinimum[100]", () => { Equal(100, ArticleExtractor.MinimumTextCharacters); Check(Paragraph(new string('日', 100)), new string('日', 100)); });
        foreach (var (label, html) in new[]
        {
            ("99", Paragraph(new string('日', 99))), ("empty", Paragraph("")), ("whitespace", Paragraph(" \n\t")),
            ("heading-only", "<article><h1>" + Prose + "</h1></article>"), ("no-body", "<html><head><title>短い記事</title></head></html>"),
            ("pdf-like", "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj"), ("json-like", "{\"title\":\"記事\",\"body\":null}")
        }) Add($"BodyMinimum[{label}]", () => Throws<InvalidDataException>(() => ArticleExtractor.Extract(html)));
        var noise = new List<(string Label, string Html)>();
        foreach (string tag in new[] { "script", "style", "nav", "header", "footer", "aside", "form", "button", "iframe", "svg", "template", "noscript" }) noise.Add(("tag-" + tag, $"<{tag}>NOISE_SENTINEL</{tag}>"));
        noise.AddRange(new (string, string)[]
        {
            ("ads", "<div class='ads'>NOISE_SENTINEL</div>"), ("social", "<div class='social-share'>NOISE_SENTINEL</div>"),
            ("comments", "<div class='comments'>NOISE_SENTINEL</div>"), ("hidden", "<div hidden>NOISE_SENTINEL</div>"),
            ("aria-hidden", "<div aria-hidden='true'>NOISE_SENTINEL</div>"), ("role-navigation", "<div role='navigation'>NOISE_SENTINEL</div>"),
            ("display-none", "<div style='DISPLAY : none'>NOISE_SENTINEL</div>"), ("visibility-hidden", "<div style='visibility : HIDDEN'>NOISE_SENTINEL</div>"),
            ("advertisement", "<div class='advertisement'>NOISE_SENTINEL</div>"), ("ad", "<div class='ad'>NOISE_SENTINEL</div>"), ("comments-id", "<div id='comments'>NOISE_SENTINEL</div>")
        });
        // No recognized p block: otherwise a broken noise selector could be concealed by block-only extraction.
        foreach (var (label, fragment) in noise) Add($"Noise[{label}]", () =>
        {
            var result = ArticleExtractor.Extract("<article><div>" + Prose + "</div>" + fragment + "</article>");
            Equal(Prose, result.Text); Equal(false, result.Truncated); Equal(false, result.Text.Contains("NOISE_SENTINEL", StringComparison.Ordinal));
        });
        Add("NoiseCombined", () => Check("<article><div>" + Prose + "</div>" + string.Concat(noise.Select(n => n.Html)) + "</article>", Prose));
        foreach (var (label, content, expected) in new[]
        {
            ("entities", "日本語 &amp; &#26085; &lt;例&gt; &nbsp; " + Prose, "日本語 & 日 <例> " + Prose),
            ("source-spaces-strong", "Hello <strong>world</strong> " + Prose, "Hello world " + Prose),
            ("source-spaces-anchor", "Hello <a href='https://example.com'>world</a> " + Prose, "Hello world " + Prose),
            ("source-spaces-span", "Hello <span>world</span> " + Prose, "Hello world " + Prose),
            ("adjacent-inline-no-space", "Hello<strong>world</strong> " + Prose, "Helloworld " + Prose)
        }) Add($"TextLayout[{label}]", () => Check(Paragraph(content), expected));
        foreach (string tag in new[] { "p", "div", "blockquote", "pre", "li", "h2", "span" })
            Add($"TextLayout[br-{tag}]", () => Check("<article><" + tag + ">Hello<br>world " + Prose + "</" + tag + "><p>見出し以外の本文です。" + Prose + "</p></article>", "Hello world " + Prose + "\n見出し以外の本文です。" + Prose));
        Add("TextLayout[paragraphs-lf]", () => Check("<article><p>" + Prose + "</p><p>" + Second + "</p></article>", Prose + "\n" + Second));
        foreach (var (label, html, expected) in new[]
        {
            ("list-nesting", "<article><ul><li>" + Prose + "<ul><li>内側の項目です。</li></ul></li><li>次の項目です。</li></ul></article>", Prose + "内側の項目です。\n次の項目です。"),
            ("blockquote-nesting", "<article><blockquote><p>" + Prose + "</p><blockquote><p>引用の続きです。</p></blockquote></blockquote></article>", Prose + "引用の続きです。"),
            ("pre-nesting", "<article><pre>" + Prose + "<span>\n 続きです。</span></pre></article>", Prose + " 続きです。"),
            ("td-nesting", "<article><table><tr><td><p>" + Prose + "</p><p>表の続きです。</p></td></tr></table></article>", Prose + "表の続きです。"),
            ("root-text-fallback", "<article><div> " + Prose + " </div></article>", Prose)
        }) Add($"TextLayout[{label}]", () => Check(html, expected));
        Add("StructuralBodyPreservation[mixed-div-and-paragraph]", () => Check("<article><div>" + Prose + "</div><p>" + Second + "</p></article>", Prose + "\n" + Second));
        Add("StructuralBodyPreservation[multiple-body-blocks]", () => Check("<body><div>" + Prose + "</div><p>短い補足です。</p><div>" + Second + "</div></body>", Prose + "\n短い補足です。\n" + Second));
        foreach (var (label, marker) in new[]
        {
            ("itemprop-false", "<meta itemprop='isAccessibleForFree' content='false'>"), ("data-paywall", "<div data-paywall='false'></div>"),
            ("class-paywall", "<div class='paywall'></div>"), ("id-paywall", "<div id='paywall'></div>"),
            ("jsonld-boolean-false", "<script type='application/ld+json'>{\"isAccessibleForFree\": false}</script>"),
            ("jsonld-string-false", "<script type='application/ld+json'>{\"isAccessibleForFree\": \"false\"}</script>"),
            ("marker-inside-noise", "<nav><div class='paywall'></div></nav>")
        }) Add($"Paywall[{label}]", () => Equal("アクセス制限のある記事です。", Throws<InvalidDataException>(() => ArticleExtractor.Extract("<article>" + marker + "<p>" + Prose + "</p></article>")).Message));
        Add("Free[free-true]", () => Check("<article><meta itemprop='isAccessibleForFree' content='true'><script type='application/ld+json'>{\"isAccessibleForFree\":true}</script><p>" + Prose + "</p></article>", Prose));
        Add("Free[no-marker]", () => Check(Paragraph(Prose), Prose));
        foreach (int length in new[] { 2097152, 2097153 }) Add($"HtmlSize[{length}]", () =>
        {
            string html = Paragraph(Prose) + "<!--" + new string('x', length - Paragraph(Prose).Length - 7) + "-->";
            Equal(length, html.Length);
            if (length == 2097152) Check(html, Prose); else Throws<InvalidDataException>(() => ArticleExtractor.Extract(html));
        });
        foreach (int length in new[] { 19999, 20000, 20001 }) Add($"Truncation[{length}]", () =>
        {
            Equal(20000, ArticleExtractor.MaxInputCharacters);
            string text = SizedProse(length);
            Check(Paragraph(text), length > 20000 ? text[..20000] : text, length > 20000);
        });
        Add("Truncation[surrogate-at-cutoff]", () =>
        {
            string prefix = SizedProse(19999);
            var result = ArticleExtractor.Extract(Paragraph(prefix + "😀終わり。"));
            Equal(prefix, result.Text); Equal(19999, result.Text.Length); Equal(true, result.Truncated); Equal(false, char.IsSurrogate(result.Text[^1]));
        });
        Add("TextLayout[headings-retained-with-prose]", () => Check("<article><h1>技術の解説</h1><p>" + Prose + "</p></article>", "技術の解説\n" + Prose));
        Add("RootSelection[first-main]", () => Check("<main><p>" + Prose + "</p></main><main><p>" + Second + "</p></main>", Prose));
    }
    public static string SizedProse(int length) => string.Concat(Enumerable.Repeat(Prose, length / Prose.Length + 1))[..length];
}
