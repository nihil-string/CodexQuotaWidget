using CodexQuotaWidget.Models;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class QuotaResetDisplayTests
{
    [Fact]
    public void CountdownIncludesDaysHoursAndMinutes()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var resetAt = now.AddDays(4).AddHours(16).AddMinutes(45);

        var display = QuotaResetDisplay.FormatCountdown(resetAt, now);

        Assert.Equal("还剩4天16小时\n45分钟重置", display);
    }

    [Fact]
    public void CountdownRoundsPartialMinuteUp()
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var resetAt = now.AddDays(1).AddSeconds(1);

        var display = QuotaResetDisplay.FormatCountdown(resetAt, now);

        Assert.Equal("还剩1天0小时\n1分钟重置", display);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CountdownWaitsForRefreshAfterReset(int offsetSeconds)
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var resetAt = now.AddSeconds(offsetSeconds);

        var display = QuotaResetDisplay.FormatCountdown(resetAt, now);

        Assert.Equal("等待刷新", display);
    }
}
