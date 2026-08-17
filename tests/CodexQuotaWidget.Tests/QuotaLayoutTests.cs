using CodexQuotaWidget.Models;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class QuotaLayoutTests
{
    [Theory]
    [InlineData(true, false, true, false, false, 72)]
    [InlineData(false, true, false, true, false, 72)]
    [InlineData(true, true, true, true, true, 148)]
    public void CreateAvailableWindowsReturnsExpectedLayout(
        bool hasFiveHour,
        bool hasWeekly,
        bool showFiveHour,
        bool showWeekly,
        bool showSeparator,
        double width)
    {
        var layout = QuotaLayout.Create(hasFiveHour, hasWeekly);

        Assert.Equal(showFiveHour, layout.ShowFiveHour);
        Assert.Equal(showWeekly, layout.ShowWeekly);
        Assert.Equal(showSeparator, layout.ShowSeparator);
        Assert.Equal(width, layout.WindowWidth);
    }

    [Fact]
    public void CreateWithoutAvailableWindowsFailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => QuotaLayout.Create(false, false));
    }

    [Fact]
    public void CalculateLeftPreservingRightEdgeRoundTripsBetweenWidths()
    {
        var compactLeft = QuotaLayout.CalculateLeftPreservingRightEdge(100, 148, 72, 0, 1920);
        var expandedLeft = QuotaLayout.CalculateLeftPreservingRightEdge(compactLeft, 72, 148, 0, 1920);

        Assert.Equal(176, compactLeft);
        Assert.Equal(100, expandedLeft);
    }

    [Theory]
    [InlineData(-100, 72, 148, 0, 1920, 0)]
    [InlineData(1900, 72, 148, 0, 1920, 1772)]
    [InlineData(-1800, 148, 72, -1920, 3840, -1724)]
    public void CalculateLeftPreservingRightEdgeClampsToVirtualScreen(
        double currentLeft,
        double currentWidth,
        double targetWidth,
        double virtualLeft,
        double virtualWidth,
        double expectedLeft)
    {
        var left = QuotaLayout.CalculateLeftPreservingRightEdge(
            currentLeft,
            currentWidth,
            targetWidth,
            virtualLeft,
            virtualWidth);

        Assert.Equal(expectedLeft, left);
    }
}
