namespace RoleRadar.Tests.Integration;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

/// <summary>
/// Phase 5 (T048): query contract, owner isolation and allowlists for Companies, Notes, Criteria and
/// SearchRuns. Every seeded document uses the shape the repositories really write today (no read-model
/// fields such as linked_entity_* or has_effective_to), so these tests also prove the legacy-data path.
/// </summary>
public sealed class ODataRemainingEntitiesTests : IClassFixture<RemainingEntitiesFixture>
{
    private const string OwnerA = RemainingEntitiesSeed.OwnerA;
    private const string OwnerB = RemainingEntitiesSeed.OwnerB;
    private const string OwnerC = RemainingEntitiesSeed.OwnerC;

    private static Guid G(int n) => RemainingEntitiesSeed.G(n);

    private readonly ODataTestHost _host;

    public ODataRemainingEntitiesTests(RemainingEntitiesFixture fixture) => _host = fixture.Host;

    private async Task<JsonElement> OkAsync(string owner, string path)
    {
        var (status, body) = await _host.GetJsonAsync(owner, path);
        Assert.True(status == HttpStatusCode.OK, $"{path} returned {(int)status}: {body.RootElement}");
        return body.RootElement.Clone();
    }

    private static List<string> Strings(JsonElement page, string property) =>
        page.GetProperty("value").EnumerateArray().Select(e => e.GetProperty(property).GetString()!).ToList();

    // ---------------------------------------------------------------- Companies

    [Fact]
    public async Task Companies_SortByName_IsCaseInsensitive_AndSupportsNameSearch_AndCount()
    {
        var asc = await OkAsync(OwnerA, "/odata/Companies?$orderby=name%20asc&$count=true");
        Assert.Equal(["Acme Robotics", "beta Systems", "zeta labs"], Strings(asc, "name"));
        Assert.Equal(3, asc.GetProperty("@odata.count").GetInt32());

        var desc = await OkAsync(OwnerA, "/odata/Companies?$orderby=name%20desc");
        Assert.Equal(["zeta labs", "beta Systems", "Acme Robotics"], Strings(desc, "name"));

        var search = await OkAsync(OwnerA, "/odata/Companies?$filter=contains(tolower(name),'acme')&$count=true");
        Assert.Equal(["Acme Robotics"], Strings(search, "name"));
        Assert.Equal(1, search.GetProperty("@odata.count").GetInt32());

        var byStatus = await OkAsync(OwnerA, "/odata/Companies?$filter=status%20eq%20'active'&$orderby=name%20asc");
        Assert.Equal(["beta Systems", "zeta labs"], Strings(byStatus, "name"));
    }

    [Fact]
    public async Task Companies_ExposeReadModelFields_AndNeverOwnerId()
    {
        var page = await OkAsync(OwnerA, "/odata/Companies?$filter=name%20eq%20'Acme Robotics'");
        var row = page.GetProperty("value")[0];
        Assert.Equal("target", row.GetProperty("status").GetString());
        Assert.Equal("public", row.GetProperty("ownershipType").GetString());
        Assert.Equal("Denver, CO", row.GetProperty("headquarters").GetString());
        Assert.Equal("fintech", row.GetProperty("categoryTags")[0].GetString());
        Assert.False(row.TryGetProperty("ownerId", out _));
        Assert.False(row.TryGetProperty("owner_id", out _));
    }

    // ---------------------------------------------------------------- Notes

    [Fact]
    public async Task Notes_DefaultOrderByUpdatedAtDescending_And_SortByHeadingIsCaseInsensitive()
    {
        var recent = await OkAsync(OwnerA, "/odata/Notes?$orderby=updatedAt%20desc");
        Assert.Equal(["newest strategy memo", "Middle notes", "Older interview prep"], Strings(recent, "heading"));

        var byHeading = await OkAsync(OwnerA, "/odata/Notes?$orderby=heading%20asc");
        Assert.Equal(["Middle notes", "newest strategy memo", "Older interview prep"], Strings(byHeading, "heading"));

        var byArea = await OkAsync(OwnerA, "/odata/Notes?$filter=linkedEntityType%20eq%20'area-b'");
        Assert.Equal(["newest strategy memo"], Strings(byArea, "heading"));
    }

    [Theory]
    [InlineData("/odata/Notes?$filter=contains(tolower(content),'secret')")]
    [InlineData("/odata/Notes?$filter=content%20eq%20'secret body one'")]
    [InlineData("/odata/Notes?$orderby=content%20asc")]
    public async Task Notes_ContentIsRejectedWith400_InFilterAndOrderBy(string path)
    {
        var (status, _) = await _host.GetJsonAsync(OwnerA, path);
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // ---------------------------------------------------------------- Criteria

    [Fact]
    public async Task Criteria_ExposeStoredKeyAndValue_AsNameAndRuleText()
    {
        var page = await OkAsync(OwnerA, "/odata/Criteria?$filter=name%20eq%20'Salary floor'");
        var row = page.GetProperty("value")[0];
        Assert.Equal("weight", row.GetProperty("category").GetString());
        Assert.Equal("At least 180k", row.GetProperty("ruleText").GetString());
        Assert.False(row.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.String, row.GetProperty("effectiveTo").ValueKind);
    }

    [Fact]
    public async Task Criteria_SortByEffectiveTo_PutsOpenEndedLast_InBothDirections()
    {
        var asc = await OkAsync(OwnerA, "/odata/Criteria?$orderby=effectiveTo%20asc");
        Assert.Equal(["Salary floor", "growth stage", "remote only"], Strings(asc, "name"));

        var desc = await OkAsync(OwnerA, "/odata/Criteria?$orderby=effectiveTo%20desc");
        Assert.Equal(["growth stage", "Salary floor", "remote only"], Strings(desc, "name"));
    }

    // The web interval filter: effective on D means effectiveFrom <= D and (effectiveTo is open or D < effectiveTo).
    // That is the domain's Criterion.IsEffectiveAt, so the end date is exclusive.
    private static string EffectiveOn(DateTime date)
    {
        var d = date.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        return $"/odata/Criteria?$filter=effectiveFrom%20le%20{d}%20and%20(effectiveTo%20eq%20null%20or%20effectiveTo%20gt%20{d})&$orderby=name%20asc";
    }

    [Fact]
    public async Task Criteria_IntervalFilter_ReturnsCriteriaEffectiveOnAGivenDate()
    {
        var page = await OkAsync(OwnerA, EffectiveOn(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(["growth stage", "remote only"], Strings(page, "name"));
    }

    [Fact]
    public async Task Criteria_IntervalFilter_TreatsEffectiveToAsExclusive_LikeTheDomain()
    {
        // "growth stage" ends exactly at EndsAt: one second earlier it is effective, at EndsAt it is not.
        var endsAt = RemainingEntitiesSeed.Now.AddDays(40);
        var endsAtOffset = new DateTimeOffset(endsAt, TimeSpan.Zero);
        var growth = RoleRadar.Domain.Tracking.Criterion.Rehydrate(
            Guid.NewGuid(), OwnerA, "growth stage", RoleRadar.Domain.Tracking.CriterionCategory.Preference, "Series C or later",
            new DateTimeOffset(RemainingEntitiesSeed.Now.AddDays(-20), TimeSpan.Zero), endsAtOffset,
            RoleRadar.Domain.Tracking.ProvenanceKind.Stated, true, Guid.NewGuid(), endsAtOffset);

        var justBefore = endsAt.AddSeconds(-1);
        Assert.True(growth.IsEffectiveAt(new DateTimeOffset(justBefore, TimeSpan.Zero)));
        Assert.Equal(["growth stage", "remote only"], Strings(await OkAsync(OwnerA, EffectiveOn(justBefore)), "name"));

        Assert.False(growth.IsEffectiveAt(endsAtOffset));
        Assert.Equal(["remote only"], Strings(await OkAsync(OwnerA, EffectiveOn(endsAt)), "name"));
    }

    // ---------------------------------------------------------------- SearchRuns

    [Fact]
    public async Task SearchRuns_SortByRunDate_BothDirections_AndMapStoredFields()
    {
        var desc = await OkAsync(OwnerA, "/odata/SearchRuns?$orderby=runDate%20desc");
        Assert.Equal([G(302), G(301), G(303)], desc.GetProperty("value").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList());

        var asc = await OkAsync(OwnerA, "/odata/SearchRuns?$orderby=runDate%20asc");
        Assert.Equal([G(303), G(301), G(302)], asc.GetProperty("value").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList());

        var first = desc.GetProperty("value")[1];
        Assert.Equal(3, first.GetProperty("newPostings").GetInt32());
        Assert.Equal("weekly sweep", first.GetProperty("summary").GetString());

        var busy = await OkAsync(OwnerA, "/odata/SearchRuns?$filter=newPostings%20ge%205&$count=true");
        Assert.Equal(1, busy.GetProperty("@odata.count").GetInt32());
    }

    // ---------------------------------------------------------------- Cross-cutting

    [Theory]
    [InlineData("Companies")]
    [InlineData("Notes")]
    [InlineData("Criteria")]
    [InlineData("SearchRuns")]
    public async Task EachEntitySet_IsOwnerIsolated_SecondOwnerSeesOnlyOwnRows_AndThirdSeesNothing(string set)
    {
        var a = await OkAsync(OwnerA, $"/odata/{set}?$count=true");
        var b = await OkAsync(OwnerB, $"/odata/{set}?$count=true");
        var c = await OkAsync(OwnerC, $"/odata/{set}?$count=true");

        Assert.Equal(3, a.GetProperty("@odata.count").GetInt32());
        Assert.Equal(1, b.GetProperty("@odata.count").GetInt32());
        Assert.Equal(0, c.GetProperty("@odata.count").GetInt32());
        Assert.Empty(c.GetProperty("value").EnumerateArray());

        // Owner B never receives an owner A row, and vice versa.
        var aIds = a.GetProperty("value").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToHashSet();
        var bIds = b.GetProperty("value").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToHashSet();
        Assert.Empty(aIds.Intersect(bIds));
    }

    [Theory]
    [InlineData("companiesSearched")]
    [InlineData("postingsFound")]
    [InlineData("errorsCount")]
    public async Task SearchRuns_UnstoredCounters_AreNotPartOfTheReadModel(string counter)
    {
        // Not stored, so not exposed: absent from rows and rejected in $select, $filter and $orderby.
        var page = await OkAsync(OwnerA, "/odata/SearchRuns");
        foreach (var row in page.GetProperty("value").EnumerateArray())
        {
            Assert.False(row.TryGetProperty(counter, out _), row.ToString());
        }

        foreach (var query in new[] { $"$select={counter}", $"$filter={counter}%20eq%200", $"$orderby={counter}%20asc" })
        {
            var (status, _) = await _host.GetJsonAsync(OwnerA, $"/odata/SearchRuns?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }
    }

    [Theory]
    [InlineData("Companies", "$filter=bogus%20eq%201")]
    [InlineData("Companies", "$orderby=headquarters%20asc")]
    [InlineData("Notes", "$filter=ownerId%20eq%20'x'")]
    [InlineData("Notes", "$orderby=tags%20asc")]
    [InlineData("Criteria", "$filter=ruleText%20eq%20'x'%20and%20bogus%20eq%201")]
    [InlineData("Criteria", "$orderby=ruleText%20asc")]
    [InlineData("SearchRuns", "$filter=bogus%20gt%201")]
    [InlineData("SearchRuns", "$orderby=summary%20asc")]
    [InlineData("SearchRuns", "$orderby=companiesSearched%20asc")]
    public async Task UnknownOrNonAllowlistedProperties_Return400(string set, string query)
    {
        var (status, _) = await _host.GetJsonAsync(OwnerA, $"/odata/{set}?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Theory]
    [InlineData("Companies")]
    [InlineData("Notes")]
    [InlineData("Criteria")]
    [InlineData("SearchRuns")]
    public async Task EachEntitySet_RejectsWrites_With405_AndAllowGet(string set)
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            var req = new HttpRequestMessage(method, $"/odata/{set}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token(OwnerA));
            using var res = await _host.Client.SendAsync(req);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
            Assert.Contains("GET", res.Content.Headers.Allow);
        }
    }

    [Theory]
    [InlineData("Companies")]
    [InlineData("Notes")]
    [InlineData("Criteria")]
    [InlineData("SearchRuns")]
    public async Task EachEntitySet_RejectsTopAbove100_And_RequiresAuthentication(string set)
    {
        var (status, _) = await _host.GetJsonAsync(OwnerA, $"/odata/{set}?$top=101");
        Assert.Equal(HttpStatusCode.BadRequest, status);

        using var unauthenticated = await _host.Client.GetAsync($"/odata/{set}");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }
}
