using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class WindowLocationMonitorTests
{
    [Fact]
    public void AcceptsTopLevelWindowLocationChanges()
    {
        Assert.True(WindowLocationMonitor.IsTopLevelWindowLocationChange(
            WindowLocationMonitor.LocationChangeEvent,
            new IntPtr(42),
            objectId: 0,
            childId: 0));
    }

    [Theory]
    [InlineData(0x800A, 42, 0, 0)]
    [InlineData(0x800B, 0, 0, 0)]
    [InlineData(0x800B, 42, 1, 0)]
    [InlineData(0x800B, 42, 0, 1)]
    public void RejectsUnrelatedAccessibilityEvents(
        uint eventType,
        long windowHandle,
        int objectId,
        int childId)
    {
        Assert.False(WindowLocationMonitor.IsTopLevelWindowLocationChange(
            eventType,
            new IntPtr(windowHandle),
            objectId,
            childId));
    }
}
