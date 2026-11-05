namespace RoleRadar.Tests.Integration;

using System.Net;
using System.Text.Json;
using Xunit;

/// <summary>
/// Phase 6 (T061): two distinct owners (plus a third that owns nothing) against ALL six entity sets, through the
/// plain, $select, $count, $filter, $orderby and paging paths. No owner ever sees another owner's document, and
/// neither <c>owner_id</c> nor <c>ownerId</c> appears anywhere in any response body. The caller identity comes from
/// the test-only IOwnerIdProvider that reads the JWT subject (see <see cref="ODataTestHost"/>).
/// </summary>
public sealed class ODataMultiOwnerIsolationTests : IClassFixture<AllEntitiesFixture>
{
    private const string OwnerA = AllEntitiesFixture.OwnerA;
    private const string OwnerB = AllEntitiesFixture.OwnerB;
    private const string OwnerC = AllEntitiesFixture.OwnerC;

    private static Guid G(int n) => RemainingEntitiesSeed.G(n);

    private readonly ODataTestHost _host;

    public ODataMultiOwnerIsolationTests(AllEntitiesFixture fixture) => _host = fixture.Host;

    /// <summary>Per entity set: the first of A's three ids, B's id, a property to $select/$orderby, and a filter that matches only B's row.</summary>
    private static readonly Dictionary<string, (int FirstA, int BId, string Property, string BOnlyFilter)> Specs = new()
    {
        ["Postings"] = (401, 404, "title", "title eq 'Owner B Posting'"),
        ["Companies"] = (1, 4, "name", "name eq 'Omega Secret Co'"),
        ["Applications"] = (601, 604, "companyName", "companyName eq 'Omega Secret Co'"),
        ["Notes"] = (101, 104, "heading", "heading eq 'Owner B private note'"),
        ["Criteria"] = (201, 204, "name", "name eq 'owner b criterion'"),
        ["SearchRuns"] = (301, 304, "newPostings", "newPostings eq 9")
    };

    public static TheoryData<string> SetNames => new(Specs.Keys);

    private static readonly string[] ForbiddenKeys = ["owner_id", "ownerid"];

    private static HashSet<Guid> AIds(int firstA) => [G(firstA), G(firstA + 1), G(firstA + 2)];

    /// <summary>Fetches a page, checks the OData envelope and that no owner key exists anywhere in the raw body.</summary>
    private async Task<JsonElement> GetPageAsync(string owner, string set, string query = "")
    {
        var path = $"/odata/{set}" + (query.Length == 0 ? string.Empty : "?" + query);
        using var response = await _host.GetAsync(owner, path);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} as {owner} returned {(int)response.StatusCode}: {raw}");

        AssertNoOwnerKeys(raw, path);
        Assert.DoesNotContain(OwnerA, raw);
        Assert.DoesNotContain(OwnerB, raw);
        Assert.DoesNotContain(OwnerC, raw);

        var root = JsonDocument.Parse(raw).RootElement.Clone();
        Assert.False(root.TryGetProperty("items", out _), raw);
        return root;
    }

    private static void AssertNoOwnerKeys(string raw, string path)
    {
        using var doc = JsonDocument.Parse(raw);
        Walk(doc.RootElement);

        void Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Assert.False(
                            ForbiddenKeys.Contains(property.Name.ToLowerInvariant()),
                            $"'{property.Name}' leaked in {path}: {raw}");
                        Walk(property.Value);
                    }

                    break;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        Walk(item);
                    }

                    break;
            }
        }
    }

    private static List<Guid> Ids(JsonElement page) =>
        page.GetProperty("value").EnumerateArray().Select(e => e.GetProperty("id").GetGuid()).ToList();

    // ---------------------------------------------------------------- plain, count, owner views

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Each_Owner_Sees_Only_Their_Own_Documents(string set)
    {
        var (firstA, bId, _, _) = Specs[set];
        var a = await GetPageAsync(OwnerA, set, "$count=true");
        Assert.Equal(AIds(firstA), Ids(a).ToHashSet());
        Assert.Equal(3, a.GetProperty("@odata.count").GetInt32());

        var b = await GetPageAsync(OwnerB, set, "$count=true");
        Assert.Equal([G(bId)], Ids(b));
        Assert.Equal(1, b.GetProperty("@odata.count").GetInt32());

        Assert.Empty(Ids(a).Intersect(Ids(b)));
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task An_Owner_With_No_Data_Sees_Nothing_And_Counts_Zero(string set)
    {
        var c = await GetPageAsync(OwnerC, set, "$count=true");
        Assert.Empty(Ids(c));
        Assert.Equal(0, c.GetProperty("@odata.count").GetInt32());
    }

    // ---------------------------------------------------------------- $select, $filter, $orderby, paging

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Select_Is_Scoped_To_The_Caller(string set)
    {
        var (firstA, bId, property, _) = Specs[set];
        var a = await GetPageAsync(OwnerA, set, $"$select=id,{property}&$count=true");
        Assert.Equal(AIds(firstA), Ids(a).ToHashSet());
        Assert.Equal(3, a.GetProperty("@odata.count").GetInt32());

        var b = await GetPageAsync(OwnerB, set, $"$select=id,{property}");
        Assert.Equal([G(bId)], Ids(b));
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Filter_Cannot_Reach_Another_Owners_Document(string set)
    {
        var (firstA, bId, _, bOnlyFilter) = Specs[set];
        // The filter matches only B's document: for B it finds it, for A and C it finds nothing.
        var b = await GetPageAsync(OwnerB, set, $"$filter={Uri.EscapeDataString(bOnlyFilter)}&$count=true");
        Assert.Equal([G(bId)], Ids(b));

        foreach (var owner in new[] { OwnerA, OwnerC })
        {
            var other = await GetPageAsync(owner, set, $"$filter={Uri.EscapeDataString(bOnlyFilter)}&$count=true");
            Assert.Empty(Ids(other));
            Assert.Equal(0, other.GetProperty("@odata.count").GetInt32());
        }

        // Knowing the other owner's id does not help either.
        var byId = await GetPageAsync(OwnerA, set, $"$filter=id%20eq%20{G(bId)}&$count=true");
        Assert.Empty(Ids(byId));
        Assert.Equal(0, byId.GetProperty("@odata.count").GetInt32());

        var own = await GetPageAsync(OwnerA, set, $"$filter=id%20eq%20{G(firstA)}");
        Assert.Equal([G(firstA)], Ids(own));
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task OrderBy_Returns_Only_Own_Documents_In_Both_Directions(string set)
    {
        var (firstA, bId, property, _) = Specs[set];
        foreach (var direction in new[] { "asc", "desc" })
        {
            var a = await GetPageAsync(OwnerA, set, $"$orderby={property}%20{direction}");
            Assert.Equal(AIds(firstA), Ids(a).ToHashSet());
            Assert.DoesNotContain(G(bId), Ids(a));

            var b = await GetPageAsync(OwnerB, set, $"$orderby={property}%20{direction}");
            Assert.Equal([G(bId)], Ids(b));
        }
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Paging_Walks_Exactly_The_Callers_Documents(string set)
    {
        var (firstA, bId, property, _) = Specs[set];
        var seen = new List<Guid>();
        for (var skip = 0; skip < 6; skip++)
        {
            var page = await GetPageAsync(OwnerA, set, $"$orderby={property}%20asc&$top=1&$skip={skip}&$select=id,{property}");
            seen.AddRange(Ids(page));
        }

        Assert.Equal(3, seen.Count);
        Assert.Equal(AIds(firstA), seen.ToHashSet());
        Assert.DoesNotContain(G(bId), seen);

        // A page beyond the caller's data is empty, never a neighbour's document.
        var beyond = await GetPageAsync(OwnerB, set, "$top=100&$skip=1&$count=true");
        Assert.Empty(Ids(beyond));
        Assert.Equal(1, beyond.GetProperty("@odata.count").GetInt32());
    }

    // ---------------------------------------------------------------- the one place a body is opted into

    [Fact]
    public async Task Notes_Content_Of_Another_Owner_Is_Never_Returned()
    {
        var a = await GetPageAsync(OwnerA, "Notes", "$select=heading,content");
        var bodies = string.Join("|", a.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("content").GetString()));
        Assert.DoesNotContain("owner b body", bodies);

        var b = await GetPageAsync(OwnerB, "Notes", "$select=content");
        Assert.Equal("owner b body", b.GetProperty("value")[0].GetProperty("content").GetString());
    }
}
