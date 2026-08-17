using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

public enum UsageFailureKind
{
    Authentication,
    Credentials,
    Network,
    InvalidResponse
}

public sealed class UsageFetchException(UsageFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public UsageFailureKind Kind { get; } = kind;
}

public sealed class CodexUsageClient : IDisposable
{
    private static readonly Uri UsageEndpoint = new("https://chatgpt.com/backend-api/wham/usage");
    private const int MaximumResponseBytes = 1024 * 1024;
    private const int ResponseBufferBytes = 16 * 1024;
    private const int MaximumCredentialsBytes = 256 * 1024;
    private const int CredentialsBufferBytes = 4 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly string _authPath;

    public CodexUsageClient(string? codexHome = null)
        : this(CreateDefaultHandler(), ResolveAuthPath(codexHome))
    {
    }

    internal CodexUsageClient(HttpMessageHandler handler, string authPath)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(authPath);

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaWidget/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _authPath = authPath;
    }

    private static HttpMessageHandler CreateDefaultHandler() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

    private static string ResolveAuthPath(string? codexHome)
    {
        var configuredHome = string.IsNullOrWhiteSpace(codexHome)
            ? Environment.GetEnvironmentVariable("CODEX_HOME")
            : codexHome;
        var root = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
        return Path.Combine(root, "auth.json");
    }

    public async Task<RateLimitSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var credentials = LoadCredentials();
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(RequestTimeout);
        var requestToken = timeoutCancellation.Token;

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        if (!string.IsNullOrWhiteSpace(credentials.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            throw new UsageFetchException(UsageFailureKind.Network, "无法连接 Codex 额度服务", exception);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new UsageFetchException(UsageFailureKind.Authentication, "Codex 登录已失效，请在 Codex 中重新登录");
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                throw new UsageFetchException(UsageFailureKind.Network, "额度服务返回了重定向，已按安全策略拒绝");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new UsageFetchException(
                    UsageFailureKind.Network,
                    $"额度服务暂不可用（HTTP {(int)response.StatusCode}）");
            }

            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                throw new UsageFetchException(UsageFailureKind.InvalidResponse, "额度响应超过安全大小限制");
            }

            byte[] bytes;
            try
            {
                bytes = await ReadBoundedResponseAsync(response.Content, requestToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (UsageFetchException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HttpRequestException or IOException or OperationCanceledException)
            {
                throw new UsageFetchException(UsageFailureKind.Network, "读取 Codex 额度响应失败", exception);
            }

            if (!TryParseUsageJson(bytes, out var snapshot) || snapshot is null)
            {
                throw new UsageFetchException(UsageFailureKind.InvalidResponse, "无法识别 Codex 额度响应");
            }

            return snapshot;
        }
    }

    public static bool TryParseUsageJson(ReadOnlyMemory<byte> json, out RateLimitSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("rate_limit", out var rateLimit) ||
                rateLimit.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            TryParseWindow(rateLimit, "primary_window", out var primary);
            TryParseWindow(rateLimit, "secondary_window", out var secondary);

            RateLimitWindow? fiveHour = null;
            RateLimitWindow? weekly = null;
            foreach (var window in new[] { primary, secondary }.OfType<RateLimitWindow>())
            {
                if (window.WindowMinutes == 300)
                {
                    fiveHour = window;
                }
                else if (window.WindowMinutes == 10_080)
                {
                    weekly = window;
                }
            }

            if (fiveHour is null && weekly is null)
            {
                return false;
            }

            snapshot = new RateLimitSnapshot(
                fiveHour,
                weekly,
                DateTimeOffset.UtcNow,
                "https://chatgpt.com/backend-api/wham/usage");
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private Credentials LoadCredentials()
    {
        try
        {
            if (!File.Exists(_authPath))
            {
                throw new UsageFetchException(UsageFailureKind.Credentials, "未找到 Codex 登录信息");
            }

            var authBytes = ReadBoundedCredentialsFile();
            try
            {
                using var document = JsonDocument.Parse(authBytes);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("tokens", out var tokens) ||
                    tokens.ValueKind != JsonValueKind.Object ||
                    !tokens.TryGetProperty("access_token", out var accessTokenElement) ||
                    accessTokenElement.ValueKind != JsonValueKind.String)
                {
                    throw new UsageFetchException(UsageFailureKind.Credentials, "Codex 登录信息格式不受支持");
                }

                var accessToken = accessTokenElement.GetString();
                string? accountId = null;
                if (tokens.TryGetProperty("account_id", out var accountElement) &&
                    accountElement.ValueKind is not JsonValueKind.Null)
                {
                    if (accountElement.ValueKind != JsonValueKind.String)
                    {
                        throw new UsageFetchException(UsageFailureKind.Credentials, "Codex 登录信息格式不受支持");
                    }

                    accountId = accountElement.GetString();
                }
                if (string.IsNullOrWhiteSpace(accessToken))
                {
                    throw new UsageFetchException(UsageFailureKind.Credentials, "Codex access token 为空");
                }

                return new Credentials(accessToken, accountId);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(authBytes);
            }
        }
        catch (UsageFetchException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            throw new UsageFetchException(UsageFailureKind.Credentials, "无法读取 Codex 登录信息", exception);
        }
    }

    private byte[] ReadBoundedCredentialsFile()
    {
        using var stream = new FileStream(
            _authPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            CredentialsBufferBytes,
            FileOptions.SequentialScan);
        if (stream.Length > MaximumCredentialsBytes)
        {
            throw new UsageFetchException(UsageFailureKind.Credentials, "Codex 登录信息超过安全大小限制");
        }

        var initialCapacity = stream.Length > 0 ? (int)stream.Length : CredentialsBufferBytes;
        using var output = new MemoryStream(initialCapacity);
        var buffer = new byte[CredentialsBufferBytes];
        try
        {
            while (true)
            {
                var remaining = MaximumCredentialsBytes + 1 - (int)output.Length;
                var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    return output.ToArray();
                }

                output.Write(buffer, 0, read);
                if (output.Length > MaximumCredentialsBytes)
                {
                    throw new UsageFetchException(UsageFailureKind.Credentials, "Codex 登录信息超过安全大小限制");
                }
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            if (output.TryGetBuffer(out var outputBuffer))
            {
                CryptographicOperations.ZeroMemory(outputBuffer.AsSpan(0, (int)output.Length));
            }
        }
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var initialCapacity = content.Headers.ContentLength is > 0 and <= MaximumResponseBytes
            ? (int)content.Headers.ContentLength.Value
            : ResponseBufferBytes;
        using var output = new MemoryStream(initialCapacity);
        var buffer = new byte[ResponseBufferBytes];

        while (true)
        {
            var remaining = MaximumResponseBytes + 1 - (int)output.Length;
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken);
            if (read == 0)
            {
                return output.ToArray();
            }

            output.Write(buffer, 0, read);
            if (output.Length > MaximumResponseBytes)
            {
                throw new UsageFetchException(UsageFailureKind.InvalidResponse, "额度响应超过安全大小限制");
            }
        }
    }

    private static bool TryParseWindow(
        JsonElement parent,
        string propertyName,
        out RateLimitWindow? window)
    {
        window = null;
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("used_percent", out var usedElement) || !usedElement.TryGetDouble(out var usedPercent) ||
            !element.TryGetProperty("limit_window_seconds", out var secondsElement) || !secondsElement.TryGetInt32(out var seconds) ||
            !element.TryGetProperty("reset_at", out var resetElement) || !resetElement.TryGetInt64(out var resetAt))
        {
            return false;
        }

        try
        {
            window = new RateLimitWindow(
                Math.Clamp(usedPercent, 0, 100),
                seconds / 60,
                DateTimeOffset.FromUnixTimeSeconds(resetAt));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record Credentials(string AccessToken, string? AccountId);
}
