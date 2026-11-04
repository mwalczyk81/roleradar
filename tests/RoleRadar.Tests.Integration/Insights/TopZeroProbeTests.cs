namespace RoleRadar.Tests.Integration.Insights;

using System.Net;
using System.Text.Json;
using Xunit;

/// <summary>
/// <c>$top=0</c> is a valid "count only" request on every OData entity set (ADR 0029): the page query is skipped and the
/// answer is an empty <c>value</c> array, plus <c>@odata.count</c> when <c>$count=true</c>. It used to answer an
/// empty-bodied HTTP 500 on Postings and Applications because MongoDB rejects <c>$limit: 0</c>.
/// </summary>
public sealed class TopZeroProbeTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    private const string Owner = InsightsSeed.OwnerA;

    public static TheoryData<string> AllSets =>
    [
        "Postings", "Companies", "Applications", "Notes", "Criteria", "SearchRuns"
    ];

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task TopZero_WithCount_ReturnsEmptyValueAndTheTotal(string entitySet)
    {
        var total = await TotalAsync(entitySet);

        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, $"/odata/{entitySet}?$top=0&$count=true");

        Assert.True(status == HttpStatusCode.OK, $"{entitySet}: expected 200, got {(int)status} with body '{body}'");
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(0, root.GetProperty("value").GetArrayLength());
        Assert.Equal(total, root.GetProperty("@odata.count").GetInt32());
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task TopZero_WithoutCount_ReturnsEmptyValueAndNoCount(string entitySet)
    {
        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, $"/odata/{entitySet}?$top=0");

        Assert.True(status == HttpStatusCode.OK, $"{entitySet}: expected 200, got {(int)status} with body '{body}'");
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(0, root.GetProperty("value").GetArrayLength());
        Assert.False(root.TryGetProperty("@odata.count", out _), "no @odata.count without $count=true");
    }

    [Theory]
    [InlineData("Postings", "hasApplication eq true")]
    [InlineData("Applications", "hasOffer eq true")]
    [InlineData("Companies", "contains(tolower(name),'northwind')")]
    public async Task TopZero_WithFilter_CountsOnlyTheMatchingRows(string entitySet, string filter)
    {
        var expected = await InsightsTestSupport.CountAsync(fixture.Host, Owner, entitySet, filter);
        Assert.True(expected > 0, "the seed must contain matching rows for this case");

        var path = $"/odata/{entitySet}?$filter={Uri.EscapeDataString(filter)}&$top=0&$count=true";
        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, path);

        Assert.True(status == HttpStatusCode.OK, $"{path}: expected 200, got {(int)status} with body '{body}'");
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(0, root.GetProperty("value").GetArrayLength());
        Assert.Equal(expected, root.GetProperty("@odata.count").GetInt32());
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task TopZero_WithSelect_StillReturnsTheEnvelope(string entitySet)
    {
        var total = await TotalAsync(entitySet);

        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, $"/odata/{entitySet}?$top=0&$select=id&$count=true");

        Assert.True(status == HttpStatusCode.OK, $"{entitySet}: expected 200, got {(int)status} with body '{body}'");
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(0, root.GetProperty("value").GetArrayLength());
        Assert.Equal(total, root.GetProperty("@odata.count").GetInt32());
    }

    [Fact]
    public async Task TopZero_IgnoresSkipAndOrderBy_AndStillCounts()
    {
        var total = await TotalAsync("Postings");

        var (status, body) = await InsightsTestSupport.RawAsync(
            fixture.Host, Owner, "/odata/Postings?$top=0&$skip=5&$orderby=score%20desc&$count=true");

        Assert.Equal(HttpStatusCode.OK, status);
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(0, root.GetProperty("value").GetArrayLength());
        Assert.Equal(total, root.GetProperty("@odata.count").GetInt32());
    }

    [Fact]
    public async Task OtherOwners_AreNotCounted_ByTopZero()
    {
        var (status, body) = await InsightsTestSupport.RawAsync(
            fixture.Host, InsightsSeed.OwnerC, "/odata/Postings?$top=0&$count=true");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, InsightsTestSupport.ParseRoot(body).GetProperty("@odata.count").GetInt32());
    }

    [Theory]
    [InlineData("/odata/Postings?$top=101", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Postings?$top=-1", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Applications?$top=abc", HttpStatusCode.BadRequest)]
    public async Task MaxTopAndOtherTopValidation_AreUnchanged(string path, HttpStatusCode expected)
    {
        var (status, _) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, path);

        Assert.Equal(expected, status);
    }

    [Theory]
    [MemberData(nameof(AllSets))]
    public async Task TopOne_StillReturnsOneRowWhenRowsExist(string entitySet)
    {
        var total = await TotalAsync(entitySet);
        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, $"/odata/{entitySet}?$top=1&$count=true");

        Assert.Equal(HttpStatusCode.OK, status);
        var root = InsightsTestSupport.ParseRoot(body);
        Assert.Equal(Math.Min(1, total), root.GetProperty("value").GetArrayLength());
        Assert.Equal(total, root.GetProperty("@odata.count").GetInt32());
    }

    private async Task<int> TotalAsync(string entitySet)
    {
        // A normal page request gives the reference total.
        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, $"/odata/{entitySet}?$top=1&$count=true");
        Assert.Equal(HttpStatusCode.OK, status);
        return JsonDocument.Parse(body).RootElement.GetProperty("@odata.count").GetInt32();
    }
}
