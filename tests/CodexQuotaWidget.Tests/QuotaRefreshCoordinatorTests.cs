using CodexQuotaWidget.Models;
using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class QuotaRefreshCoordinatorTests
{
    [Fact]
    public async Task OnlineSuccessDoesNotReadLocalSnapshot()
    {
        var online = CreateSnapshot(20);
        var localReads = 0;
        using var coordinator = new QuotaRefreshCoordinator(
            _ => Task.FromResult(online),
            _ =>
            {
                Interlocked.Increment(ref localReads);
                return Task.FromResult<RateLimitSnapshot?>(null);
            });

        var attempt = await coordinator.RefreshAsync();

        Assert.NotNull(attempt);
        Assert.Same(online, attempt.Snapshot);
        Assert.Null(attempt.OnlineFailure);
        Assert.True(coordinator.IsLatest(attempt));
        Assert.Equal(0, localReads);
    }

    [Fact]
    public async Task UsageFailureFallsBackToLocalAndPreservesFailureKind()
    {
        var local = CreateSnapshot(40);
        var failure = new UsageFetchException(UsageFailureKind.Authentication, "expired");
        using var coordinator = new QuotaRefreshCoordinator(
            _ => Task.FromException<RateLimitSnapshot>(failure),
            _ => Task.FromResult<RateLimitSnapshot?>(local));

        var attempt = await coordinator.RefreshAsync();

        Assert.NotNull(attempt);
        Assert.Same(local, attempt.Snapshot);
        var onlineFailure = Assert.IsType<UsageFetchException>(attempt.OnlineFailure);
        Assert.Same(failure, onlineFailure);
        Assert.Equal(UsageFailureKind.Authentication, onlineFailure.Kind);
    }

    [Fact]
    public async Task UsageFailureWithoutLocalSnapshotReturnsUnavailableAttempt()
    {
        var failure = new UsageFetchException(UsageFailureKind.Network, "offline");
        using var coordinator = new QuotaRefreshCoordinator(
            _ => Task.FromException<RateLimitSnapshot>(failure),
            _ => Task.FromResult<RateLimitSnapshot?>(null));

        var attempt = await coordinator.RefreshAsync();

        Assert.NotNull(attempt);
        Assert.Null(attempt.Snapshot);
        Assert.Same(failure, attempt.OnlineFailure);
        Assert.True(coordinator.IsLatest(attempt));
    }

    [Fact]
    public async Task NewerRefreshInvalidatesCompletedAttemptBeforeUiCommit()
    {
        var first = CreateSnapshot(10);
        var second = CreateSnapshot(20);
        var callCount = 0;
        using var coordinator = new QuotaRefreshCoordinator(
            _ => Task.FromResult(Interlocked.Increment(ref callCount) == 1 ? first : second),
            _ => Task.FromResult<RateLimitSnapshot?>(null));

        var firstAttempt = await coordinator.RefreshAsync();
        var secondAttempt = await coordinator.RefreshAsync();

        Assert.NotNull(firstAttempt);
        Assert.NotNull(secondAttempt);
        Assert.False(coordinator.IsLatest(firstAttempt));
        Assert.True(coordinator.IsLatest(secondAttempt));
        Assert.Same(second, secondAttempt.Snapshot);
    }

    [Fact]
    public async Task SupersededRefreshCannotReturnResultWhenDependencyIgnoresCancellation()
    {
        var firstStarted = NewSignal();
        var releaseFirst = NewSignal();
        var oldSnapshot = CreateSnapshot(10);
        var newSnapshot = CreateSnapshot(20);
        var callCount = 0;
        using var coordinator = new QuotaRefreshCoordinator(
            async _ =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    firstStarted.TrySetResult(true);
                    await releaseFirst.Task;
                    return oldSnapshot;
                }

                return newSnapshot;
            },
            _ => Task.FromResult<RateLimitSnapshot?>(null));

        var firstTask = coordinator.RefreshAsync();
        await firstStarted.Task;
        var secondAttempt = await coordinator.RefreshAsync();
        releaseFirst.TrySetResult(true);

        Assert.Null(await firstTask);
        Assert.NotNull(secondAttempt);
        Assert.Same(newSnapshot, secondAttempt.Snapshot);
        Assert.True(coordinator.IsLatest(secondAttempt));
    }

    [Fact]
    public async Task SupersededCancellationDoesNotTriggerLocalFallback()
    {
        var firstStarted = NewSignal();
        var localReads = 0;
        var callCount = 0;
        using var coordinator = new QuotaRefreshCoordinator(
            async cancellationToken =>
            {
                if (Interlocked.Increment(ref callCount) == 1)
                {
                    firstStarted.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return CreateSnapshot(20);
            },
            _ =>
            {
                Interlocked.Increment(ref localReads);
                return Task.FromResult<RateLimitSnapshot?>(null);
            });

        var firstTask = coordinator.RefreshAsync();
        await firstStarted.Task;
        var secondAttempt = await coordinator.RefreshAsync();

        Assert.Null(await firstTask);
        Assert.NotNull(secondAttempt);
        Assert.Equal(0, localReads);
    }

    [Fact]
    public async Task UnexpectedOnlineFailurePropagatesWithoutFallback()
    {
        var localReads = 0;
        using var coordinator = new QuotaRefreshCoordinator(
            _ => Task.FromException<RateLimitSnapshot>(new InvalidOperationException("boom")),
            _ =>
            {
                Interlocked.Increment(ref localReads);
                return Task.FromResult<RateLimitSnapshot?>(null);
            });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RefreshAsync());

        Assert.Equal("boom", exception.Message);
        Assert.Equal(0, localReads);
    }

    [Fact]
    public async Task CancelInvalidatesActiveResultWhenDependencyIgnoresCancellation()
    {
        var started = NewSignal();
        var release = NewSignal();
        using var coordinator = new QuotaRefreshCoordinator(
            async _ =>
            {
                started.TrySetResult(true);
                await release.Task;
                return CreateSnapshot(10);
            },
            _ => Task.FromResult<RateLimitSnapshot?>(null));

        var activeTask = coordinator.RefreshAsync();
        await started.Task;
        coordinator.Cancel();
        release.TrySetResult(true);

        Assert.Null(await activeTask);
    }

    [Fact]
    public async Task DisposeCancelsActiveRefreshAndRejectsFutureRefreshes()
    {
        var started = NewSignal();
        var coordinator = new QuotaRefreshCoordinator(
            async cancellationToken =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return CreateSnapshot(10);
            },
            _ => Task.FromResult<RateLimitSnapshot?>(null));

        var activeTask = coordinator.RefreshAsync();
        await started.Task;
        coordinator.Dispose();

        Assert.Null(await activeTask);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => coordinator.RefreshAsync());
    }

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static RateLimitSnapshot CreateSnapshot(double usedPercent) =>
        new(
            FiveHour: null,
            Weekly: new RateLimitWindow(
                usedPercent,
                WindowMinutes: 10_080,
                ResetsAt: DateTimeOffset.Parse("2030-07-20T00:00:00Z")),
            ObservedAt: DateTimeOffset.Parse("2030-07-13T00:00:00Z"),
            SourceFile: "test");
}
