using System.Text.Json;

public sealed class TestAssertionException(string message) : Exception(message);
public sealed record TestCase(string Name, Action Body);
public sealed record RunnerOptions(string? Filter, bool List);
public sealed record RunResult(int Passed, int Failed)
{
    public int ExitCode => Failed == 0 ? 0 : 1;
}

public static class TestSupport
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new TestAssertionException($"Expected {Safe(expected)}; actual {Safe(actual)}");
    }
    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        var expectedItems = expected.ToArray();
        var actualItems = actual.ToArray();
        if (expectedItems.Length != actualItems.Length)
            throw new TestAssertionException($"Expected sequence count {expectedItems.Length}; actual {actualItems.Length}");
        for (int i = 0; i < expectedItems.Length; i++)
            if (!EqualityComparer<T>.Default.Equals(expectedItems[i], actualItems[i]))
                throw new TestAssertionException($"Sequence index {i}: expected {Safe(expectedItems[i])}; actual {Safe(actualItems[i])}");
    }
    public static void Bytes(byte[] expected, byte[] actual) => Sequence(expected, actual);
    public static void Same(object? expected, object? actual)
    {
        if (!ReferenceEquals(expected, actual)) throw new TestAssertionException("Reference identity mismatch");
    }
    public static void Contains(string expected, string actual)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal))
            throw new TestAssertionException($"Missing fragment {Safe(expected)}; actual {Safe(actual)}");
    }
    public static T Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (Exception exception)
        {
            if (exception.GetType() == typeof(T)) return (T)exception;
            throw new TestAssertionException($"Expected exception {typeof(T).Name}; actual {exception.GetType().Name}");
        }
        throw new TestAssertionException($"Expected exception {typeof(T).Name}; no exception");
    }
    public static T ThrowsAssignable<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        catch (Exception exception)
        {
            throw new TestAssertionException($"Expected exception assignable to {typeof(T).Name}; actual {exception.GetType().Name}");
        }
        throw new TestAssertionException($"Expected exception assignable to {typeof(T).Name}; no exception");
    }

    public static System.ClientModel.ClientResultException ClientError(int status, string message) =>
        new(message, new StatusResponse(status));

    private sealed class StatusResponse(int status) : System.ClientModel.Primitives.PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "Synthetic response";
        protected override System.ClientModel.Primitives.PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.Empty;
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);
        public override void Dispose() => ContentStream?.Dispose();
    }

    private static string Safe<T>(T value)
    {
        // Test fixtures are synthetic. Bound and escape all diagnostics; never log exception messages.
        var text = value?.ToString() ?? "<null>";
        return JsonSerializer.Serialize(text.Length > 180 ? text[..180] + "…" : text);
    }
}
