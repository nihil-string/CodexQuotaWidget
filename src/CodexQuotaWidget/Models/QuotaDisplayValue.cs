namespace CodexQuotaWidget.Models;

internal readonly record struct QuotaDisplayValue(string Text, double RingValue)
{
    public static QuotaDisplayValue From(RateLimitWindow? window)
    {
        if (window is null)
        {
            return new QuotaDisplayValue("—", 0);
        }

        var remainingPercent = window.RemainingPercent;
        return new QuotaDisplayValue(
            $"{Math.Round(remainingPercent):0}%",
            remainingPercent);
    }
}
