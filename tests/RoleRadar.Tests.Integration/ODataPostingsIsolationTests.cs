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
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ODataPostingsIsolationTests : IAsyncLifetime
{
    private const string TestSecret = "this-is-a-very-long-test-secret-key-for-odata-isolation-32-bytes!";
    private const string TestIssuer = "https://test-identity.local";
    private const string TestAudience = "roleradar.api";
    private const string OwnerA = "owner-alpha-1111";
    private const string OwnerB = "owner-beta-2222";

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
        _database = client.GetDatabase("odata_isolation_test_db");
        await SeedDataAsync();

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));

        _apiApp = Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _container.GetConnectionString(),
                    ["DatabaseName"] = "odata_isolation_test_db",
                    ["Authentication:Authority"] = TestIssuer,
                    ["Authentication:Audience"] = TestAudience,
                    ["Owner:AllowedSubjects:0"] = OwnerA,
                    ["Owner:AllowedSubjects:1"] = OwnerB
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

                builder.Services.Replace(ServiceDescriptor.Scoped<RoleRadar.Domain.Common.IOwnerIdProvider, TestSubjectOwnerIdProvider>());
            });

        await _apiApp.StartAsync();

        var server = _apiApp.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _client = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _apiApp.StopAsync();
        await _apiApp.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static string GenerateJwt(string sub)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(TestSecret);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, sub),
                new Claim("scope", "roleradar.api")
            ]),
            Issuer = TestIssuer,
            Audience = TestAudience,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };
        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    private async Task SeedDataAsync()
    {
        var collection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);

        var docs = new List<BsonDocument>
        {
            // Owner A postings
            new()
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = OwnerA,
                ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["company_name"] = "Alpha Corp",
                ["title"] = "Alpha Architect",
                ["disposition"] = "applied",
                ["disposition_rank"] = 3,
                ["score"] = 90,
                ["has_score"] = true,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            },
            // Owner B postings
            new()
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = OwnerB,
                ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["company_name"] = "Beta Corp",
                ["title"] = "Beta Developer",
                ["disposition"] = "lead",
                ["disposition_rank"] = 1,
                ["score"] = 70,
                ["has_score"] = true,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            }
        };

        await collection.InsertManyAsync(docs);
    }

    [Fact]
    public async Task OwnerA_Cannot_See_OwnerB_Postings_And_OwnerId_Is_Never_Exposed()
    {
        var tokenA = GenerateJwt(OwnerA);
        using var requestA = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        requestA.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var respA = await _client.SendAsync(requestA);
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);

        var rawJson = await respA.Content.ReadAsStringAsync();
        Assert.DoesNotContain("owner_id", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", rawJson, StringComparison.OrdinalIgnoreCase);

        var jsonDoc = JsonDocument.Parse(rawJson);
        var items = jsonDoc.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Alpha Architect", items[0].GetProperty("title").GetString());
        Assert.Equal("Alpha Corp", items[0].GetProperty("companyName").GetString());

        // Verify Owner B token returns only Owner B items
        var tokenB = GenerateJwt(OwnerB);
        using var requestB = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        requestB.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var respB = await _client.SendAsync(requestB);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);

        var rawJsonB = await respB.Content.ReadAsStringAsync();
        Assert.DoesNotContain("owner_id", rawJsonB, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId", rawJsonB, StringComparison.OrdinalIgnoreCase);

        var jsonDocB = JsonDocument.Parse(rawJsonB);
        var itemsB = jsonDocB.RootElement.GetProperty("value").EnumerateArray().ToList();
        Assert.Single(itemsB);
        Assert.Equal("Beta Developer", itemsB[0].GetProperty("title").GetString());
        Assert.Equal("Beta Corp", itemsB[0].GetProperty("companyName").GetString());
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
