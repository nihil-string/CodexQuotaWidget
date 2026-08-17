namespace CodexQuotaWidget.Models;

internal readonly record struct QuotaLayoutState(
    bool ShowFiveHour,
    bool ShowWeekly,
    bool ShowSeparator,
    double WindowWidth);

internal static class QuotaLayout
{
    private const double DualQuotaWidth = 148;
    private const double SingleQuotaWidth = 72;

    public static QuotaLayoutState Create(bool hasFiveHour, bool hasWeekly)
    {
        if (!hasFiveHour && !hasWeekly)
        {
            throw new InvalidOperationException("At least one quota window is required for layout.");
        }

        var showSeparator = hasFiveHour && hasWeekly;
        return new QuotaLayoutState(
            hasFiveHour,
            hasWeekly,
            showSeparator,
            showSeparator ? DualQuotaWidth : SingleQuotaWidth);
    }

    public static double CalculateLeftPreservingRightEdge(
        double currentLeft,
        double currentWidth,
        double targetWidth,
        double virtualScreenLeft,
        double virtualScreenWidth)
    {
        if (!double.IsFinite(currentLeft) ||
            !double.IsFinite(currentWidth) || currentWidth <= 0 ||
            !double.IsFinite(targetWidth) || targetWidth <= 0 ||
            !double.IsFinite(virtualScreenLeft) ||
            !double.IsFinite(virtualScreenWidth) || virtualScreenWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentWidth), "Window and screen metrics must be finite and positive.");
        }

        if (targetWidth >= virtualScreenWidth)
        {
            return virtualScreenLeft;
        }

        var rightEdge = currentLeft + currentWidth;
        return Math.Clamp(
            rightEdge - targetWidth,
            virtualScreenLeft,
            virtualScreenLeft + virtualScreenWidth - targetWidth);
    }
}
