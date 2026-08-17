using System.Text.Json;
using System.IO;
using System.Security;
using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

public sealed class SessionRateLimitReader
{
    private const int FiveHourMinutes = 300;
    private const int WeeklyMinutes = 10_080;
    private const int MaximumCandidateFiles = 24;
    private const long MaximumTailBytes = 2 * 1024 * 1024;

    private readonly string _sessionsPath;

    public SessionRateLimitReader(string? codexHome = null)
    {
        var configuredHome = string.IsNullOrWhiteSpace(codexHome)
            ? Environment.GetEnvironmentVariable("CODEX_HOME")
            : codexHome;
        var root = string.IsNullOrWhiteSpace(configuredHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex")
            : configuredHome;
        _sessionsPath = Path.Combine(root, "sessions");
    }

    public string SessionsPath => _sessionsPath;

    public Task<RateLimitSnapshot?> ReadLatestAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ReadLatestCoreAsync(cancellationToken), cancellationToken);

    private async Task<RateLimitSnapshot?> ReadLatestCoreAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_sessionsPath))
        {
            return null;
        }

        FileInfo[] candidates;
        try
        {
            candidates = SelectNewestCandidateFiles(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }

        RateLimitSnapshot? latest = null;
        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await ReadLatestFromFileAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && (latest is null || snapshot.ObservedAt > latest.ObservedAt))
            {
                latest = snapshot;
            }
        }

        return RemoveExpiredWindows(latest, DateTimeOffset.UtcNow);
    }

    private FileInfo[] SelectNewestCandidateFiles(CancellationToken cancellationToken)
    {
        var candidates = new PriorityQueue<FileInfo, long>(MaximumCandidateFiles);
        foreach (var file in new DirectoryInfo(_sessionsPath)
                     .EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lastWriteTicks = file.LastWriteTimeUtc.Ticks;
            if (candidates.Count < MaximumCandidateFiles)
            {
                candidates.Enqueue(file, lastWriteTicks);
                continue;
            }

            if (candidates.TryPeek(out _, out var oldestTicks) && lastWriteTicks > oldestTicks)
            {
                candidates.Dequeue();
                candidates.Enqueue(file, lastWriteTicks);
            }
        }

        return candidates.UnorderedItems.Select(item => item.Element).ToArray();
    }

    public static bool TryParseLine(string line, string sourceFile, out RateLimitSnapshot? snapshot)
    {
        snapshot = null;
        if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal) ||
            !line.Contains("\"token_count\"", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("type", out var payloadType) ||
                payloadType.ValueKind != JsonValueKind.String ||
                !string.Equals(payloadType.GetString(), "token_count", StringComparison.Ordinal) ||
                !payload.TryGetProperty("rate_limits", out var rateLimits) ||
                rateLimits.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!root.TryGetProperty("timestamp", out var timestamp) ||
                timestamp.ValueKind != JsonValueKind.String ||
                !timestamp.TryGetDateTimeOffset(out var observedAt))
            {
                return false;
            }

            var windows = new List<RateLimitWindow>(2);
            AddWindow(rateLimits, "primary", windows);
            AddWindow(rateLimits, "secondary", windows);

            var fiveHour = windows.FirstOrDefault(window => window.WindowMinutes == FiveHourMinutes);
            var weekly = windows.FirstOrDefault(window => window.WindowMinutes == WeeklyMinutes);
            if (fiveHour is null && weekly is null)
            {
                return false;
            }

            snapshot = new RateLimitSnapshot(fiveHour, weekly, observedAt, sourceFile);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AddWindow(JsonElement parent, string propertyName, ICollection<RateLimitWindow> windows)
    {
        if (!parent.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("used_percent", out var usedElement) || !usedElement.TryGetDouble(out var usedPercent) ||
            !element.TryGetProperty("window_minutes", out var minutesElement) || !minutesElement.TryGetInt32(out var windowMinutes) ||
            !element.TryGetProperty("resets_at", out var resetElement) || !resetElement.TryGetInt64(out var resetSeconds))
        {
            return;
        }

        try
        {
            windows.Add(new RateLimitWindow(
                Math.Clamp(usedPercent, 0, 100),
                windowMinutes,
                DateTimeOffset.FromUnixTimeSeconds(resetSeconds)));
        }
        catch (ArgumentOutOfRangeException)
        {
            // Malformed external log data is ignored at the file boundary.
        }
    }

    private static RateLimitSnapshot? RemoveExpiredWindows(
        RateLimitSnapshot? snapshot,
        DateTimeOffset now)
    {
        if (snapshot is null)
        {
            return null;
        }

        var fiveHour = snapshot.FiveHour is { ResetsAt: var fiveHourReset } && fiveHourReset > now
            ? snapshot.FiveHour
            : null;
        var weekly = snapshot.Weekly is { ResetsAt: var weeklyReset } && weeklyReset > now
            ? snapshot.Weekly
            : null;

        return fiveHour is null && weekly is null
            ? null
            : snapshot with { FiveHour = fiveHour, Weekly = weekly };
    }

    private static async Task<RateLimitSnapshot?> ReadLatestFromFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                useAsync: true);

            var start = Math.Max(0, stream.Length - MaximumTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);

            if (start > 0)
            {
                await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }

            RateLimitSnapshot? latest = null;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (TryParseLine(line, path, out var parsed) &&
                    parsed is not null &&
                    (latest is null || parsed.ObservedAt >= latest.ObservedAt))
                {
                    latest = parsed;
                }
            }

            return latest;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return null;
        }
    }
}
