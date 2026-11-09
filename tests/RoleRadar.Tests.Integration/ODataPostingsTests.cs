namespace RoleRadar.Tests.Integration;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Api;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ODataPostingsTestsFixture : IAsyncLifetime
{
    private const string TestSecret = "this-is-a-very-long-test-secret-key-for-odata-postings-32-bytes!";
    private const string TestIssuer = "https://test-identity.local";
    private const string TestAudience = "roleradar.api";
    private const string OwnerSub = "postings-owner-1234";

    private readonly MongoDbContainer _container = new MongoDbBuilder(MongoTestImage.Tag)
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private Microsoft.AspNetCore.Builder.WebApplication _apiApp = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        await MongoContainerStartGate.StartAsync(_container);

        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase("odata_postings_test_db");
        await SeedPostingsAsync();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));

        _apiApp = Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _container.GetConnectionString(),
                    ["DatabaseName"] = "odata_postings_test_db",
                    ["Authentication:Authority"] = TestIssuer,
                    ["Authentication:Audience"] = TestAudience,
                    ["Owner:AllowedSubjects:0"] = OwnerSub
                });

                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = string.Empty;
                    options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
                        {
                            Issuer = TestIssuer,
                            SigningKeys = { key }
                        });
                    options.RequireHttpsMetadata = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = TestIssuer,
                        ValidateAudience = true,
                        ValidAudience = TestAudience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ValidateLifetime = false
                    };
                });
            });

        await _apiApp.StartAsync();

        var server = _apiApp.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };

        var token = GenerateJwt(OwnerSub, "roleradar.api");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _apiApp.StopAsync();
        await _apiApp.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static string GenerateJwt(string sub, string scope)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, sub),
                new Claim("scope", scope)
            ]),
            Issuer = TestIssuer,
            Audience = TestAudience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    private async Task SeedPostingsAsync()
    {
        var collection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);

        var postingsData = new[]
        {
            ("unscored", 0, (int?)null, false, "Aardvark Specialist", "Beta Ltd"),
            ("lead", 1, (int?)50, true, "apple Developer", "Alpha Inc"),
            ("scored", 2, (int?)80, true, "C++ Systems Engineer", "Delta Co"),
            ("applied", 3, (int?)null, false, "(Senior) Architect", "Acme Corp"),
            ("rejected", 4, (int?)60, true, "Software Engineer (rejected)", "Gamma LLC"),
            ("screened_out", 5, (int?)70, true, "Software Engineer (screened_out)", "Epsilon Inc"),
            ("closed", 6, (int?)90, true, "Zebra Wrangler", "Zeta Systems")
        };

        var docs = new List<BsonDocument>();
        foreach (var (disp, rank, score, hasScore, title, compName) in postingsData)
        {
            docs.Add(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = RoleRadar.Domain.Common.OwnerId.DefaultValue,
                ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["company_name"] = compName,
                ["title"] = title,
                ["requisition_id"] = $"REQ-{rank}",
                ["location"] = "Remote",
                ["disposition"] = disp,
                ["disposition_rank"] = rank,
                ["resurface_policy"] = "never",
                ["score"] = score.HasValue ? score.Value : BsonNull.Value,
                ["has_score"] = hasScore,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow.AddMinutes(-rank)),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });
        }

        await collection.InsertManyAsync(docs);
    }

    public HttpClient Client => _client;
}

/// <summary>Read-only tests share one container and API host (see <see cref="ODataPostingsTestsFixture"/>); each test is independent of the others.</summary>
public sealed class ODataPostingsTests : IClassFixture<ODataPostingsTestsFixture>
{
    private readonly HttpClient _client;

    public ODataPostingsTests(ODataPostingsTestsFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task Post_Method_Returns_405_With_Allow_Get()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/odata/Postings")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotNull(response.Content.Headers.Allow);
        Assert.Contains("GET", response.Content.Headers.Allow);
    }

    [Fact]
    public async Task OrderBy_Disposition_Asc_And_Desc_Sorts_By_Pipeline_Rank()
    {
        // Ascending
        var ascResp = await _client.GetAsync("/odata/Postings?$orderby=disposition asc");
        Assert.Equal(HttpStatusCode.OK, ascResp.StatusCode);
        var ascRaw = await ascResp.Content.ReadAsStringAsync();
        var ascJson = JsonSerializer.Deserialize<JsonElement>(ascRaw);
        Assert.True(ascJson.TryGetProperty("value", out var ascValueProp), $"Property 'value' not found: {ascRaw}");
        var ascItems = ascValueProp.EnumerateArray().ToList();
        Assert.NotEmpty(ascItems);
        Assert.True(ascItems[0].TryGetProperty("disposition", out _), $"Property 'disposition' not found on item: {ascItems[0].GetRawText()}");
        Assert.Equal(7, ascItems.Count);
        Assert.Equal("unscored", ascItems[0].GetProperty("disposition").GetString());
        Assert.Equal("closed", ascItems[^1].GetProperty("disposition").GetString());

        // Descending
        var descResp = await _client.GetAsync("/odata/Postings?$orderby=disposition desc");
        Assert.Equal(HttpStatusCode.OK, descResp.StatusCode);
        var descJson = await descResp.Content.ReadFromJsonAsync<JsonElement>();
        var descItems = descJson.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal(7, descItems.Count);
        Assert.Equal("closed", descItems[0].GetProperty("disposition").GetString());
        Assert.Equal("unscored", descItems[^1].GetProperty("disposition").GetString());
    }

    [Fact]
    public async Task OrderBy_Score_Sorts_Nulls_Last_In_Both_Directions()
    {
        // Ascending
        var ascResp = await _client.GetAsync("/odata/Postings?$orderby=score asc");
        Assert.Equal(HttpStatusCode.OK, ascResp.StatusCode);
        var ascJson = await ascResp.Content.ReadFromJsonAsync<JsonElement>();
        var ascItems = ascJson.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal(7, ascItems.Count);

        // Nulls must be at the end in ASC
        Assert.Equal(JsonValueKind.Null, ascItems[^1].GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, ascItems[^2].GetProperty("score").ValueKind);
        Assert.Equal(50, ascItems[0].GetProperty("score").GetInt32());

        // Descending
        var descResp = await _client.GetAsync("/odata/Postings?$orderby=score desc");
        Assert.Equal(HttpStatusCode.OK, descResp.StatusCode);
        var descJson = await descResp.Content.ReadFromJsonAsync<JsonElement>();
        var descItems = descJson.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal(7, descItems.Count);

        // Nulls must be at the end in DESC
        Assert.Equal(JsonValueKind.Null, descItems[^1].GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, descItems[^2].GetProperty("score").ValueKind);
        Assert.Equal(90, descItems[0].GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task String_Filter_Without_ToLower_Returns_400_Problem_Details()
    {
        // Rejects contains(Title, 'Software')
        var resp1 = await _client.GetAsync("/odata/Postings?$filter=contains(title,'Software')");
        Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);

        // Rejects contains(tolower(Title), 'Software') because search term is not lowercased
        var resp2 = await _client.GetAsync("/odata/Postings?$filter=contains(tolower(title),'Software')");
        Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);

        // Accepts contains(tolower(Title), 'software')
        var resp3 = await _client.GetAsync("/odata/Postings?$filter=contains(tolower(title),'software')");
        Assert.Equal(HttpStatusCode.OK, resp3.StatusCode);
    }

    [Fact]
    public async Task Pagination_Continuity_Produces_Zero_Repeated_Or_Skipped_Rows()
    {
        var page1Resp = await _client.GetAsync("/odata/Postings?$top=3&$skip=0&$orderby=disposition asc");
        Assert.Equal(HttpStatusCode.OK, page1Resp.StatusCode);
        var page1Json = await page1Resp.Content.ReadFromJsonAsync<JsonElement>();
        var page1Ids = page1Json.GetProperty("value").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();

        var page2Resp = await _client.GetAsync("/odata/Postings?$top=3&$skip=3&$orderby=disposition asc");
        Assert.Equal(HttpStatusCode.OK, page2Resp.StatusCode);
        var page2Json = await page2Resp.Content.ReadFromJsonAsync<JsonElement>();
        var page2Ids = page2Json.GetProperty("value").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();

        Assert.Equal(3, page1Ids.Count);
        Assert.Equal(3, page2Ids.Count);
        Assert.Empty(page1Ids.Intersect(page2Ids));
    }

    [Fact]
    public async Task Select_Projection_Returns_Only_Selected_Fields()
    {
        var response = await _client.GetAsync("/odata/Postings?$select=title,companyName");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("items", out _), "responses use the OData 'value' envelope only");
        var items = json.GetProperty("value").EnumerateArray().ToList();
        Assert.NotEmpty(items);

        var first = items[0];
        Assert.True(first.TryGetProperty("title", out _));
        Assert.True(first.TryGetProperty("companyName", out _));
        Assert.False(first.TryGetProperty("location", out _));
        Assert.False(first.TryGetProperty("disposition", out _));
        Assert.False(first.TryGetProperty("score", out _));
    }

    [Fact]
    public async Task Count_Option_Returns_OData_Count()
    {
        var response = await _client.GetAsync("/odata/Postings?$count=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("@odata.count", out var countProp));
        Assert.Equal(7, countProp.GetInt32());
    }

    [Fact]
    public async Task Case_Insensitive_Title_Sort_Interleaves_Casing()
    {
        // "Aardvark Specialist", "apple Developer", "Zebra Wrangler"
        // In case-insensitive collation, "apple" must sort between "Aardvark" and "Zebra"
        var response = await _client.GetAsync("/odata/Postings?$orderby=title asc&$filter=title eq 'Aardvark Specialist' or title eq 'apple Developer' or title eq 'Zebra Wrangler'");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var titles = json.GetProperty("value").EnumerateArray()
            .Select(x => x.GetProperty("title").GetString()!)
            .ToList();

        Assert.Equal(3, titles.Count);
        Assert.Equal("Aardvark Specialist", titles[0]);
        Assert.Equal("apple Developer", titles[1]);
        Assert.Equal("Zebra Wrangler", titles[2]);
    }

    [Fact]
    public async Task CompanyName_Sort_Ascending_And_Descending()
    {
        // Companies: Acme Corp, Alpha Inc, Beta Ltd, Delta Co, Epsilon Inc, Gamma LLC, Zeta Systems
        var ascResp = await _client.GetAsync("/odata/Postings?$orderby=companyName asc");
        Assert.Equal(HttpStatusCode.OK, ascResp.StatusCode);
        var ascJson = await ascResp.Content.ReadFromJsonAsync<JsonElement>();
        var ascCompanies = ascJson.GetProperty("value").EnumerateArray()
            .Select(x => x.GetProperty("companyName").GetString()!)
            .ToList();

        Assert.Equal(7, ascCompanies.Count);
        Assert.Equal("Acme Corp", ascCompanies[0]);
        Assert.Equal("Zeta Systems", ascCompanies[^1]);

        var descResp = await _client.GetAsync("/odata/Postings?$orderby=companyName desc");
        Assert.Equal(HttpStatusCode.OK, descResp.StatusCode);
        var descJson = await descResp.Content.ReadFromJsonAsync<JsonElement>();
        var descCompanies = descJson.GetProperty("value").EnumerateArray()
            .Select(x => x.GetProperty("companyName").GetString()!)
            .ToList();

        Assert.Equal(7, descCompanies.Count);
        Assert.Equal("Zeta Systems", descCompanies[0]);
        Assert.Equal("Acme Corp", descCompanies[^1]);
    }

    [Fact]
    public async Task Search_With_Special_Characters_Over_Http()
    {
        // 1. Search for 'c++'
        var respCpp = await _client.GetAsync("/odata/Postings?$filter=contains(tolower(title),'c%2B%2B')");
        Assert.Equal(HttpStatusCode.OK, respCpp.StatusCode);
        var jsonCpp = await respCpp.Content.ReadFromJsonAsync<JsonElement>();
        var itemsCpp = jsonCpp.GetProperty("value").EnumerateArray().ToList();
        Assert.Single(itemsCpp);
        Assert.Equal("C++ Systems Engineer", itemsCpp[0].GetProperty("title").GetString());

        // 2. Search for '(senior'
        var respSenior = await _client.GetAsync("/odata/Postings?$filter=contains(tolower(title),'(senior')");
        Assert.Equal(HttpStatusCode.OK, respSenior.StatusCode);
        var jsonSenior = await respSenior.Content.ReadFromJsonAsync<JsonElement>();
        var itemsSenior = jsonSenior.GetProperty("value").EnumerateArray().ToList();
        Assert.Single(itemsSenior);
        Assert.Equal("(Senior) Architect", itemsSenior[0].GetProperty("title").GetString());
    }
}
