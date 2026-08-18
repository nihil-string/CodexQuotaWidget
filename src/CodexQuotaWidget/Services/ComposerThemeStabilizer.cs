namespace CodexQuotaWidget.Services;

internal readonly record struct ComposerTheme(bool IsLightBackground, int BackgroundRgb);

internal readonly record struct ComposerThemeDecision(
    ComposerTheme Theme,
    bool RequiresConfirmation);

internal sealed class ComposerThemeStabilizer
{
    // Foreground activation can briefly expose an unpainted DWM/Electron frame.
    // Keep the last stable theme until a separate probe confirms a large change.
    private const int DefaultBackgroundRgb = 0xFAFAFA;
    private const int SuspiciousBlackChannelMaximum = 8;
    private const int ConsistentChannelTolerance = 12;
    private const int RequiredConsistentSamples = 2;

    private ComposerTheme _current = new(IsLightBackground: true, DefaultBackgroundRgb);
    private ComposerTheme? _pending;
    private int _pendingSampleCount;
    private bool _hasAcceptedSample;

    public ComposerTheme Current => _current;

    public ComposerThemeDecision Observe(bool isLightBackground, int backgroundRgb)
    {
        var observed = new ComposerTheme(
            isLightBackground,
            backgroundRgb & 0x00FF_FFFF);
        if (!_hasAcceptedSample && !IsSuspiciousBlack(observed))
        {
            Accept(observed);
            return new ComposerThemeDecision(_current, RequiresConfirmation: false);
        }

        if (_hasAcceptedSample && !RequiresConfirmation(_current, observed))
        {
            Accept(observed);
            return new ComposerThemeDecision(_current, RequiresConfirmation: false);
        }

        if (_pending is { } pending && AreConsistent(pending, observed))
        {
            _pendingSampleCount++;
        }
        else
        {
            _pending = observed;
            _pendingSampleCount = 1;
        }

        if (_pendingSampleCount >= RequiredConsistentSamples)
        {
            Accept(observed);
            return new ComposerThemeDecision(_current, RequiresConfirmation: false);
        }

        return new ComposerThemeDecision(_current, RequiresConfirmation: true);
    }

    private void Accept(ComposerTheme theme)
    {
        _current = theme;
        _hasAcceptedSample = true;
        _pending = null;
        _pendingSampleCount = 0;
    }

    private static bool RequiresConfirmation(ComposerTheme current, ComposerTheme observed) =>
        current.IsLightBackground != observed.IsLightBackground || IsSuspiciousBlack(observed);

    private static bool IsSuspiciousBlack(ComposerTheme theme)
    {
        var red = theme.BackgroundRgb >> 16 & 0xff;
        var green = theme.BackgroundRgb >> 8 & 0xff;
        var blue = theme.BackgroundRgb & 0xff;
        return red <= SuspiciousBlackChannelMaximum &&
            green <= SuspiciousBlackChannelMaximum &&
            blue <= SuspiciousBlackChannelMaximum;
    }

    private static bool AreConsistent(ComposerTheme first, ComposerTheme second) =>
        first.IsLightBackground == second.IsLightBackground &&
        ChannelDifference(first.BackgroundRgb >> 16, second.BackgroundRgb >> 16) <= ConsistentChannelTolerance &&
        ChannelDifference(first.BackgroundRgb >> 8, second.BackgroundRgb >> 8) <= ConsistentChannelTolerance &&
        ChannelDifference(first.BackgroundRgb, second.BackgroundRgb) <= ConsistentChannelTolerance;

    private static int ChannelDifference(int first, int second) =>
        Math.Abs((first & 0xff) - (second & 0xff));
}
