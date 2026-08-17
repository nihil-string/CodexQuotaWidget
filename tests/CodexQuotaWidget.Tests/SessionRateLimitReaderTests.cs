using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class SessionRateLimitReaderTests
{
    [Fact]
    public void ParsesFiveHourAndWeeklyWindowsByDuration()
    {
        const string line = """
            {"timestamp":"2026-07-12T04:05:06Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":42.4,"window_minutes":300,"resets_at":1783832400},"secondary":{"used_percent":17.0,"window_minutes":10080,"resets_at":1784419200}}}}
            """;

        var parsed = SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot);

        Assert.True(parsed);
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.FiveHour);
        Assert.NotNull(snapshot.Weekly);
        Assert.Equal(42.4, snapshot.FiveHour.UsedPercent);
        Assert.Equal(17.0, snapshot.Weekly.UsedPercent);
        Assert.Equal("fixture.jsonl", snapshot.SourceFile);
    }

    [Fact]
    public void ParsesWeeklyWindowWithoutFiveHourWindow()
    {
        const string line = """
            {"timestamp":"2026-07-12T04:05:06Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":51.0,"window_minutes":10080,"resets_at":1784419200},"secondary":null}}}
            """;

        var parsed = SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot);

        Assert.True(parsed);
        Assert.NotNull(snapshot);
        Assert.Null(snapshot.FiveHour);
        Assert.NotNull(snapshot.Weekly);
        Assert.Equal(51, snapshot.Weekly.UsedPercent);
    }

    [Fact]
    public void RejectsNullRateLimitEnvelope()
    {
        const string line = """
            {"timestamp":"2026-07-12T04:05:06Z","type":"event_msg","payload":{"type":"token_count","rate_limits":null}}
            """;

        Assert.False(SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void RejectsMalformedJsonWithoutThrowing()
    {
        const string line = "{\"type\":\"token_count\",\"rate_limits\":";

        Assert.False(SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("[\"token_count\",\"rate_limits\"]")]
    [InlineData("{\"payload\":[\"token_count\",\"rate_limits\"]}")]
    public void RejectsNonObjectLogStructuresWithoutThrowing(string line)
    {
        Assert.False(SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public void RejectsNonTokenCountEventsEvenWhenTextContainsTokenCount()
    {
        const string line = """
            {"type":"token_count","payload":{"type":"other","message":"token_count","rate_limits":{"primary":{"used_percent":42.4,"window_minutes":300,"resets_at":1783832400}}}}
            """;

        Assert.False(SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot));
        Assert.Null(snapshot);
    }

    [Theory]
    [InlineData("{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":42.4,\"window_minutes\":300,\"resets_at\":1783832400}}}}")]
    [InlineData("{\"timestamp\":\"not-a-date\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"rate_limits\":{\"primary\":{\"used_percent\":42.4,\"window_minutes\":300,\"resets_at\":1783832400}}}}")]
    public void RejectsRateLimitEventsWithoutValidTimestamp(string line)
    {
        Assert.False(SessionRateLimitReader.TryParseLine(line, "fixture.jsonl", out var snapshot));
        Assert.Null(snapshot);
    }

    [Fact]
    public async Task ReadLatestAsyncReadsLatestSnapshotFromSessionDirectory()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), $"CodexQuotaWidget.Tests.{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions", "2026", "07", "13");
        Directory.CreateDirectory(sessionsPath);
        try
        {
            var activeReset = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
            await File.WriteAllTextAsync(
                Path.Combine(sessionsPath, "fixture.jsonl"),
                """
                {"timestamp":"2026-07-13T01:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":48.0,"window_minutes":10080,"resets_at":ACTIVE_RESET}}}}
                {"timestamp":"2026-07-13T02:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":55.0,"window_minutes":10080,"resets_at":ACTIVE_RESET}}}}
                """.Replace("ACTIVE_RESET", activeReset.ToString(), StringComparison.Ordinal));
            var reader = new SessionRateLimitReader(codexHome);

            var snapshot = await reader.ReadLatestAsync();

            Assert.NotNull(snapshot);
            Assert.NotNull(snapshot.Weekly);
            Assert.Equal(55, snapshot.Weekly.UsedPercent);
            Assert.Equal(DateTimeOffset.Parse("2026-07-13T02:00:00Z"), snapshot.ObservedAt);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public async Task ReadLatestAsyncDropsWindowsWhoseQuotaPeriodAlreadyReset()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), $"CodexQuotaWidget.Tests.{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions");
        Directory.CreateDirectory(sessionsPath);
        try
        {
            var expiredReset = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
            var activeReset = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
            var line = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = "2026-07-14T00:00:00Z",
                type = "event_msg",
                payload = new
                {
                    type = "token_count",
                    rate_limits = new
                    {
                        primary = new { used_percent = 80.0, window_minutes = 300, resets_at = expiredReset },
                        secondary = new { used_percent = 55.0, window_minutes = 10_080, resets_at = activeReset }
                    }
                }
            });
            await File.WriteAllTextAsync(Path.Combine(sessionsPath, "fixture.jsonl"), line);

            var snapshot = await new SessionRateLimitReader(codexHome).ReadLatestAsync();

            Assert.NotNull(snapshot);
            Assert.Null(snapshot.FiveHour);
            Assert.NotNull(snapshot.Weekly);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public async Task ReadLatestAsyncReturnsNullWhenAllQuotaPeriodsAlreadyReset()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), $"CodexQuotaWidget.Tests.{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions");
        Directory.CreateDirectory(sessionsPath);
        try
        {
            var expiredReset = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
            var line = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = "2026-07-14T00:00:00Z",
                type = "event_msg",
                payload = new
                {
                    type = "token_count",
                    rate_limits = new
                    {
                        primary = new { used_percent = 80.0, window_minutes = 300, resets_at = expiredReset }
                    }
                }
            });
            await File.WriteAllTextAsync(Path.Combine(sessionsPath, "fixture.jsonl"), line);

            var snapshot = await new SessionRateLimitReader(codexHome).ReadLatestAsync();

            Assert.Null(snapshot);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public async Task ReadLatestAsyncFindsSnapshotAmongNewestCandidateFiles()
    {
        var codexHome = Path.Combine(Path.GetTempPath(), $"CodexQuotaWidget.Tests.{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions");
        Directory.CreateDirectory(sessionsPath);
        try
        {
            var baseWriteTime = DateTime.UtcNow.AddHours(-1);
            for (var index = 0; index < 30; index++)
            {
                var path = Path.Combine(sessionsPath, $"older-{index:D2}.jsonl");
                await File.WriteAllTextAsync(path, "{\"type\":\"unrelated\"}");
                File.SetLastWriteTimeUtc(path, baseWriteTime.AddMinutes(index));
            }

            var activeReset = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
            var newestLine = System.Text.Json.JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow,
                type = "event_msg",
                payload = new
                {
                    type = "token_count",
                    rate_limits = new
                    {
                        primary = new { used_percent = 65.0, window_minutes = 10_080, resets_at = activeReset }
                    }
                }
            });
            var newestPath = Path.Combine(sessionsPath, "newest.jsonl");
            await File.WriteAllTextAsync(newestPath, newestLine);
            File.SetLastWriteTimeUtc(newestPath, DateTime.UtcNow);

            var snapshot = await new SessionRateLimitReader(codexHome).ReadLatestAsync();

            Assert.NotNull(snapshot);
            Assert.NotNull(snapshot.Weekly);
            Assert.Equal(65, snapshot.Weekly.UsedPercent);
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }
}
