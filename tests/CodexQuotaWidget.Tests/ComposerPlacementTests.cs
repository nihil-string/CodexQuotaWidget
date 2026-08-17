using System.Text.Json;
using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class ComposerPlacementTests
{
    [Theory]
    [InlineData(42, 42u, true)]
    [InlineData(42, 43u, false)]
    [InlineData(0, 42u, false)]
    [InlineData(42, 0u, false)]
    public void MatchesAnyForegroundWindowOwnedByTheCodexProcess(
        int processId,
        uint foregroundProcessId,
        bool expected)
    {
        Assert.Equal(
            expected,
            CodexComposerLocator.IsForegroundProcess(processId, foregroundProcessId));
    }

    [Fact]
    public void IsolatedProbePayloadRoundTripsTheValidatedTarget()
    {
        var target = new CodexComposerTarget(
            new IntPtr(12345),
            new ScreenRectangle(100, 200, 800, 600),
            new ScreenRectangle(420, 750, 148, 28),
            IsLightBackground: true,
            BackgroundRgb: 0xFAFAFA);

        var json = JsonSerializer.Serialize(ComposerProbePayload.FromTarget(target));
        var restored = JsonSerializer.Deserialize<ComposerProbePayload>(json)?.ToTarget();

        Assert.Equal(target, restored);
    }

    [Fact]
    public void DoesNotRepairZOrderWhenOverlayIsAlreadyAboveCodex()
    {
        var windowsAbove = new Dictionary<IntPtr, IntPtr>
        {
            [new IntPtr(10)] = new IntPtr(20),
            [new IntPtr(20)] = new IntPtr(30),
            [new IntPtr(30)] = IntPtr.Zero
        };

        var needsRepair = CodexComposerLocator.TryGetOverlayZOrderRepair(
            new IntPtr(10),
            new IntPtr(30),
            handle => windowsAbove[handle],
            out var insertAfter);

        Assert.False(needsRepair);
        Assert.Equal(new IntPtr(20), insertAfter);
    }

    [Fact]
    public void RepairsZOrderImmediatelyAboveCodexWhenOverlayIsBehind()
    {
        var windowsAbove = new Dictionary<IntPtr, IntPtr>
        {
            [new IntPtr(10)] = new IntPtr(20),
            [new IntPtr(20)] = IntPtr.Zero
        };

        var needsRepair = CodexComposerLocator.TryGetOverlayZOrderRepair(
            new IntPtr(10),
            new IntPtr(30),
            handle => windowsAbove[handle],
            out var insertAfter);

        Assert.True(needsRepair);
        Assert.Equal(new IntPtr(20), insertAfter);
    }

    [Fact]
    public void RejectsZOrderRepairWithoutBothWindowHandles()
    {
        Assert.False(CodexComposerLocator.TryGetOverlayZOrderRepair(
            IntPtr.Zero,
            new IntPtr(30),
            _ => IntPtr.Zero,
            out _));
        Assert.False(CodexComposerLocator.TryGetOverlayZOrderRepair(
            new IntPtr(10),
            IntPtr.Zero,
            _ => IntPtr.Zero,
            out _));
    }

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

    [Fact]
    public void ProjectsCachedPlacementWhenTheCodexWindowOnlyMoves()
    {
        var success = ComposerPlacement.TryProject(
            new ScreenRectangle(100, 200, 800, 600),
            new ScreenRectangle(420, 750, 148, 28),
            new ScreenRectangle(140, 260, 800, 600),
            out var placement);

        Assert.True(success);
        Assert.Equal(new ScreenRectangle(460, 810, 148, 28), placement);
    }

    [Fact]
    public void RejectsCachedPlacementAfterTheCodexWindowResizes()
    {
        var success = ComposerPlacement.TryProject(
            new ScreenRectangle(100, 200, 800, 600),
            new ScreenRectangle(420, 750, 148, 28),
            new ScreenRectangle(100, 200, 900, 600),
            out _);

        Assert.False(success);
    }
}
