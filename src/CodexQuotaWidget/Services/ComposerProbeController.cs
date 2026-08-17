namespace CodexQuotaWidget.Services;

internal readonly record struct ComposerProbeObservation(
    CodexComposerTarget? Target,
    bool TimedOut);

internal sealed class ComposerProbeController : IDisposable
{
    private readonly Func<double, double, CancellationToken, Task<CodexComposerTarget?>> _startProbe;
    private readonly TimeSpan _successInterval;
    private readonly TimeSpan _failureInterval;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _cachedTargetGrace;
    private Task<CodexComposerTarget?>? _activeProbe;
    private CancellationTokenSource? _activeProbeCancellation;
    private DateTimeOffset _activeStartedAt;
    private DateTimeOffset _nextProbeAt = DateTimeOffset.MinValue;
    private CodexComposerTarget? _target;
    private DateTimeOffset _targetObservedAt;
    private bool _activeProbeTimedOut;
    private bool _disposed;

    public ComposerProbeController(
        Func<double, double, CancellationToken, Task<CodexComposerTarget?>> startProbe,
        TimeSpan successInterval,
        TimeSpan failureInterval,
        TimeSpan timeout,
        TimeSpan cachedTargetGrace)
    {
        ArgumentNullException.ThrowIfNull(startProbe);
        if (successInterval <= TimeSpan.Zero ||
            failureInterval <= TimeSpan.Zero ||
            timeout <= TimeSpan.Zero ||
            cachedTargetGrace < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(successInterval),
                "Probe intervals must be positive and the cached-target grace interval cannot be negative.");
        }

        _startProbe = startProbe;
        _successInterval = successInterval;
        _failureInterval = failureInterval;
        _timeout = timeout;
        _cachedTargetGrace = cachedTargetGrace;
    }

    public ComposerProbeObservation Poll(double width, double height, DateTimeOffset now)
    {
        if (_disposed)
        {
            return default;
        }

        if (_activeProbe is not null)
        {
            if (_activeProbe.IsCompleted)
            {
                CompleteActiveProbe(now);
            }
            else if (now - _activeStartedAt >= _timeout)
            {
                _activeProbeTimedOut = true;
                CancelActiveProbe();
                DiscardExpiredTarget(now);
                return new ComposerProbeObservation(_target, TimedOut: true);
            }
        }

        if (_activeProbe is null && now >= _nextProbeAt)
        {
            StartProbe(width, height, now);
            if (_activeProbe?.IsCompleted is true)
            {
                CompleteActiveProbe(now);
            }
        }

        return new ComposerProbeObservation(
            _target,
            TimedOut: _activeProbeTimedOut && _activeProbe is not null);
    }

    public void Invalidate(DateTimeOffset now)
    {
        if (_disposed)
        {
            return;
        }

        _target = null;
        _targetObservedAt = default;
        _nextProbeAt = now;
    }

    public void Reset(DateTimeOffset now)
    {
        if (_disposed)
        {
            return;
        }

        _target = null;
        _targetObservedAt = default;
        _nextProbeAt = now;
        if (_activeProbe is not null)
        {
            _activeProbeTimedOut = true;
            CancelActiveProbe();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        CancelActiveProbe();
        _activeProbeCancellation?.Dispose();
        _activeProbeCancellation = null;
        _target = null;
        _targetObservedAt = default;
    }

    private void StartProbe(double width, double height, DateTimeOffset now)
    {
        try
        {
            _activeProbeCancellation = new CancellationTokenSource();
            _activeProbe = _startProbe(width, height, _activeProbeCancellation.Token) ??
                Task.FromResult<CodexComposerTarget?>(null);
            _activeStartedAt = now;
            _activeProbeTimedOut = false;
        }
        catch
        {
            // The probe factory is an external automation boundary. A synchronous
            // startup failure is treated exactly like a failed probe.
            _activeProbe = null;
            _activeProbeCancellation?.Dispose();
            _activeProbeCancellation = null;
            DiscardExpiredTarget(now);
            _nextProbeAt = now + _failureInterval;
        }
    }

    private void CompleteActiveProbe(DateTimeOffset now)
    {
        var completedProbe = _activeProbe!;
        _activeProbe = null;
        var completedCancellation = _activeProbeCancellation;
        _activeProbeCancellation = null;
        CodexComposerTarget? completedResult = null;
        try
        {
            completedResult = completedProbe.GetAwaiter().GetResult();
        }
        catch
        {
            // UI Automation and process inspection are external boundaries.
            // Faulted probes fail closed and are retried after backoff.
        }
        finally
        {
            completedCancellation?.Dispose();
        }

        var result = _activeProbeTimedOut ? null : completedResult;
        _activeProbeTimedOut = false;
        if (result is { } target)
        {
            _target = target;
            _targetObservedAt = now;
            _nextProbeAt = now + _successInterval;
        }
        else
        {
            DiscardExpiredTarget(now);
            _nextProbeAt = now + _failureInterval;
        }
    }

    private void CancelActiveProbe()
    {
        try
        {
            _activeProbeCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion won the race and already disposed the cancellation source.
        }
    }

    private void DiscardExpiredTarget(DateTimeOffset now)
    {
        if (_target is null ||
            now < _targetObservedAt ||
            now - _targetObservedAt > _cachedTargetGrace)
        {
            _target = null;
            _targetObservedAt = default;
        }
    }
}
