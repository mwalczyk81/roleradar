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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Api;
using RoleRadar.Api.Authentication;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ODataApplicationsTests : IAsyncLifetime
{
    private const string TestSecret = "this-is-a-very-long-test-secret-key-for-odata-apps-32-bytes!";
    private const string TestIssuer = "https://test-identity.local";
    private const string TestAudience = "roleradar.api";
    private const string OwnerA = "apps-owner-aaa";
    private const string OwnerB = "apps-owner-bbb";
    private const string OwnerC = "apps-owner-ccc"; // owns only the legacy document used by the reconcile test

    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.34")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private Microsoft.AspNetCore.Builder.WebApplication _apiApp = null!;
    private HttpClient _client = null!;
    private IMongoDatabase _database = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase("odata_apps_test_db");
        await SeedApplicationsAsync();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));

        _apiApp = Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _container.GetConnectionString(),
                    ["DatabaseName"] = "odata_apps_test_db",
                    ["Authentication:Authority"] = TestIssuer,
                    ["Authentication:Audience"] = TestAudience,
                    ["Owner:AllowedSubjects:0"] = OwnerA,
                    ["Owner:AllowedSubjects:1"] = OwnerB,
                    ["Owner:AllowedSubjects:2"] = OwnerC
                });

                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.Authority = string.Empty;
                    opts.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
                        {
                            Issuer = TestIssuer
                        });
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = TestIssuer,
                        ValidateAudience = true,
                        ValidAudience = TestAudience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ValidateLifetime = true
                    };
                });

                builder.Services.Replace(ServiceDescriptor.Scoped<IOwnerIdProvider, TestSubjectOwnerIdProvider>());
            });

        await _apiApp.StartAsync();

        var server = _apiApp.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var baseAddress = addresses?.First() ?? throw new InvalidOperationException("No server address found.");

        _client = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _apiApp.StopAsync();
        await _apiApp.DisposeAsync();
        await _container.DisposeAsync();
    }

    private string GenerateJwtToken(string subject = OwnerA)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, subject),
            new Claim("scope", "roleradar.api")
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task SeedApplicationsAsync()
    {
        var apps = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);

        // App 1: Owner A, responded early
        await apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.Parse("11111111-1111-1111-1111-111111111111"), GuidRepresentation.Standard),
            ["owner_id"] = OwnerA,
            ["posting_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["posting_title"] = "Senior Backend Engineer",
            ["company_name"] = "Acme Corp",
            ["status"] = "applied",
            ["applied_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-10)),
            ["has_interview"] = true,
            ["first_response_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-8)),
            ["last_event_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-2)),
            ["has_first_response"] = true,
            ["has_last_event"] = true
        });

        // App 2: Owner A, responded recently
        await apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.Parse("22222222-2222-2222-2222-222222222222"), GuidRepresentation.Standard),
            ["owner_id"] = OwnerA,
            ["posting_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["posting_title"] = "Cloud Architect",
            ["company_name"] = "Beta Systems",
            ["status"] = "applied",
            ["applied_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-20)),
            ["has_interview"] = false,
            ["first_response_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-4)),
            ["last_event_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-4)),
            ["has_first_response"] = true,
            ["has_last_event"] = true
        });

        // App 3: Owner A, no response (null first_response_at)
        await apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.Parse("33333333-3333-3333-3333-333333333333"), GuidRepresentation.Standard),
            ["owner_id"] = OwnerA,
            ["posting_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["posting_title"] = "apple software dev", // test case-insensitive sort
            ["company_name"] = "Zeta Labs",
            ["status"] = "applied",
            ["applied_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-5)),
            ["has_interview"] = false,
            ["first_response_at"] = BsonNull.Value,
            ["last_event_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-5)),
            ["has_first_response"] = false,
            ["has_last_event"] = true
        });

        // App 4: Owner B (Isolation)
        await apps.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.Parse("44444444-4444-4444-4444-444444444444"), GuidRepresentation.Standard),
            ["owner_id"] = OwnerB,
            ["posting_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["posting_title"] = "Secret Owner B Application",
            ["company_name"] = "Omega Corp",
            ["status"] = "applied",
            ["applied_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-1)),
            ["has_interview"] = false,
            ["first_response_at"] = BsonNull.Value,
            ["last_event_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-1)),
            ["has_first_response"] = false,
            ["has_last_event"] = true
        });
    }

    [Fact]
    public async Task GetApplications_ReturnsPreMaterializedMilestones_And_CalculatedAgeInDays()
    {
        var token = GenerateJwtToken(OwnerA);
        var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Applications");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var value = doc.RootElement.GetProperty("value");
        Assert.Equal(3, value.GetArrayLength());

        var app1 = value.EnumerateArray().First(x => x.GetProperty("id").GetString() == "11111111-1111-1111-1111-111111111111");
        Assert.Equal("Senior Backend Engineer", app1.GetProperty("postingTitle").GetString());
        Assert.Equal("Acme Corp", app1.GetProperty("companyName").GetString());
        Assert.True(app1.GetProperty("hasInterview").GetBoolean());
        Assert.NotNull(app1.GetProperty("firstResponseAt").GetString());
        Assert.NotNull(app1.GetProperty("lastEventAt").GetString());
        Assert.True(app1.GetProperty("ageInDays").GetInt32() >= 9);

        // Verify owner_id is strictly omitted from response
        Assert.False(app1.TryGetProperty("owner_id", out _));
        Assert.False(app1.TryGetProperty("ownerId", out _));
    }

    [Fact]
    public async Task GetApplications_NullsLast_Sorting_BothDirections_ForFirstResponseAt()
    {
        var token = GenerateJwtToken(OwnerA);

        // 1. Ascending sort: App 1 (day -8), App 2 (day -4), then App 3 (null)
        var ascReq = new HttpRequestMessage(HttpMethod.Get, "/odata/Applications?$orderby=firstResponseAt%20asc");
        ascReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var ascRes = await _client.SendAsync(ascReq);
        Assert.Equal(HttpStatusCode.OK, ascRes.StatusCode);

        using var ascDoc = await JsonDocument.ParseAsync(await ascRes.Content.ReadAsStreamAsync());
        var ascItems = ascDoc.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal("11111111-1111-1111-1111-111111111111", ascItems[0].GetProperty("id").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", ascItems[1].GetProperty("id").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333333", ascItems[2].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, ascItems[2].GetProperty("firstResponseAt").ValueKind);

        // 2. Descending sort: App 2 (day -4), App 1 (day -8), then App 3 (null) LAST
        var descReq = new HttpRequestMessage(HttpMethod.Get, "/odata/Applications?$orderby=firstResponseAt%20desc");
        descReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var descRes = await _client.SendAsync(descReq);
        Assert.Equal(HttpStatusCode.OK, descRes.StatusCode);

        using var descDoc = await JsonDocument.ParseAsync(await descRes.Content.ReadAsStreamAsync());
        var descItems = descDoc.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.Equal("22222222-2222-2222-2222-222222222222", descItems[0].GetProperty("id").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", descItems[1].GetProperty("id").GetString());
        Assert.Equal("33333333-3333-3333-3333-333333333333", descItems[2].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, descItems[2].GetProperty("firstResponseAt").ValueKind);
    }

    [Fact]
    public async Task GetApplications_MultiOwnerIsolation_ReturnsOnlyCallerRecords()
    {
        var tokenB = GenerateJwtToken(OwnerB);
        var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Applications");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var value = doc.RootElement.GetProperty("value");
        Assert.Equal(1, value.GetArrayLength());
        Assert.Equal("44444444-4444-4444-4444-444444444444", value[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task NonGetMethods_Return405MethodNotAllowed_WithAllowGetHeader()
    {
        var token = GenerateJwtToken(OwnerA);

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
        {
            var req = new HttpRequestMessage(method, "/odata/Applications");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var res = await _client.SendAsync(req);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, res.StatusCode);
            Assert.NotNull(res.Content.Headers.Allow);
            Assert.Contains("GET", res.Content.Headers.Allow);
        }
    }

    [Fact]
    public async Task LegacyDocument_AfterReconcile_ExposesStatusAndAppliedAt_AndMatchesStatusFilter()
    {
        // A real pre-Phase-4 document only carries outcome / submitted_date / posting_id.
        // Without the reconcile backfill, status is null and applied_at is the 0001 default.
        var companyId = Guid.NewGuid();
        var postingId = Guid.NewGuid();
        var legacyId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        await _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["owner_id"] = OwnerC,
            ["name"] = "Legacy Holdings"
        });
        await _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
            ["owner_id"] = OwnerC,
            ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["title"] = "Legacy Platform Engineer"
        });
        await _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(legacyId, GuidRepresentation.Standard),
            ["owner_id"] = OwnerC,
            ["posting_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
            ["submitted_date"] = "2026-10-01",
            ["outcome"] = "applied",
            ["created_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-30)),
            ["updated_at"] = BsonDateTime.Create(DateTime.UtcNow.AddDays(-30))
        });

        var report = await new RoleRadar.Infrastructure.Mongo.Reconcile.ReadModelReconciliationService(_database)
            .ReconcileApplicationsAsync(dryRun: false);
        Assert.False(report.IsDryRun);
        Assert.True(report.ApplicationsUpdated >= 1);

        var token = GenerateJwtToken(OwnerC);
        var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Applications?$filter=status%20eq%20'applied'");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var value = doc.RootElement.GetProperty("value");
        Assert.Equal(1, value.GetArrayLength());

        var row = value[0];
        Assert.Equal(legacyId.ToString(), row.GetProperty("id").GetString());
        Assert.Equal("applied", row.GetProperty("status").GetString());
        Assert.Equal("Legacy Platform Engineer", row.GetProperty("postingTitle").GetString());
        Assert.Equal("Legacy Holdings", row.GetProperty("companyName").GetString());

        // appliedAt is submitted_date at UTC midnight, never the 0001 default.
        var appliedAt = row.GetProperty("appliedAt").GetDateTimeOffset();
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), appliedAt);
    }

    private sealed class TestSubjectOwnerIdProvider(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor) : RoleRadar.Domain.Common.IOwnerIdProvider
    {
        public RoleRadar.Domain.Common.OwnerId GetOwnerId()
        {
            var user = httpContextAccessor.HttpContext?.User
                ?? throw new System.Security.SecurityException("No active HTTP context.");
            var sub = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new System.Security.SecurityException("No sub claim.");
            return new RoleRadar.Domain.Common.OwnerId(sub);
        }
    }
}
