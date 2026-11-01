using System.Xml;
using FeedToMarkdown;
using static TestSupport;

public static class FeedParserTests
{
    private const string Open = "<rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#' xmlns='http://purl.org/rss/1.0/'>";
    private static string Item(string id, string? title, string? link) => $"<item rdf:about='{id}'>" + (title is null ? "" : $"<title>{title}</title>") + (link is null ? "" : $"<link>{link}</link>") + "</item>";
    private static string Wrap(string items, string? sequence = null) => Open + (sequence is null ? "" : "<channel><items><rdf:Seq>" + sequence + "</rdf:Seq></items></channel>") + items + "</rdf:RDF>";
    private static string Li(string id) => $"<rdf:li rdf:resource='{id}'/>";
    private static string One(string? title = "日本語の記事", string? link = "https://example.com/article") => Wrap(Item("urn:bookmark:one", title, link));
    private static void Check(string xml, int count, params (string Title, string Url)[] expected)
    {
        var actual = FeedParser.Parse(xml, count);
        Sequence(expected.Select(e => e.Title), actual.Select(e => e.Title));
        Sequence(expected.Select(e => e.Url), actual.Select(e => e.Url.AbsoluteUri));
    }
    public static void Register(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new("FeedParser." + name, body));
        string items = Item("urn:c", "記事三", "https://example.com/c") + Item("urn:a", "記事一", "https://example.com/a") + Item("urn:b", "記事二", "https://example.com/b");
        string ordered = Wrap(items, Li("urn:b") + Li("urn:c") + Li("urn:a"));
        foreach (var (label, count, expected) in new (string, int, (string, string)[])[]
        {
            ("count-one", 1, [("記事二", "https://example.com/b")]),
            ("count-exact", 3, [("記事二", "https://example.com/b"), ("記事三", "https://example.com/c"), ("記事一", "https://example.com/a")]),
            ("count-shortfall", 5, [("記事二", "https://example.com/b"), ("記事三", "https://example.com/c"), ("記事一", "https://example.com/a")])
        }) Add($"SequenceOrder[{label}]", () => Check(ordered, count, expected));
        Add("NoSequencePhysicalOrder", () => Check(Wrap(items), 3, ("記事三", "https://example.com/c"), ("記事一", "https://example.com/a"), ("記事二", "https://example.com/b")));
        Add("TitleEntitiesAndWhitespace", () => Check(One(" \n 技術 &amp; &#26085;&#26412; &#32; 記事\t", " https://example.com/entity "), 1, ("技術 & 日本 記事", "https://example.com/entity")));
        Add("TitleBoundary[length-4096]", () => Check(One(new string('日', 4096)), 1, (new string('日', 4096), "https://example.com/article")));
        foreach (var (label, title) in new (string, string?)[] { ("length-4097", new string('日', 4097)), ("missing", null), ("empty", ""), ("whitespace", " \n\t") })
            Add($"TitleBoundary[{label}]", () => Throws<FormatException>(() => FeedParser.Parse(One(title), 1)));
        foreach (var (label, link, policy) in new (string, string?, bool)[]
        {
            ("missing", null, false), ("empty", "", false), ("malformed", "https://[broken", false), ("relative", "articles/relative", false),
            ("localhost", "https://localhost/a", true), ("private-ip", "https://10.1.2.3/a", true), ("unsafe-scheme", "ftp://example.com/a", true), ("unsafe-port", "https://example.com:444/a", true)
        }) Add($"LinkReject[{label}]", () => { if (policy) Throws<InvalidDataException>(() => FeedParser.Parse(One(link: link), 1)); else Throws<FormatException>(() => FeedParser.Parse(One(link: link), 1)); });
        foreach (var (label, count) in new[] { ("zero", 0), ("negative", -1) }) Add($"Count[{label}]", () => Equal("count", Throws<ArgumentOutOfRangeException>(() => FeedParser.Parse(One(), count)).ParamName));
        foreach (var (label, xml) in new[]
        {
            ("empty-rdf", Wrap("")), ("no-items", Wrap("<channel/>")), ("empty-seq", Wrap(items, "")),
            ("wrong-root", "<root/>"), ("wrong-rdf-namespace", One().Replace("http://www.w3.org/1999/02/22-rdf-syntax-ns#", "urn:wrong")),
            ("wrong-rss-namespace", One().Replace("http://purl.org/rss/1.0/", "urn:wrong")), ("rss2", "<rss version='2.0'><channel><item><title>記事</title></item></channel></rss>"),
            ("missing-about", Wrap("<item><title>記事</title><link>https://example.com/a</link></item>", Li("urn:a"))),
            ("missing-resource", Wrap(items, "<rdf:li/>")), ("dangling-resource", Wrap(items, Li("urn:absent")))
        }) Add($"StructureReject[{label}]", () => Throws<FormatException>(() => FeedParser.Parse(xml, 1)));
        Add("DuplicateIds", () => Throws<ArgumentException>(() => FeedParser.Parse(Wrap(Item("urn:a", "一", "https://example.com/a") + Item("urn:a", "二", "https://example.com/b"), Li("urn:a")), 1)));
        foreach (var (label, xml) in new[] { ("broken-tag", Open + "<item></rdf:RDF>"), ("trailing-root", One() + "<extra/>") })
            Add($"MalformedXml[{label}]", () => Throws<XmlException>(() => FeedParser.Parse(xml, 1)));
        foreach (var (label, length) in new[] { ("exact-limit", 2097152), ("over-limit", 2097153) }) Add($"XmlSize[{label}]", () =>
        {
            Equal(2097152, FeedParser.MaxXmlCharacters);
            string xml = One().Replace("</rdf:RDF>", new string(' ', length - One().Length) + "</rdf:RDF>");
            Equal(length, xml.Length);
            if (length == 2097152) Check(xml, 1, ("日本語の記事", "https://example.com/article")); else Throws<XmlException>(() => FeedParser.Parse(xml, 1));
        });
        foreach (var (label, dtd) in new[] { ("inline-doctype", "<!DOCTYPE rdf:RDF [<!ENTITY harmless 'test'>]>"), ("external-entity", "<!DOCTYPE rdf:RDF [<!ENTITY harmless SYSTEM 'urn:test:blocked'>]>") })
            Add($"DtdDenied[{label}]", () => Throws<XmlException>(() => FeedParser.Parse(dtd + One(), 1)));
        Add("SelectionLaziness[invalid-selected]", () => Throws<FormatException>(() => FeedParser.Parse(Wrap(Item("urn:bad", "", "https://example.com/b") + items), 1)));
        Add("SelectionLaziness[invalid-after-prefix]", () => Check(Wrap(Item("urn:ok", "正常な記事", "https://example.com/ok") + Item("urn:bad", "", null), Li("urn:ok") + Li("urn:bad")), 1, ("正常な記事", "https://example.com/ok")));
        Add("SelectionLaziness[duplicate-after-prefix]", () => Throws<ArgumentException>(() => FeedParser.Parse(Wrap(items + Item("urn:a", "重複", "https://example.com/duplicate"), Li("urn:b")), 1)));
        Add("SelectionLaziness[malformed-after-prefix]", () => Throws<XmlException>(() => FeedParser.Parse(Wrap(items + "<item>"), 1)));
        Add("NoSequenceMissingAbout", () => Check(Wrap("<item><title>識別子なし</title><link>https://example.com/no-id</link></item>"), 1, ("識別子なし", "https://example.com/no-id")));
    }
}
