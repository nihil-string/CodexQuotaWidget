using CodexQuotaWidget.Models;

namespace CodexQuotaWidget.Services;

internal sealed record QuotaRefreshAttempt(
    long Revision,
    RateLimitSnapshot? Snapshot,
    UsageFetchException? OnlineFailure);

internal sealed class QuotaRefreshCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task<RateLimitSnapshot>> _fetchOnline;
    private readonly Func<CancellationToken, Task<RateLimitSnapshot?>> _readLocal;
    private CancellationTokenSource? _activeCancellation;
    private long _revision;
    private bool _disposed;

    public QuotaRefreshCoordinator(
        Func<CancellationToken, Task<RateLimitSnapshot>> fetchOnline,
        Func<CancellationToken, Task<RateLimitSnapshot?>> readLocal)
    {
        ArgumentNullException.ThrowIfNull(fetchOnline);
        ArgumentNullException.ThrowIfNull(readLocal);

        _fetchOnline = fetchOnline;
        _readLocal = readLocal;
    }

    public async Task<QuotaRefreshAttempt?> RefreshAsync()
    {
        var (revision, cancellation, previousCancellation) = BeginRefresh();
        CancelSafely(previousCancellation);
        var cancellationToken = cancellation.Token;

        try
        {
            QuotaRefreshAttempt attempt;
            try
            {
                var snapshot = await _fetchOnline(cancellationToken).ConfigureAwait(false);
                attempt = new QuotaRefreshAttempt(revision, snapshot, OnlineFailure: null);
            }
            catch (UsageFetchException exception)
            {
                var snapshot = await _readLocal(cancellationToken).ConfigureAwait(false);
                attempt = new QuotaRefreshAttempt(revision, snapshot, exception);
            }

            return IsCurrent(revision, cancellation) ? attempt : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            EndRefresh(cancellation);
        }
    }

    public bool IsLatest(QuotaRefreshAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        lock (_gate)
        {
            return !_disposed && attempt.Revision == _revision;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _revision++;
            cancellation = _activeCancellation;
            _activeCancellation = null;
        }

        CancelSafely(cancellation);
    }

    public void Dispose()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _revision++;
            cancellation = _activeCancellation;
            _activeCancellation = null;
        }

        CancelSafely(cancellation);
    }

    private (long Revision, CancellationTokenSource Cancellation, CancellationTokenSource? PreviousCancellation)
        BeginRefresh()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var previousCancellation = _activeCancellation;
            var cancellation = new CancellationTokenSource();
            var revision = ++_revision;
            _activeCancellation = cancellation;
            return (revision, cancellation, previousCancellation);
        }
    }

    private bool IsCurrent(long revision, CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            return !_disposed &&
                   revision == _revision &&
                   ReferenceEquals(_activeCancellation, cancellation) &&
                   !cancellation.IsCancellationRequested;
        }
    }

    private void EndRefresh(CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeCancellation, cancellation))
            {
                _activeCancellation = null;
            }
        }

        cancellation.Dispose();
    }

    private static void CancelSafely(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The refresh completed between being replaced and receiving cancellation.
        }
    }
}
