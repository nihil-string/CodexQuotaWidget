using System.Text;
using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class CodexUsageClientTests
{
    [Fact]
    public void ParsesCurrentWhamUsageShape()
    {
        var json = Encoding.UTF8.GetBytes("""
            {
              "plan_type": "pro",
              "rate_limit": {
                "primary_window": {
                  "used_percent": 38.5,
                  "limit_window_seconds": 18000,
                  "reset_at": 1783832400
                },
                "secondary_window": {
                  "used_percent": 12,
                  "limit_window_seconds": 604800,
                  "reset_at": 1784419200
                }
              }
            }
            """);

        var parsed = CodexUsageClient.TryParseUsageJson(json, out var snapshot);

        Assert.True(parsed);
        Assert.NotNull(snapshot);
        Assert.Equal(38.5, snapshot.FiveHour.UsedPercent);
        Assert.Equal(12, snapshot.Weekly.UsedPercent);
        Assert.Equal(61.5, snapshot.FiveHour.RemainingPercent);
        Assert.Equal(88, snapshot.Weekly.RemainingPercent);
    }

    [Fact]
    public void RejectsResponsesWithoutBothExpectedWindows()
    {
        var json = Encoding.UTF8.GetBytes("""{"rate_limit":{"primary_window":null}}""");

        Assert.False(CodexUsageClient.TryParseUsageJson(json, out var snapshot));
        Assert.Null(snapshot);
    }
}
