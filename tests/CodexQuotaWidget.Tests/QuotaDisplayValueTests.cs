using CodexQuotaWidget.Models;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class QuotaDisplayValueTests
{
    [Fact]
    public void MissingQuotaUsesPlaceholderInsteadOfFullPercentage()
    {
        var display = QuotaDisplayValue.From(window: null);

        Assert.Equal("—", display.Text);
        Assert.Equal(0, display.RingValue);
    }

    [Fact]
    public void ValidUnusedQuotaStillShowsFullPercentage()
    {
        var display = QuotaDisplayValue.From(new RateLimitWindow(
            UsedPercent: 0,
            WindowMinutes: 300,
            ResetsAt: DateTimeOffset.Parse("2030-07-20T00:00:00Z")));

        Assert.Equal("100%", display.Text);
        Assert.Equal(100, display.RingValue);
    }
}
