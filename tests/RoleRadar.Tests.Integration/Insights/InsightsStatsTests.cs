namespace RoleRadar.Tests.Integration.Insights;

using System.Net;
using System.Text.Json;
using Xunit;

/// <summary>
/// T067: GET /api/v1/insights/stats over the real API and the fictional seed. Expected values were computed by hand
/// from the seed definitions in <see cref="InsightsSeed"/> (applications at noon UTC, as-of 2026-09-30, a Wednesday),
/// not by calling the product code.
/// </summary>
public sealed class InsightsStatsTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    private const string OwnerA = InsightsSeed.OwnerA;
    private const string AsOfText = "2026-09-30";

    private ODataTestHost Host => fixture.Host;

    private async Task<JsonElement> StatsAsync(string owner, string window, string asOf = AsOfText)
    {
        var (status, body) = await InsightsTestSupport.RawAsync(Host, owner, $"/api/v1/insights/stats?window={window}&asOf={asOf}");
        Assert.True(status == HttpStatusCode.OK, $"stats {window} {asOf}: expected 200, got {(int)status} with body '{body}'");
        return InsightsTestSupport.ParseRoot(body);
    }

    private static List<(string WeekStart, int Count)> Weeks(JsonElement stats) =>
        stats.GetProperty("weeklyApplications").EnumerateArray()
            .Select(w => (w.GetProperty("weekStart").GetString()!, w.GetProperty("count").GetInt32()))
            .ToList();

    private static int[] RunYields(JsonElement stats) =>
        stats.GetProperty("searchRunYield").GetProperty("runs").EnumerateArray()
            .Select(r => r.GetProperty("newPostings").GetInt32())
            .ToArray();

    [Fact]
    public async Task Last90Days_EqualsTheHandComputedSeedValues()
    {
        var stats = await StatsAsync(OwnerA, "last90Days");

        Assert.Equal(AsOfText, stats.GetProperty("asOf").GetString());
        Assert.Equal("last90Days", stats.GetProperty("window").GetString());
        Assert.Equal("2026-07-03", stats.GetProperty("windowStart").GetString());

        // 25 applications are in the window; 14 have a response (interview, offer, offer accepted, offer declined or
        // rejected). Days to the first response, sorted: 5 5 5 5 6 7 10 10 10 10 10 15 20 20. Even count: the mean of
        // the 7th and 8th values, 10 and 10.
        var responseTime = stats.GetProperty("responseTime");
        Assert.Equal(14, responseTime.GetProperty("sampleSize").GetInt32());
        Assert.Equal(10d, responseTime.GetProperty("medianDays").GetDouble());

        Assert.Equal(
            [
                ("2026-06-29", 1), ("2026-07-06", 2), ("2026-07-13", 0), ("2026-07-20", 1), ("2026-07-27", 3),
                ("2026-08-03", 0), ("2026-08-10", 5), ("2026-08-17", 2), ("2026-08-24", 2), ("2026-08-31", 3),
                ("2026-09-07", 2), ("2026-09-14", 2), ("2026-09-21", 1), ("2026-09-28", 1)
            ],
            Weeks(stats));
        Assert.Equal(25, Weeks(stats).Sum(w => w.Count));

        Assert.Equal([4, 0, 12, 3, 9, 6], RunYields(stats));
        Assert.False(stats.GetProperty("searchRunYield").GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task AllTime_EqualsTheHandComputedSeedValues()
    {
        var stats = await StatsAsync(OwnerA, "allTime");

        Assert.Equal("allTime", stats.GetProperty("window").GetString());
        Assert.Equal(JsonValueKind.Null, stats.GetProperty("windowStart").ValueKind);

        // 27 applications up to the as-of date (the one dated after it is excluded); 15 have a response. The old
        // application that is outside the 90 day window (applied 90 days before as-of, answered after 10 days) joins.
        var responseTime = stats.GetProperty("responseTime");
        Assert.Equal(15, responseTime.GetProperty("sampleSize").GetInt32());
        Assert.Equal(10d, responseTime.GetProperty("medianDays").GetDouble());

        Assert.Equal(
            [
                ("2026-06-22", 1), ("2026-06-29", 2), ("2026-07-06", 2), ("2026-07-13", 0), ("2026-07-20", 1),
                ("2026-07-27", 3), ("2026-08-03", 0), ("2026-08-10", 5), ("2026-08-17", 2), ("2026-08-24", 2),
                ("2026-08-31", 3), ("2026-09-07", 2), ("2026-09-14", 2), ("2026-09-21", 1), ("2026-09-28", 1)
            ],
            Weeks(stats));
        Assert.Equal(27, Weeks(stats).Sum(w => w.Count));

        Assert.Equal([7, 4, 0, 12, 3, 9, 6], RunYields(stats));
    }

    [Theory]
    [InlineData("last90Days", true)]
    [InlineData("allTime", false)]
    public async Task ResponseTimeSampleSize_EqualsTheRespondedLensCount_ForTheSameWindowAndAsOf(string window, bool last90Days)
    {
        var snapshot = InsightsLensSnapshot.Load();
        Assert.Equal(AsOfText, snapshot["asOf"]!.GetValue<string>());
        var respondedFilter = InsightsLensSnapshot.Filter(snapshot, "lenses", "responded", last90Days);

        var lensCount = await InsightsTestSupport.CountAsync(Host, OwnerA, "Applications", respondedFilter);
        var stats = await StatsAsync(OwnerA, window);

        Assert.Equal(lensCount, stats.GetProperty("responseTime").GetProperty("sampleSize").GetInt32());
    }

    [Theory]
    [InlineData("last90Days", true)]
    [InlineData("allTime", false)]
    public async Task WeeklyApplicationsTotal_EqualsTheAppliedLensCount_ForTheSameWindowAndAsOf(string window, bool last90Days)
    {
        var snapshot = InsightsLensSnapshot.Load();
        var appliedFilter = InsightsLensSnapshot.Filter(snapshot, "lenses", "applied", last90Days);

        var lensCount = await InsightsTestSupport.CountAsync(Host, OwnerA, "Applications", appliedFilter);
        var stats = await StatsAsync(OwnerA, window);

        Assert.Equal(lensCount, Weeks(stats).Sum(w => w.Count));
    }

    [Fact]
    public async Task OwnerB_SeesOnlyItsOwnRows()
    {
        var stats = await StatsAsync(InsightsSeed.OwnerB, "last90Days");

        // Owner B: applications at 30, 35 and 70 days; two answered after 5 days each.
        Assert.Equal(2, stats.GetProperty("responseTime").GetProperty("sampleSize").GetInt32());
        Assert.Equal(5d, stats.GetProperty("responseTime").GetProperty("medianDays").GetDouble());
        Assert.Equal(3, Weeks(stats).Sum(w => w.Count));
        Assert.Equal([17], RunYields(stats));
    }

    [Fact]
    public async Task OwnerC_WithNoData_GetsNullMedian_ZeroFilledWeeks_AndNoRuns()
    {
        var stats = await StatsAsync(InsightsSeed.OwnerC, "last90Days");

        Assert.Equal(JsonValueKind.Null, stats.GetProperty("responseTime").GetProperty("medianDays").ValueKind);
        Assert.Equal(0, stats.GetProperty("responseTime").GetProperty("sampleSize").GetInt32());
        Assert.Equal(14, Weeks(stats).Count);
        Assert.All(Weeks(stats), w => Assert.Equal(0, w.Count));
        Assert.Empty(RunYields(stats));
        Assert.False(stats.GetProperty("searchRunYield").GetProperty("truncated").GetBoolean());

        var allTime = await StatsAsync(InsightsSeed.OwnerC, "allTime");
        Assert.Empty(Weeks(allTime));
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task NonGetMethods_Are405_WithAllowGet(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/insights/stats?window=last90Days&asOf={AsOfText}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Host.Token(OwnerA));

        using var response = await Host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("GET", response.Content.Headers.Allow);
    }

    [Fact]
    public async Task WithoutAToken_Is401()
    {
        using var response = await Host.Client.GetAsync($"/api/v1/insights/stats?window=last90Days&asOf={AsOfText}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?window=last90Days")]
    [InlineData("?asOf=2026-09-30")]
    [InlineData("?window=&asOf=2026-09-30")]
    [InlineData("?window=bogus&asOf=2026-09-30")]
    [InlineData("?window=90d&asOf=2026-09-30")]
    [InlineData("?window=Last90Days&asOf=2026-09-30")]
    [InlineData("?window=last90Days&asOf=")]
    [InlineData("?window=last90Days&asOf=not-a-date")]
    [InlineData("?window=last90Days&asOf=2026-13-40")]
    [InlineData("?window=last90Days&asOf=2026-9-30")]
    [InlineData("?window=last90Days&asOf=2026-09-30T00:00:00Z")]
    public async Task MissingOrBadWindowOrAsOf_Is400_WithAValidationProblemDocument(string query)
    {
        var (status, body) = await InsightsTestSupport.RawAsync(Host, OwnerA, "/api/v1/insights/stats" + query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("validation_error", root.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task AsOfMoreThanOneDayInTheFuture_Is400_WithAValidationProblemDocument()
    {
        var farFuture = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var (status, body) = await InsightsTestSupport.RawAsync(Host, OwnerA, $"/api/v1/insights/stats?window=last90Days&asOf={farFuture}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal("validation_error", root.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task AsOfTomorrow_IsAllowed()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var stats = await StatsAsync(OwnerA, "last90Days", tomorrow);

        Assert.Equal(tomorrow, stats.GetProperty("asOf").GetString());
    }
}
