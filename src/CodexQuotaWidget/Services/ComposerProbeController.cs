namespace CodexQuotaWidget.Services;

internal readonly record struct ComposerProbeObservation(
    CodexComposerTarget? Target,
    bool TimedOut);

internal sealed class ComposerProbeController : IDisposable
{
    private readonly Func<double, double, Task<CodexComposerTarget?>> _startProbe;
    private readonly TimeSpan _successInterval;
    private readonly TimeSpan _failureInterval;
    private readonly TimeSpan _timeout;
    private Task<CodexComposerTarget?>? _activeProbe;
    private DateTimeOffset _activeStartedAt;
    private DateTimeOffset _nextProbeAt = DateTimeOffset.MinValue;
    private CodexComposerTarget? _target;
    private bool _activeProbeTimedOut;
    private bool _disposed;

    public ComposerProbeController(
        Func<double, double, Task<CodexComposerTarget?>> startProbe,
        TimeSpan successInterval,
        TimeSpan failureInterval,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(startProbe);
        if (successInterval <= TimeSpan.Zero ||
            failureInterval <= TimeSpan.Zero ||
            timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(successInterval), "Probe intervals must be positive.");
        }

        _startProbe = startProbe;
        _successInterval = successInterval;
        _failureInterval = failureInterval;
        _timeout = timeout;
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
                _target = null;
                return new ComposerProbeObservation(Target: null, TimedOut: true);
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
        _nextProbeAt = now;
    }

    public void Reset(DateTimeOffset now)
    {
        if (_disposed)
        {
            return;
        }

        _target = null;
        _nextProbeAt = now;
        if (_activeProbe is not null)
        {
            _activeProbeTimedOut = true;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _target = null;
    }

    private void StartProbe(double width, double height, DateTimeOffset now)
    {
        try
        {
            _activeProbe = _startProbe(width, height) ??
                Task.FromResult<CodexComposerTarget?>(null);
            _activeStartedAt = now;
            _activeProbeTimedOut = false;
        }
        catch
        {
            // The probe factory is an external automation boundary. A synchronous
            // startup failure is treated exactly like a failed probe.
            _activeProbe = null;
            _target = null;
            _nextProbeAt = now + _failureInterval;
        }
    }

    private void CompleteActiveProbe(DateTimeOffset now)
    {
        var completedProbe = _activeProbe!;
        _activeProbe = null;
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

        var result = _activeProbeTimedOut ? null : completedResult;
        _activeProbeTimedOut = false;
        if (result is { } target)
        {
            _target = target;
            _nextProbeAt = now + _successInterval;
        }
        else
        {
            _target = null;
            _nextProbeAt = now + _failureInterval;
        }
    }
}
