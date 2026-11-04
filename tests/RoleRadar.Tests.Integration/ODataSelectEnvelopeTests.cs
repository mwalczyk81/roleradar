namespace RoleRadar.Tests.Integration;

using System.Net;
using System.Text.Json;
using Xunit;

/// <summary>
/// Phase 6 (A): every OData response, with or without $select, is the standard envelope
/// { "value": [...], "@odata.count": n } with no "items" key. One pipeline serves all six entity sets.
/// </summary>
public sealed class ODataSelectEnvelopeTests : IClassFixture<AllEntitiesFixture>
{
    private readonly ODataTestHost _host;

    public ODataSelectEnvelopeTests(AllEntitiesFixture fixture) => _host = fixture.Host;

    // entity set, a selectable property that exists on the read model, rows owner A has
    public static TheoryData<string, string, int> EntitySets => new()
    {
        { "Postings", "title", 3 },
        { "Companies", "name", 3 },
        { "Applications", "companyName", 3 },
        { "Notes", "heading", 3 },
        { "Criteria", "name", 3 },
        { "SearchRuns", "newPostings", 3 }
    };

    private async Task<JsonElement> OkAsync(string path)
    {
        var (status, body) = await _host.GetJsonAsync(AllEntitiesFixture.OwnerA, path);
        Assert.True(status == HttpStatusCode.OK, $"{path} returned {(int)status}: {body.RootElement}");
        return body.RootElement.Clone();
    }

    [Theory]
    [MemberData(nameof(EntitySets))]
    public async Task Plain_Response_Uses_Value_Envelope_With_No_Items_Key(string set, string property, int rows)
    {
        var page = await OkAsync($"/odata/{set}");
        Assert.False(page.TryGetProperty("items", out _), page.ToString());
        Assert.Equal(rows, page.GetProperty("value").GetArrayLength());
        Assert.False(page.TryGetProperty("@odata.count", out _));
        Assert.NotEmpty(property);
    }

    [Theory]
    [MemberData(nameof(EntitySets))]
    public async Task Select_Response_Uses_Value_Envelope_With_No_Items_Key(string set, string property, int rows)
    {
        var page = await OkAsync($"/odata/{set}?$select={property}");
        Assert.False(page.TryGetProperty("items", out _), page.ToString());
        var value = page.GetProperty("value");
        Assert.Equal(rows, value.GetArrayLength());
        foreach (var row in value.EnumerateArray())
        {
            Assert.True(row.TryGetProperty(property, out _), row.ToString());
            Assert.Single(row.EnumerateObject(), p => !p.Name.StartsWith('@'));
        }

        Assert.False(page.TryGetProperty("@odata.count", out _), "count only appears when $count=true");
    }

    [Theory]
    [MemberData(nameof(EntitySets))]
    public async Task Select_With_Count_Returns_OData_Count_Alongside_Value(string set, string property, int rows)
    {
        var page = await OkAsync($"/odata/{set}?$select={property}&$count=true&$top=1");
        Assert.False(page.TryGetProperty("items", out _), page.ToString());
        Assert.Equal(1, page.GetProperty("value").GetArrayLength());
        Assert.Equal(rows, page.GetProperty("@odata.count").GetInt32());
    }

    [Theory]
    [MemberData(nameof(EntitySets))]
    public async Task Select_Composes_With_Filter_Orderby_And_Paging(string set, string property, int rows)
    {
        var page = await OkAsync($"/odata/{set}?$select={property}&$orderby={property}%20desc&$skip=1&$top=1&$count=true");
        Assert.False(page.TryGetProperty("items", out _), page.ToString());
        Assert.Equal(1, page.GetProperty("value").GetArrayLength());
        Assert.Equal(rows, page.GetProperty("@odata.count").GetInt32());
    }

    [Fact]
    public async Task Notes_Select_Heading_Never_Returns_Content()
    {
        var page = await OkAsync("/odata/Notes?$select=heading");
        Assert.False(page.TryGetProperty("items", out _));
        foreach (var row in page.GetProperty("value").EnumerateArray())
        {
            Assert.True(row.TryGetProperty("heading", out _));
            Assert.False(row.TryGetProperty("content", out _), row.ToString());
            Assert.DoesNotContain("secret body", row.ToString());
        }
    }

    [Fact]
    public async Task Notes_Select_Content_Returns_Content_Only_When_Requested()
    {
        var page = await OkAsync("/odata/Notes?$select=heading,content&$orderby=heading%20asc");
        Assert.False(page.TryGetProperty("items", out _));
        var rowsWithBody = page.GetProperty("value").EnumerateArray().Select(r => r.GetProperty("content").GetString()).ToList();
        Assert.Equal(3, rowsWithBody.Count);
        Assert.All(rowsWithBody, body => Assert.StartsWith("secret body", body));

        var plain = await OkAsync("/odata/Notes");
        foreach (var row in plain.GetProperty("value").EnumerateArray())
        {
            Assert.True(!row.TryGetProperty("content", out var c) || c.ValueKind == JsonValueKind.Null, row.ToString());
        }
    }

    [Fact]
    public async Task Postings_Select_Never_Exposes_Other_Fields_Or_Owner()
    {
        var page = await OkAsync("/odata/Postings?$select=title,companyName");
        foreach (var row in page.GetProperty("value").EnumerateArray())
        {
            Assert.False(row.TryGetProperty("ownerId", out _));
            Assert.False(row.TryGetProperty("owner_id", out _));
            Assert.False(row.TryGetProperty("disposition", out _));
        }
    }
}
