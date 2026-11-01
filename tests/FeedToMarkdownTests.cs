#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property PublishAot=false
#:include ..\FeedToMarkdown.cs
#:include TestSupport.cs
#:include LeafContractTests.cs
#:include ConfigurationTests.cs
#:include FeedParserTests.cs
#:include ArticleExtractorTests.cs
#:include AnalysisParserTests.cs
#:include OfflineHttpFixtures.cs
#:include PublicHttpDownloaderTests.cs
#:include Phase3Fixtures.cs
#:include ArticlePipelineTests.cs
#:include MarkdownDocumentTests.cs
#:include ApplicationTests.cs

using static TestSupport;

public static class TestRunner
{
    public static int Main(string[] args) => RunCommand(Registry(), args, Console.Out, Console.Error);
    public static int RunCommand(IReadOnlyList<TestCase> cases, IReadOnlyList<string> args, TextWriter output, TextWriter errorOutput)
    {
        try { return Execute(cases, ParseArguments(args), output).ExitCode; }
        catch (ArgumentException error)
        {
            errorOutput.WriteLine(error.Message); // Only runner-owned diagnostics.
            return 2;
        }
    }
    public static IReadOnlyList<TestCase> Registry()
    {
        var cases = new List<TestCase>();
        RegisterHarness(cases);
        LeafContractTests.Register(cases);
        ConfigurationTests.Register(cases);
        FeedParserTests.Register(cases);
        ArticleExtractorTests.Register(cases);
        AnalysisParserTests.Register(cases);
        PublicHttpDownloaderTests.Register(cases);
        ArticlePipelineTests.Register(cases);
        MarkdownDocumentTests.Register(cases);
        ApplicationTests.Register(cases);
        cases.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        if (cases.Count == 0 || cases.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != cases.Count)
            throw new ArgumentException("Registry must be nonempty and unique.");
        return cases;
    }
    public static RunnerOptions ParseArguments(IReadOnlyList<string> args)
    {
        string? filter = null;
        bool list = false;
        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--list-tests":
                    if (list) throw new ArgumentException("Duplicate --list-tests.");
                    list = true;
                    break;
                case "--filter":
                    if (filter is not null) throw new ArgumentException("Duplicate --filter.");
                    if (++i == args.Count) throw new ArgumentException("Missing --filter value.");
                    if (string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException("Empty --filter value.");
                    filter = args[i];
                    break;
                default: throw new ArgumentException("Unknown runner option.");
            }
        }
        return new(filter, list);
    }
    public static IReadOnlyList<TestCase> Select(IReadOnlyList<TestCase> cases, string? filter)
    {
        var selected = cases.Where(c => filter is null || c.Name.StartsWith(filter, StringComparison.Ordinal)).ToArray();
        if (selected.Length == 0) throw new ArgumentException("No tests match --filter.");
        return selected;
    }
    public static RunResult Execute(IReadOnlyList<TestCase> cases, RunnerOptions options, TextWriter output)
    {
        var selected = Select(cases, options.Filter);
        if (options.List)
        {
            foreach (var test in selected) output.WriteLine(test.Name);
            return new(0, 0);
        }
        int passed = 0, failed = 0;
        foreach (var test in selected)
        {
            try { test.Body(); passed++; output.WriteLine($"PASS {test.Name}"); }
            catch (Exception exception)
            {
                failed++;
                output.WriteLine($"FAIL {test.Name}: {(exception is TestAssertionException ? exception.Message : exception.GetType().Name)}");
            }
        }
        output.WriteLine($"TOTAL {selected.Count}; PASS {passed}; FAIL {failed}; BLOCKED 0");
        return new(passed, failed);
    }
    private static void RegisterHarness(List<TestCase> cases)
    {
        void Add(string name, Action body) => cases.Add(new(name, body));
        TestCase[] Small() => [new("A.one", () => { }), new("A.two", () => { }), new("B.one", () => { })];
        Add("Harness.RegistryIsNonemptyUniqueAndOrdinal", () =>
        {
            var names = Registry().Select(c => c.Name).ToArray();
            Equal(true, names.Length > 0);
            Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
            Sequence(names.Order(StringComparer.Ordinal), names);
            Sequence(names, Registry().Select(c => c.Name));
        });
        foreach (var (label, filter, expected) in new (string, string?, string[])[]
        {
            ("all", null, ["A.one", "A.two", "B.one"]), ("prefix", "A.", ["A.one", "A.two"]),
            ("single-case", "B.one", ["B.one"])
        }) Add($"Harness.Selection[{label}]", () => Sequence(expected, Select(Small(), filter).Select(c => c.Name)));
        Add("Harness.Selection[zero-match]", () =>
        {
            Contains("No tests match", Throws<ArgumentException>(() => Select(Small(), "a.")).Message);
            using var output = new StringWriter();
            using var error = new StringWriter();
            Equal(2, RunCommand(Small(), ["--filter", "a."], output, error));
            Equal("", output.ToString());
            Contains("No tests match", error.ToString());
        });
        foreach (var (label, args, message) in new (string, string[], string)[]
        {
            ("unknown", ["--wat"], "Unknown"), ("missing-filter", ["--filter"], "Missing --filter"),
            ("empty-filter", ["--filter", ""], "Empty --filter"),
            ("duplicate-filter", ["--filter", "A", "--filter", "B"], "Duplicate --filter"),
            ("duplicate-list", ["--list-tests", "--list-tests"], "Duplicate --list-tests")
        }) Add($"Harness.Arguments[{label}]", () =>
        {
            Contains(message, Throws<ArgumentException>(() => ParseArguments(args)).Message);
            using var output = new StringWriter();
            using var error = new StringWriter();
            int calls = 0;
            Equal(2, RunCommand([new("local", () => calls++)], args, output, error));
            Equal(0, calls);
            Equal("", output.ToString());
            Contains(message, error.ToString());
        });
        Add("Harness.ListDoesNotExecute", () =>
        {
            int calls = 0;
            TestCase[] local = [new("A.one", () => calls++), new("B.one", () => calls++)];
            foreach (string? filter in new string?[] { null, "B" })
            {
                using var output = new StringWriter();
                Equal(new RunResult(0, 0), Execute(local, new(filter, true), output));
                Equal(filter is null ? "A.one" + Environment.NewLine + "B.one" + Environment.NewLine : "B.one" + Environment.NewLine, output.ToString());
                Equal(0, calls);
            }
            using var runOutput = new StringWriter();
            Equal(new RunResult(1, 0), Execute(local, new("B", false), runOutput));
            Equal(1, calls);
        });
        Add("Harness.RunAggregatesFailure", () =>
        {
            var calls = new List<int>();
            TestCase[] local = [new("one", () => calls.Add(1)), new("two", () => { calls.Add(2); throw new InvalidOperationException("not logged"); }), new("three", () => calls.Add(3))];
            using var output = new StringWriter();
            var result = Execute(local, new(null, false), output);
            Equal(new RunResult(2, 1), result);
            Equal(1, result.ExitCode);
            Sequence(new[] { 1, 2, 3 }, calls);
            Contains("FAIL two: InvalidOperationException", output.ToString());
            Equal(false, output.ToString().Contains("not logged", StringComparison.Ordinal));
        });
        foreach (var (label, mismatch) in new (string, Action)[]
        {
            ("string", () => Equal("a", "b")), ("sequence", () => Sequence(new[] { 1, 2 }, new[] { 2, 1 })),
            ("bytes", () => Bytes([1, 2], [1, 3])),
            ("exception-type", () => Throws<ArgumentException>(() => throw new InvalidOperationException())),
            ("no-exception", () => Throws<ArgumentException>(() => { })),
            ("exact-derived", () => Throws<ArgumentException>(() => throw new ArgumentNullException("value"))),
            ("assignable-unrelated", () => ThrowsAssignable<ArgumentException>(() => throw new InvalidOperationException())),
            ("assignable-no-exception", () => ThrowsAssignable<ArgumentException>(() => { }))
        }) Add($"Harness.AssertionHelpersRejectMismatch[{label}]", () =>
        {
            bool rejected = false;
            try { mismatch(); }
            catch (TestAssertionException) { rejected = true; }
            if (!rejected) throw new TestAssertionException("Assertion helper accepted an invalid outcome.");
        });
        Add("Harness.ExceptionHelpersAcceptExpected", () =>
        {
            var exact = new ArgumentException("expected");
            if (!ReferenceEquals(exact, Throws<ArgumentException>(() => throw exact)))
                throw new TestAssertionException("Exact exception helper did not return the original exception.");
            var derived = new ArgumentNullException("value");
            if (!ReferenceEquals(derived, ThrowsAssignable<ArgumentException>(() => throw derived)))
                throw new TestAssertionException("Assignable exception helper did not return the original derived exception.");
        });
    }
}
