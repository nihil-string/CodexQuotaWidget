using System.Text.Json;
using CodexQuotaWidget.Services;

try
{
    using var client = new CodexUsageClient();
    var snapshot = await client.FetchAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        status = "ok",
        fiveHourUsedPercent = snapshot.FiveHour?.UsedPercent,
        fiveHourRemainingPercent = snapshot.FiveHour?.RemainingPercent,
        fiveHourResetAt = snapshot.FiveHour?.ResetsAt,
        weeklyUsedPercent = snapshot.Weekly?.UsedPercent,
        weeklyRemainingPercent = snapshot.Weekly?.RemainingPercent,
        weeklyResetAt = snapshot.Weekly?.ResetsAt
    }));
    return 0;
}
catch (UsageFetchException exception)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        status = "error",
        kind = exception.Kind.ToString(),
        message = exception.Message
    }));
    return 1;
}
