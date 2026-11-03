namespace RoleRadar.Tests.Integration;

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
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
using MongoDB.Driver;
using RoleRadar.Api;
using RoleRadar.Domain.Common;
using Testcontainers.MongoDb;

/// <summary>
/// Real API host (JWT bearer, OData routes, real Mongo repositories) over an isolated Testcontainers
/// replica set. Shared by the Phase 5 OData test classes so each one only declares its seed data.
/// </summary>
internal sealed class ODataTestHost : IAsyncDisposable
{
    private const string TestSecret = "this-is-a-very-long-test-secret-key-for-odata-host-32-bytes!";
    private const string TestIssuer = "https://test-identity.local";
    private const string TestAudience = "roleradar.api";

    private readonly MongoDbContainer _container;
    private readonly string[] _owners;
    private readonly string _databaseName;
    private Microsoft.AspNetCore.Builder.WebApplication? _app;

    public ODataTestHost(string databaseName, params string[] owners)
    {
        _databaseName = databaseName;
        _owners = owners;
        _container = new MongoDbBuilder("mongo:8.0.34")
            .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
            .WithReplicaSet("rs0")
            .Build();
    }

    public HttpClient Client { get; private set; } = null!;

    public IMongoDatabase Database { get; private set; } = null!;

    public IServiceProvider Services => _app!.Services;

    /// <summary>When set, the API connects here instead of the container (used to start the API with Mongo unreachable).</summary>
    public string? ApiMongoConnectionString { get; set; }

    /// <summary>The container's connection string (credentials and mapped port included).</summary>
    public string ContainerConnectionString => _container.GetConnectionString();

    /// <summary>Starts the container and the database. Call <see cref="StartApiAsync"/> after seeding.</summary>
    public async Task StartDatabaseAsync()
    {
        await MongoContainerStartGate.StartAsync(_container);
        Database = new MongoClient(_container.GetConnectionString()).GetDatabase(_databaseName);
    }

    public async Task StartApiAsync()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));

        _app = Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = ApiMongoConnectionString ?? _container.GetConnectionString(),
                    ["DatabaseName"] = _databaseName,
                    ["Authentication:Authority"] = TestIssuer,
                    ["Authentication:Audience"] = TestAudience
                };
                for (var i = 0; i < _owners.Length; i++)
                {
                    settings[$"Owner:AllowedSubjects:{i}"] = _owners[i];
                }

                builder.Configuration.AddInMemoryCollection(settings);

                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.Authority = string.Empty;
                    opts.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = TestIssuer });
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

        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<IServer>();
        var baseAddress = server.Features.Get<IServerAddressesFeature>()?.Addresses.First()
            ?? throw new InvalidOperationException("No server address found.");
        Client = new HttpClient { BaseAddress = new Uri(baseAddress) };
    }

    public string Token(string subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subject), new Claim("scope", "roleradar.api")],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public async Task<HttpResponseMessage> GetAsync(string owner, string pathAndQuery)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(owner));
        return await Client.SendAsync(request);
    }

    public async Task<(System.Net.HttpStatusCode Status, JsonDocument Body)> GetJsonAsync(string owner, string pathAndQuery)
    {
        using var response = await GetAsync(owner, pathAndQuery);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text));
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private sealed class TestSubjectOwnerIdProvider(Microsoft.AspNetCore.Http.IHttpContextAccessor httpContextAccessor) : IOwnerIdProvider
    {
        public OwnerId GetOwnerId()
        {
            var user = httpContextAccessor.HttpContext?.User
                ?? throw new System.Security.SecurityException("No active HTTP context.");
            var sub = user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? throw new System.Security.SecurityException("No sub claim.");
            return new OwnerId(sub);
        }
    }
}
