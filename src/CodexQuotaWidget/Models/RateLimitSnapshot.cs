namespace CodexQuotaWidget.Models;

public sealed record RateLimitWindow(
    double UsedPercent,
    int WindowMinutes,
    DateTimeOffset ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}

public sealed record RateLimitSnapshot(
    RateLimitWindow FiveHour,
    RateLimitWindow Weekly,
    DateTimeOffset ObservedAt,
    string SourceFile);
