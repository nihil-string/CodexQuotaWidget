using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class ComposerProbeControllerTests
{
    private static readonly DateTimeOffset StartTime = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly CodexComposerTarget Target = new(
        new IntPtr(42),
        new ScreenRectangle(100, 200, 800, 600),
        new ScreenRectangle(420, 750, 148, 28),
        IsLightBackground: true,
        BackgroundRgb: 0xFAFAFA);

    [Fact]
    public void ReusesSuccessfulTargetUntilTheNextProbeIsDue()
    {
        var starts = 0;
        using var controller = CreateController((_, _, _) =>
        {
            starts++;
            return Task.FromResult<CodexComposerTarget?>(Target);
        });

        var initial = controller.Poll(148, 28, StartTime);
        var beforeDue = controller.Poll(148, 28, StartTime.AddSeconds(4.9));
        var atDue = controller.Poll(148, 28, StartTime.AddSeconds(5));

        Assert.Equal(Target, initial.Target);
        Assert.Equal(Target, beforeDue.Target);
        Assert.Equal(Target, atDue.Target);
        Assert.Equal(1, initial.Revision);
        Assert.Equal(1, beforeDue.Revision);
        Assert.Equal(2, atDue.Revision);
        Assert.Equal(2, starts);
    }

    [Fact]
    public void RequestedProbeRunsEarlyWithoutDiscardingTheCachedTarget()
    {
        var starts = 0;
        using var controller = CreateController((_, _, _) =>
        {
            starts++;
            return Task.FromResult<CodexComposerTarget?>(Target);
        });

        var initial = controller.Poll(148, 28, StartTime);
        controller.RequestProbe(StartTime.AddSeconds(1));
        var beforeRequested = controller.Poll(148, 28, StartTime.AddMilliseconds(999));
        var requested = controller.Poll(148, 28, StartTime.AddSeconds(1));

        Assert.Equal(Target, initial.Target);
        Assert.Equal(Target, beforeRequested.Target);
        Assert.Equal(Target, requested.Target);
        Assert.Equal(1, beforeRequested.Revision);
        Assert.Equal(2, requested.Revision);
        Assert.Equal(2, starts);
    }

    [Fact]
    public void TimesOutWithoutStartingOverlappingAutomationCalls()
    {
        var firstProbe = new TaskCompletionSource<CodexComposerTarget?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        using var controller = CreateController((_, _, _) =>
        {
            starts++;
            return starts == 1
                ? firstProbe.Task
                : Task.FromResult<CodexComposerTarget?>(Target);
        });

        _ = controller.Poll(148, 28, StartTime);
        var timedOut = controller.Poll(148, 28, StartTime.AddSeconds(1));
        _ = controller.Poll(148, 28, StartTime.AddSeconds(10));

        Assert.True(timedOut.TimedOut);
        Assert.Null(timedOut.Target);
        Assert.Equal(1, starts);

        firstProbe.SetResult(Target);
        var discarded = controller.Poll(148, 28, StartTime.AddSeconds(11));
        var retried = controller.Poll(148, 28, StartTime.AddSeconds(12));

        Assert.Null(discarded.Target);
        Assert.Equal(Target, retried.Target);
        Assert.Equal(2, starts);
    }

    [Fact]
    public void ConvertsFaultedProbeToFailureBackoff()
    {
        var starts = 0;
        using var controller = CreateController((_, _, _) =>
        {
            starts++;
            return Task.FromException<CodexComposerTarget?>(new InvalidOperationException("provider failed"));
        });

        var failed = controller.Poll(148, 28, StartTime);
        _ = controller.Poll(148, 28, StartTime.AddMilliseconds(999));
        _ = controller.Poll(148, 28, StartTime.AddSeconds(1));

        Assert.Null(failed.Target);
        Assert.False(failed.TimedOut);
        Assert.Equal(2, starts);
    }

    [Fact]
    public void KeepsLastSuccessfulTargetDuringBoundedTransientFailures()
    {
        var starts = 0;
        using var controller = CreateController((_, _, _) =>
        {
            starts++;
            return Task.FromResult<CodexComposerTarget?>(starts == 1 ? Target : null);
        });

        var initial = controller.Poll(148, 28, StartTime);
        var firstFailure = controller.Poll(148, 28, StartTime.AddSeconds(5));
        var secondFailure = controller.Poll(148, 28, StartTime.AddSeconds(6));
        var expired = controller.Poll(148, 28, StartTime.AddSeconds(15.1));

        Assert.Equal(Target, initial.Target);
        Assert.Equal(Target, firstFailure.Target);
        Assert.Equal(Target, secondFailure.Target);
        Assert.Null(expired.Target);
        Assert.Equal(4, starts);
    }

    [Fact]
    public void KeepsCachedTargetAfterTimeoutWithoutOverlappingTheProbe()
    {
        var pendingProbe = new TaskCompletionSource<CodexComposerTarget?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        using var controller = CreateController((_, _, _) =>
        {
            starts++;
            return starts == 1
                ? Task.FromResult<CodexComposerTarget?>(Target)
                : pendingProbe.Task;
        });

        _ = controller.Poll(148, 28, StartTime);
        _ = controller.Poll(148, 28, StartTime.AddSeconds(5));
        var timedOut = controller.Poll(148, 28, StartTime.AddSeconds(6));
        var expired = controller.Poll(148, 28, StartTime.AddSeconds(15.1));

        Assert.True(timedOut.TimedOut);
        Assert.Equal(Target, timedOut.Target);
        Assert.Null(expired.Target);
        Assert.Equal(2, starts);
    }

    [Fact]
    public void CancelsTimedOutProbeBeforeAllowingARetry()
    {
        var starts = 0;
        var observedToken = CancellationToken.None;
        using var controller = CreateController((_, _, cancellationToken) =>
        {
            starts++;
            observedToken = cancellationToken;
            var completion = new TaskCompletionSource<CodexComposerTarget?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(
                static state =>
                    ((TaskCompletionSource<CodexComposerTarget?>)state!).TrySetCanceled(),
                completion);
            return completion.Task;
        });

        _ = controller.Poll(148, 28, StartTime);
        var timedOut = controller.Poll(148, 28, StartTime.AddSeconds(1));

        Assert.True(timedOut.TimedOut);
        Assert.True(observedToken.IsCancellationRequested);
        Assert.Equal(1, starts);

        _ = controller.Poll(148, 28, StartTime.AddSeconds(1.1));
        _ = controller.Poll(148, 28, StartTime.AddSeconds(2.1));

        Assert.Equal(2, starts);
    }

    private static ComposerProbeController CreateController(
        Func<double, double, CancellationToken, Task<CodexComposerTarget?>> startProbe) =>
        new(
            startProbe,
            successInterval: TimeSpan.FromSeconds(5),
            failureInterval: TimeSpan.FromSeconds(1),
            timeout: TimeSpan.FromSeconds(1),
            cachedTargetGrace: TimeSpan.FromSeconds(15));
}
