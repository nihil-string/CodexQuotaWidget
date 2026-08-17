using CodexQuotaWidget.Models;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class ComposerPlacementTests
{
    [Fact]
    public void CentersQuotaBetweenPermissionsAndModelButtons()
    {
        var permissions = new ScreenRectangle(1240, 1155, 86, 28);
        var model = new ScreenRectangle(1756, 1155, 107, 28);

        var success = ComposerPlacement.TryCreate(
            permissions,
            model,
            desiredWidth: 148,
            desiredHeight: 28,
            out var placement);

        Assert.True(success);
        Assert.Equal(1467, placement.Left);
        Assert.Equal(1157, placement.Top);
        Assert.Equal(148, placement.Width);
        Assert.Equal(28, placement.Height);
    }

    [Fact]
    public void ShrinksToTheAvailableGap()
    {
        var permissions = new ScreenRectangle(100, 200, 80, 28);
        var model = new ScreenRectangle(270, 200, 100, 28);

        var success = ComposerPlacement.TryCreate(
            permissions,
            model,
            desiredWidth: 148,
            desiredHeight: 28,
            out var placement);

        Assert.True(success);
        Assert.Equal(188, placement.Left);
        Assert.Equal(74, placement.Width);
    }

    [Theory]
    [InlineData(245, 200)]
    [InlineData(300, 230)]
    public void RejectsInsufficientOrMisalignedAnchors(double modelLeft, double modelTop)
    {
        var success = ComposerPlacement.TryCreate(
            new ScreenRectangle(100, 200, 80, 28),
            new ScreenRectangle(modelLeft, modelTop, 100, 28),
            desiredWidth: 148,
            desiredHeight: 28,
            out _);

        Assert.False(success);
    }
}
