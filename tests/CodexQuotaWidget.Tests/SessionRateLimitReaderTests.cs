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
        Assert.Equal(42.4, snapshot.FiveHour.UsedPercent);
        Assert.Equal(17.0, snapshot.Weekly.UsedPercent);
        Assert.Equal("fixture.jsonl", snapshot.SourceFile);
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
}
