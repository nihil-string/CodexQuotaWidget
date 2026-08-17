using System.Net;
using System.Text;
using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class CodexUsageClientTests
{
    [Fact]
    public void ParsesCurrentWhamUsageShape()
    {
        var json = Encoding.UTF8.GetBytes("""
            {
              "plan_type": "pro",
              "rate_limit": {
                "primary_window": {
                  "used_percent": 38.5,
                  "limit_window_seconds": 18000,
                  "reset_at": 1783832400
                },
                "secondary_window": {
                  "used_percent": 12,
                  "limit_window_seconds": 604800,
                  "reset_at": 1784419200
                }
              }
            }
            """);

        var parsed = CodexUsageClient.TryParseUsageJson(json, out var snapshot);

        Assert.True(parsed);
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.FiveHour);
        Assert.NotNull(snapshot.Weekly);
        Assert.Equal(38.5, snapshot.FiveHour.UsedPercent);
        Assert.Equal(12, snapshot.Weekly.UsedPercent);
        Assert.Equal(61.5, snapshot.FiveHour.RemainingPercent);
        Assert.Equal(88, snapshot.Weekly.RemainingPercent);
    }

    [Fact]
    public void ParsesWeeklyOnlyUsage()
    {
        var json = Encoding.UTF8.GetBytes("""
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 51,
                  "limit_window_seconds": 604800,
                  "reset_at": 1784419200
                },
                "secondary_window": null
              }
            }
            """);

        var parsed = CodexUsageClient.TryParseUsageJson(json, out var snapshot);

        Assert.True(parsed);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.FiveHour);
        Assert.NotNull(snapshot.Weekly);
        Assert.Equal(51, snapshot.Weekly.UsedPercent);
    }

    [Fact]
    public void RejectsResponsesWithoutRecognizedWindows()
    {
        var json = Encoding.UTF8.GetBytes("""{"rate_limit":{"primary_window":null}}""");

        Assert.False(CodexUsageClient.TryParseUsageJson(json, out var snapshot));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"unexpected\"")]
    public void RejectsNonObjectUsagePayloadWithoutThrowing(string json)
    {
        Assert.False(CodexUsageClient.TryParseUsageJson(Encoding.UTF8.GetBytes(json), out var snapshot));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("{\"tokens\":{\"access_token\":123}}")]
    [InlineData("{\"tokens\":{\"access_token\":\"token\",\"account_id\":{}}}")]
    public async Task FetchAsyncRejectsInvalidCredentialTypes(string authJson)
    {
        using var fixture = new ClientFixture(authJson, new UnexpectedRequestHandler());

        var exception = await Assert.ThrowsAsync<UsageFetchException>(() => fixture.Client.FetchAsync());

        Assert.Equal(UsageFailureKind.Credentials, exception.Kind);
    }

    [Fact]
    public async Task FetchAsyncRejectsOversizedCredentialFileBeforeSendingRequest()
    {
        using var fixture = new ClientFixture(new string(' ', 256 * 1024 + 1), new UnexpectedRequestHandler());

        var exception = await Assert.ThrowsAsync<UsageFetchException>(() => fixture.Client.FetchAsync());

        Assert.Equal(UsageFailureKind.Credentials, exception.Kind);
        Assert.Equal("Codex 登录信息超过安全大小限制", exception.Message);
    }

    [Fact]
    public async Task FetchAsyncPreservesCallerCancellation()
    {
        var handler = new BlockingRequestHandler();
        using var fixture = new ClientFixture(ValidAuthJson, handler);
        using var cancellation = new CancellationTokenSource();

        var fetch = fixture.Client.FetchAsync(cancellation.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
    }

    [Fact]
    public async Task FetchAsyncStopsReadingChunkedResponsesAtSizeLimit()
    {
        var responseStream = new GeneratedReadStream(2 * 1024 * 1024);
        var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(responseStream)
        });
        using var fixture = new ClientFixture(ValidAuthJson, handler);

        var exception = await Assert.ThrowsAsync<UsageFetchException>(() => fixture.Client.FetchAsync());

        Assert.Equal(UsageFailureKind.InvalidResponse, exception.Kind);
        Assert.InRange(responseStream.BytesRead, 1, 1024 * 1024 + 1);
    }

    private const string ValidAuthJson =
        "{\"tokens\":{\"access_token\":\"secret-token\",\"account_id\":\"account\"}}";

    private sealed class ClientFixture : IDisposable
    {
        private readonly string _directory;

        public ClientFixture(string authJson, HttpMessageHandler handler)
        {
            _directory = Path.Combine(Path.GetTempPath(), $"CodexQuotaWidget.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            var authPath = Path.Combine(_directory, "auth.json");
            File.WriteAllText(authPath, authJson);
            Client = new CodexUsageClient(handler, authPath);
        }

        public CodexUsageClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class UnexpectedRequestHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Invalid credentials must fail before sending a request.");
    }

    private sealed class BlockingRequestHandler : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }

    private sealed class GeneratedReadStream(long length) : Stream
    {
        private long _remaining = length;

        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, _remaining);
            Array.Clear(buffer, offset, read);
            _remaining -= read;
            BytesRead += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..read].Clear();
            _remaining -= read;
            BytesRead += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
