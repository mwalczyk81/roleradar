namespace RoleRadar.Tests.Integration.Insights;

using System.Net;
using Xunit;

/// <summary>
/// A1: a caller-supplied <c>$orderby</c> that names <c>id</c>, or repeats a key, used to answer an empty-bodied HTTP 500
/// because the stable <c>id</c> tiebreaker was appended a second time (and a repeated key was registered twice).
/// Rule: the first occurrence of a key wins, later repeats are ignored, and the tiebreaker is skipped when <c>id</c>
/// was already sorted. Existing default and sorted orders must not change.
/// </summary>
public sealed class OrderByRepeatedKeyTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    private const string Owner = InsightsSeed.OwnerA;

    public static TheoryData<string, string> SetAndSecondKey => new()
    {
        { "Postings", "title" },
        { "Companies", "name" },
        { "Applications", "appliedAt" },
        { "Notes", "heading" },
        { "Criteria", "name" },
        { "SearchRuns", "newPostings" },
    };

    [Theory]
    [MemberData(nameof(SetAndSecondKey))]
    public async Task OrderById_Ascending_Returns200_AndRowsAreSortedById(string entitySet, string _)
    {
        var ids = await IdsAsync(entitySet, "id");

        AssertSeeded(entitySet, ids);
        Assert.Equal(ids.OrderBy(i => i, StringComparer.Ordinal).ToList(), ids);
    }

    [Theory]
    [MemberData(nameof(SetAndSecondKey))]
    public async Task OrderById_Descending_Returns200_AndRowsAreSortedByIdDescending(string entitySet, string _)
    {
        var ids = await IdsAsync(entitySet, "id desc");

        AssertSeeded(entitySet, ids);
        Assert.Equal(ids.OrderByDescending(i => i, StringComparer.Ordinal).ToList(), ids);
    }

    [Theory]
    [MemberData(nameof(SetAndSecondKey))]
    public async Task OrderById_CombinedWithAnotherKey_InBothOrders_Returns200(string entitySet, string key)
    {
        var idFirst = await IdsAsync(entitySet, $"id,{key}");
        var idLast = await IdsAsync(entitySet, $"{key},id");

        AssertSeeded(entitySet, idFirst);
        AssertSeeded(entitySet, idLast);
        // id is unique, so sorting by it first makes the second key irrelevant.
        Assert.Equal(idFirst.OrderBy(i => i, StringComparer.Ordinal).ToList(), idFirst);
    }

    [Theory]
    [MemberData(nameof(SetAndSecondKey))]
    public async Task RepeatedKey_SameDirection_Returns200_AndEqualsTheSingleKeyOrder(string entitySet, string key)
    {
        var single = await IdsAsync(entitySet, key);
        var repeated = await IdsAsync(entitySet, $"{key},{key}");

        Assert.Equal(single, repeated);
    }

    [Theory]
    [MemberData(nameof(SetAndSecondKey))]
    public async Task RepeatedKey_OppositeDirection_FirstWins(string entitySet, string key)
    {
        var firstAsc = await IdsAsync(entitySet, $"{key} asc");
        var ascThenDesc = await IdsAsync(entitySet, $"{key} asc,{key} desc");
        var firstDesc = await IdsAsync(entitySet, $"{key} desc");
        var descThenAsc = await IdsAsync(entitySet, $"{key} desc,{key} asc");

        Assert.Equal(firstAsc, ascThenDesc);
        Assert.Equal(firstDesc, descThenAsc);
    }

    [Theory]
    [InlineData("Postings", "title", "disposition")]
    [InlineData("Applications", "status", "appliedAt")]
    public async Task RepeatedKey_AroundAnotherKey_FirstWins(string entitySet, string first, string second)
    {
        var expected = await IdsAsync(entitySet, $"{first},{second}");
        var actual = await IdsAsync(entitySet, $"{first},{second},{first} desc");

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Applications_AgeInDays_And_AppliedAt_ShareOneSortKey_FirstWins()
    {
        // ageInDays sorts on the stored appliedAt (reversed), so naming both is a repeated key.
        var ageFirst = await IdsAsync("Applications", "ageInDays,appliedAt");
        var ageOnly = await IdsAsync("Applications", "ageInDays");
        var appliedFirst = await IdsAsync("Applications", "appliedAt,ageInDays");
        var appliedOnly = await IdsAsync("Applications", "appliedAt");

        Assert.Equal(ageOnly, ageFirst);
        Assert.Equal(appliedOnly, appliedFirst);
    }

    [Theory]
    [InlineData("Postings", "title,title,title,title,title")]
    [InlineData("Applications", "status,status,status,status,status")]
    public async Task RepeatedKey_StillCountsTowardTheFiveKeyLimit_AtTheLimit(string entitySet, string orderBy)
    {
        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, $"/odata/{entitySet}?$orderby={Uri.EscapeDataString(orderBy)}");

        Assert.True(status == HttpStatusCode.OK, $"{entitySet}: expected 200, got {(int)status} with body '{body}'");
    }

    [Theory]
    [InlineData("/odata/Postings?$orderby=content", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Postings?$orderby=title,bogus", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Applications?$orderby=notes", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Applications?$orderby=status,bogus,status", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Notes?$orderby=content", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Companies?$orderby=bogus", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Criteria?$orderby=ruleText", HttpStatusCode.BadRequest)]
    [InlineData("/odata/SearchRuns?$orderby=bogus", HttpStatusCode.BadRequest)]
    [InlineData("/odata/Postings?$orderby=title,title,title,title,title,title", HttpStatusCode.BadRequest)]
    public async Task AllowlistAndKeyLimit_400s_AreUnchanged(string path, HttpStatusCode expected)
    {
        var (status, _) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, path);

        Assert.Equal(expected, status);
    }

    [Theory]
    [MemberData(nameof(SetAndSecondKey))]
    public async Task DefaultOrder_IsUnchanged_AndExplicitSortsStillTieBreakById(string entitySet, string key)
    {
        // No $orderby: the default order is deterministic across two reads.
        var first = await IdsAsync(entitySet, null);
        var second = await IdsAsync(entitySet, null);
        Assert.Equal(first, second);

        // A sort by one key is stable across two reads (the id tiebreaker is still applied).
        var sortedA = await IdsAsync(entitySet, key);
        var sortedB = await IdsAsync(entitySet, key);
        Assert.Equal(sortedA, sortedB);
    }

    // The Insights seed has postings, companies and applications. Notes, criteria and search runs are empty for this
    // owner: the sort must still translate (200), which is what the status assertion in IdsAsync proves.
    private static void AssertSeeded(string entitySet, List<string> ids)
    {
        if (entitySet is "Postings" or "Companies" or "Applications")
        {
            Assert.NotEmpty(ids);
        }
    }

    private async Task<List<string>> IdsAsync(string entitySet, string? orderBy)
    {
        var query = orderBy is null ? "" : $"&$orderby={Uri.EscapeDataString(orderBy)}";
        var path = $"/odata/{entitySet}?$top=100&$select=id{query}";
        var (status, body) = await InsightsTestSupport.RawAsync(fixture.Host, Owner, path);

        Assert.True(status == HttpStatusCode.OK, $"{path}: expected 200, got {(int)status} with body '{body}'");
        return InsightsTestSupport.ParseRoot(body)
            .GetProperty("value")
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetString() ?? string.Empty)
            .ToList();
    }
}
