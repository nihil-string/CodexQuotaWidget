using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.IO;
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

    private readonly HttpClient _httpClient;
    private readonly string _authPath;

    public CodexUsageClient(string? codexHome = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaWidget/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var root = codexHome
            ?? Environment.GetEnvironmentVariable("CODEX_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        _authPath = Path.Combine(root, "auth.json");
    }

    public async Task<RateLimitSnapshot> FetchAsync(CancellationToken cancellationToken = default)
    {
        var credentials = LoadCredentials();
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
                cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
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

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (bytes.Length > MaximumResponseBytes)
            {
                throw new UsageFetchException(UsageFailureKind.InvalidResponse, "额度响应超过安全大小限制");
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
            if (!root.TryGetProperty("rate_limit", out var rateLimit) || rateLimit.ValueKind != JsonValueKind.Object ||
                !TryParseWindow(rateLimit, "primary_window", out var primary) ||
                !TryParseWindow(rateLimit, "secondary_window", out var secondary) ||
                primary is null || secondary is null)
            {
                return false;
            }

            RateLimitWindow? fiveHour = null;
            RateLimitWindow? weekly = null;
            foreach (var window in new[] { primary, secondary })
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

            if (fiveHour is null || weekly is null)
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

            using var document = JsonDocument.Parse(File.ReadAllBytes(_authPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object ||
                !tokens.TryGetProperty("access_token", out var accessTokenElement))
            {
                throw new UsageFetchException(UsageFailureKind.Credentials, "Codex 登录信息格式不受支持");
            }

            var accessToken = accessTokenElement.GetString();
            var accountId = tokens.TryGetProperty("account_id", out var accountElement)
                ? accountElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new UsageFetchException(UsageFailureKind.Credentials, "Codex access token 为空");
            }

            return new Credentials(accessToken, accountId);
        }
        catch (UsageFetchException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new UsageFetchException(UsageFailureKind.Credentials, "无法读取 Codex 登录信息", exception);
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
