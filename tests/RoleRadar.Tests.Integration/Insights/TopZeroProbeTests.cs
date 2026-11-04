namespace RoleRadar.Tests.Integration.Insights;

using System.Net;
using Xunit;

/// <summary>
/// Records what the 006 pipeline really does with <c>$top=0</c> (research R1, ADR 0029). The Insights counts use
/// <c>$top=1</c> either way; this test pins the observed behavior so a change to it is noticed.
/// </summary>
public sealed class TopZeroProbeTests(InsightsFixture fixture, ITestOutputHelper output) : IClassFixture<InsightsFixture>
{
    [Theory]
    [InlineData("Postings")]
    [InlineData("Applications")]
    public async Task TopZero_Behavior_IsRecorded(string entitySet)
    {
        var (status, body) = await InsightsTestSupport.RawAsync(
            fixture.Host, InsightsSeed.OwnerA, $"/odata/{entitySet}?$top=0&$count=true");
        output.WriteLine($"TOP0-PROBE {entitySet}: status={(int)status} body={Truncate(body)}");

        // Observed behavior is pinned in the assertion below once recorded (see ADR 0029).
        Assert.Equal(ExpectedStatus, status);
    }

    [Theory]
    [InlineData("Postings")]
    [InlineData("Applications")]
    public async Task TopOne_WithCount_ReturnsOneRowAndTheTotal(string entitySet)
    {
        var (status, body) = await InsightsTestSupport.RawAsync(
            fixture.Host, InsightsSeed.OwnerA, $"/odata/{entitySet}?$top=1&$select=id&$count=true");
        Assert.Equal(HttpStatusCode.OK, status);

        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(1, root.GetProperty("value").GetArrayLength());
        Assert.True(root.GetProperty("@odata.count").GetInt32() > 1);
    }

    // Recorded 2026-11-04: the API answers 500 with an empty body (not a 400), so validation lets $top=0 through and the
    // failure comes later, consistent with MongoDB rejecting `$limit: 0`. The Insights counts therefore use $top=1.
    private const HttpStatusCode ExpectedStatus = HttpStatusCode.InternalServerError;

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";
}
