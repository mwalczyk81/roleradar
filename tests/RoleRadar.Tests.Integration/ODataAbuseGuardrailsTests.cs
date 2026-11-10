namespace RoleRadar.Tests.Integration;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit;

/// <summary>
/// Phase 6 (T060): the query guardrails hold on all six entity sets, not only Postings. Every rejected query
/// is HTTP 400 with a validation problem document ({ status: 400, errorCode: "validation_error", detail }, feature 014),
/// and every write verb is HTTP 405 with Allow: GET.
/// </summary>
public sealed class ODataAbuseGuardrailsTests : IClassFixture<AllEntitiesFixture>
{
    private const string Owner = AllEntitiesFixture.OwnerA;

    private readonly ODataTestHost _host;

    public ODataAbuseGuardrailsTests(AllEntitiesFixture fixture) => _host = fixture.Host;

    /// <summary>A property that is filterable on each entity set, a matching literal, and whether it is a string.</summary>
    private static readonly Dictionary<string, (string Property, string Literal, bool IsString)> Specs = new()
    {
        ["Postings"] = ("title", "'x'", true),
        ["Companies"] = ("name", "'x'", true),
        ["Applications"] = ("companyName", "'x'", true),
        ["Notes"] = ("heading", "'x'", true),
        ["Criteria"] = ("name", "'x'", true),
        ["SearchRuns"] = ("newPostings", "1", false)
    };

    public static TheoryData<string> SetNames => new(Specs.Keys);

    public static TheoryData<string> StringSetNames => new(Specs.Where(kv => kv.Value.IsString).Select(kv => kv.Key));

    private async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string set, string query)
    {
        var (status, body) = await _host.GetJsonAsync(Owner, $"/odata/{set}?{query}");
        return (status, body.RootElement.Clone());
    }

    private async Task AssertRejectedAsync(string set, string query)
    {
        var (status, body) = await GetAsync(set, query);
        Assert.True(status == HttpStatusCode.BadRequest, $"/odata/{set}?{query} returned {(int)status}: {body}");
        Assert.Equal(400, body.GetProperty("status").GetInt32());
        Assert.Equal("validation_error", body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()), body.ToString());
    }

    private static string Q(string filter) => "$filter=" + Uri.EscapeDataString(filter);

    // ---------------------------------------------------------------- harness sanity

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task A_Valid_Filter_On_The_Same_Property_Is_Accepted(string set)
    {
        var (property, literal, _) = Specs[set];
        var (status, body) = await GetAsync(set, Q($"{property} eq {literal}"));
        Assert.True(status == HttpStatusCode.OK, body.ToString());
    }

    // ---------------------------------------------------------------- size and shape of the expression

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Expression_Over_100_Nodes_Is_Rejected(string set)
    {
        var (property, literal, _) = Specs[set];
        var filter = string.Join(" or ", Enumerable.Range(0, 110).Select(_ => $"{property} eq {literal}"));
        await AssertRejectedAsync(set, Q(filter));
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Expression_Deeper_Than_10_Is_Rejected(string set)
    {
        var (property, literal, _) = Specs[set];
        // 14 nested levels, far fewer than 100 nodes: only the depth rule can reject it.
        var filter = $"{property} eq {literal}";
        for (var i = 0; i < 14; i++)
        {
            filter = $"({property} eq {literal} and ({filter}))";
        }

        await AssertRejectedAsync(set, Q(filter));
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Arithmetic_Operators_Are_Rejected(string set)
    {
        foreach (var op in new[] { "add", "sub", "mul", "div", "mod" })
        {
            await AssertRejectedAsync(set, Q($"1 {op} 1 eq 2"));
        }
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Disallowed_Functions_Are_Rejected(string set)
    {
        var (property, _, isString) = Specs[set];
        var filters = isString
            ? new[]
            {
                $"length({property}) eq 5",
                $"substring({property},1) eq 'x'",
                $"concat({property},'a') eq 'xa'",
                $"indexof({property},'x') eq 0",
                $"trim({property}) eq 'x'",
                $"toupper({property}) eq 'X'"
            }
            : new[] { $"round({property}) eq 1", $"floor({property}) eq 1", $"ceiling({property}) eq 1" };

        foreach (var filter in filters)
        {
            await AssertRejectedAsync(set, Q(filter));
        }

    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Lambda_Any_And_All_Are_Rejected(string set)
    {
        var (property, literal, _) = Specs[set];
        await AssertRejectedAsync(set, Q($"{property}/any()"));
        await AssertRejectedAsync(set, Q($"{property}/any(t:t eq {literal})"));
        await AssertRejectedAsync(set, Q($"{property}/all(t:t eq {literal})"));
    }

    [Theory]
    [InlineData("Companies", "categoryTags")]
    [InlineData("Notes", "tags")]
    public async Task Lambda_On_A_Real_Collection_Property_Is_Rejected(string set, string collection)
    {
        await AssertRejectedAsync(set, Q($"{collection}/any(t:t eq 'x')"));
        await AssertRejectedAsync(set, Q($"{collection}/all(t:t eq 'x')"));
    }

    // ---------------------------------------------------------------- paging

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Top_Over_100_Is_Rejected_But_100_Is_Accepted(string set)
    {
        await AssertRejectedAsync(set, "$top=101");
        await AssertRejectedAsync(set, "$top=1000000");
        await AssertRejectedAsync(set, "$skip=-1");

        var (status, body) = await GetAsync(set, "$top=100");
        Assert.True(status == HttpStatusCode.OK, body.ToString());
    }

    // ---------------------------------------------------------------- unknown properties and disallowed options

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Unknown_Properties_Are_Rejected_In_Filter_OrderBy_And_Select(string set)
    {
        await AssertRejectedAsync(set, Q("noSuchProperty eq 'x'"));
        await AssertRejectedAsync(set, "$orderby=noSuchProperty%20asc");
        await AssertRejectedAsync(set, "$select=noSuchProperty");
        await AssertRejectedAsync(set, "$select=id,noSuchProperty");
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Owner_Id_Cannot_Be_Filtered_Sorted_Or_Selected(string set)
    {
        foreach (var name in new[] { "ownerId", "owner_id" })
        {
            await AssertRejectedAsync(set, Q($"{name} eq 'x'"));
            await AssertRejectedAsync(set, $"$orderby={name}%20asc");
            await AssertRejectedAsync(set, $"$select={name}");
        }
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Expand_And_Apply_Are_Rejected(string set)
    {
        await AssertRejectedAsync(set, "$expand=anything");
        await AssertRejectedAsync(set, "$apply=" + Uri.EscapeDataString("groupby((id))"));
        await AssertRejectedAsync(set, "$apply=" + Uri.EscapeDataString("aggregate($count as total)"));
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Orderby_Functions_And_Too_Many_Sort_Keys_Are_Rejected(string set)
    {
        await AssertRejectedAsync(set, "$orderby=" + Uri.EscapeDataString("tolower(id)"));
        await AssertRejectedAsync(set, "$orderby=id,id,id,id,id,id");
    }

    // ---------------------------------------------------------------- string searches must be lowercase and wrapped

    [Theory]
    [MemberData(nameof(StringSetNames))]
    public async Task String_Search_Terms_Must_Be_Lowercase(string set)
    {
        var property = Specs[set].Property;
        foreach (var fn in new[] { "contains", "startswith", "endswith" })
        {
            await AssertRejectedAsync(set, Q($"{fn}(tolower({property}),'ACME')"));
            await AssertRejectedAsync(set, Q($"{fn}(tolower({property}),'Acme')"));

            var (status, body) = await GetAsync(set, Q($"{fn}(tolower({property}),'acme')"));
            Assert.True(status == HttpStatusCode.OK, $"{fn} with a lowercase term must pass: {body}");
        }
    }

    [Theory]
    [MemberData(nameof(StringSetNames))]
    public async Task String_Search_Must_Wrap_The_Property_In_Tolower(string set)
    {
        var property = Specs[set].Property;
        foreach (var fn in new[] { "contains", "startswith", "endswith" })
        {
            await AssertRejectedAsync(set, Q($"{fn}({property},'acme')"));
        }
    }

    // ---------------------------------------------------------------- notes content

    [Fact]
    public async Task Notes_Content_Cannot_Be_Filtered_Or_Sorted()
    {
        await AssertRejectedAsync("Notes", Q("contains(tolower(content),'secret')"));
        await AssertRejectedAsync("Notes", Q("content eq 'secret body one'"));
        await AssertRejectedAsync("Notes", Q("Content eq 'secret body one'"));
        await AssertRejectedAsync("Notes", Q("heading eq 'x' or content ne null"));
        await AssertRejectedAsync("Notes", "$orderby=content%20asc");
        await AssertRejectedAsync("Notes", "$orderby=content%20desc");
        await AssertRejectedAsync("Notes", "$orderby=heading,content");
    }

    // ---------------------------------------------------------------- write verbs

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Write_Verbs_Return_405_With_Allow_Get(string set)
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, $"/odata/{set}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token(Owner));
            using var response = await _host.Client.SendAsync(request);

            Assert.True(response.StatusCode == HttpStatusCode.MethodNotAllowed, $"{method} /odata/{set} returned {(int)response.StatusCode}");
            Assert.Contains("GET", response.Content.Headers.Allow);
        }
    }

    [Theory]
    [MemberData(nameof(SetNames))]
    public async Task Writes_To_A_Single_Entity_Path_Are_Not_Routable(string set)
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, $"/odata/{set}({Guid.NewGuid()})");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _host.Token(Owner));
            using var response = await _host.Client.SendAsync(request);

            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method} /odata/{set}(id) returned {(int)response.StatusCode}");
        }
    }
}
