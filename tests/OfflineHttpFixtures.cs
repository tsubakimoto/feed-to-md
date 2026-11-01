using System.Net;
using System.Net.Http.Headers;

public static class OfflineHttpFixtures
{
    public sealed record RequestSnapshot(string Method, string Uri, string Accept, string UserAgent, bool CanBeCanceled);
    public sealed class Handler : HttpMessageHandler
    {
        public readonly List<RequestSnapshot> Requests = [];
        public readonly List<ProbeContent> RequestProbes = [];
        public readonly List<Response> Responses = [];
        public new Func<int, CancellationToken, Task<HttpResponseMessage>>? Send;
        public CancellationToken LastToken;
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            LastToken = token;
            Requests.Add(new(request.Method.Method, request.RequestUri!.AbsoluteUri, string.Join(",", request.Headers.Accept.Select(a => a.ToString())), request.Headers.UserAgent.ToString(), token.CanBeCanceled));
            // A fake-only empty content probe makes request disposal observable without reflection.
            var probe = new ProbeContent(new CountingStream([]));
            request.Content = probe;
            RequestProbes.Add(probe);
            int index = Requests.Count - 1;
            return Send is null ? Task.FromResult<HttpResponseMessage>(Responses[index]) : Send(index, token);
        }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
    public sealed class Response : HttpResponseMessage
    {
        public bool Disposed;
        public readonly ProbeContent Probe;
        public Response(byte[] bytes, string? media = "text/html", string? charset = "utf-8", long? length = null, HttpStatusCode status = HttpStatusCode.OK, int[]? chunks = null) : base(status)
        {
            Probe = new ProbeContent(new CountingStream(bytes, chunks));
            Content = Probe;
            if (media is not null) Content.Headers.ContentType = new MediaTypeHeaderValue(media) { CharSet = charset };
            if (length is not null) Content.Headers.ContentLength = length;
        }
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
    }
    public sealed class ProbeContent : HttpContent
    {
        public readonly CountingStream Stream;
        public bool Disposed;
        public int StreamRequests;
        public ProbeContent(CountingStream stream) => Stream = stream;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new InvalidOperationException("Fake response must be read as a stream.");
        protected override Task<Stream> CreateContentReadStreamAsync() { StreamRequests++; return Task.FromResult<Stream>(Stream); }
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return CreateContentReadStreamAsync(); }
        protected override void Dispose(bool disposing) { if (disposing) { Disposed = true; Stream.Dispose(); } base.Dispose(disposing); }
    }
    public sealed class CountingStream : Stream
    {
        private readonly byte[] bytes;
        private readonly int[] chunks;
        private int offset;
        public int ReadCalls;
        public int BytesRead;
        public bool Disposed;
        public CancellationToken LastToken { get; private set; }
        public bool BlockRead;
        public readonly TaskCompletionSource<bool> ReadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CountingStream(byte[] bytes, int[]? chunks = null) { this.bytes = bytes; this.chunks = chunks ?? [16384]; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => offset; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            LastToken = token;
            token.ThrowIfCancellationRequested();
            ReadCalls++;
            if (BlockRead)
            {
                var canceled = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = token.Register(() => canceled.TrySetCanceled(token));
                ReadEntered.TrySetResult(true);
                return await canceled.Task;
            }
            int count = Math.Min(Math.Min(buffer.Length, chunks[(ReadCalls - 1) % chunks.Length]), bytes.Length - offset);
            bytes.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            BytesRead += count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        protected override void Dispose(bool disposing) { if (disposing) Disposed = true; base.Dispose(disposing); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
