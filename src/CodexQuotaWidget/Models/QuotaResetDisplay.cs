namespace CodexQuotaWidget.Models;

internal static class QuotaResetDisplay
{
    private const long MinutesPerDay = 24 * 60;

    public static string FormatCountdown(DateTimeOffset resetAt, DateTimeOffset now)
    {
        if (resetAt <= now)
        {
            return "等待刷新";
        }

        var remainingMinutes = (long)Math.Ceiling((resetAt - now).TotalMinutes);
        var days = remainingMinutes / MinutesPerDay;
        var hours = remainingMinutes % MinutesPerDay / 60;
        var minutes = remainingMinutes % 60;

        return $"还剩{days}天{hours}小时\n{minutes}分钟重置";
    }
}
