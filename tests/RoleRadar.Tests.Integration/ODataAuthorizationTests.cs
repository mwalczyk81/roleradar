namespace RoleRadar.Tests.Integration;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Api;
using RoleRadar.Tests.Integration.Fixtures;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ODataAuthorizationTestsFixture : IAsyncLifetime
{
    private const string TestSecret = "this-is-a-very-long-test-secret-key-for-odata-auth-32-bytes!";
    private const string TestIssuer = "https://test-identity.local";
    private const string TestAudience = "roleradar.api";
    public const string ValidOwnerSub = "valid-owner-subject-1234";
    public const string InvalidOwnerSub = "attacker-subject-5678";

    private readonly MongoDbContainer _container = new MongoDbBuilder(MongoTestImage.Tag)
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private Microsoft.AspNetCore.Builder.WebApplication _apiApp = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        await MongoContainerStartGate.StartAsync(_container);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));

        _apiApp = Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _container.GetConnectionString(),
                    ["DatabaseName"] = "odata_auth_test_db",
                    ["Authentication:Authority"] = TestIssuer,
                    ["Authentication:Audience"] = TestAudience,
                    ["Owner:AllowedSubjects:0"] = ValidOwnerSub
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
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _apiApp.StopAsync();
        await _apiApp.DisposeAsync();
        await _container.DisposeAsync();
    }

    public static string GenerateJwt(string sub, string scope)
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

    public HttpClient Client => _client;
}

/// <summary>Read-only tests share one container and API host (see <see cref="ODataAuthorizationTestsFixture"/>); each test is independent of the others.</summary>
public sealed class ODataAuthorizationTests : IClassFixture<ODataAuthorizationTestsFixture>
{
    private readonly HttpClient _client;

    private const string ValidOwnerSub = ODataAuthorizationTestsFixture.ValidOwnerSub;
    private const string InvalidOwnerSub = ODataAuthorizationTestsFixture.InvalidOwnerSub;

    public ODataAuthorizationTests(ODataAuthorizationTestsFixture fixture) => _client = fixture.Client;

    private static string GenerateJwt(string sub, string scope) => ODataAuthorizationTestsFixture.GenerateJwt(sub, scope);

    [Fact]
    public async Task AnonymousRequest_Returns_401Unauthorized()
    {
        var response = await _client.GetAsync("/odata/Postings");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongScope_Returns_403Forbidden()
    {
        var token = GenerateJwt(ValidOwnerSub, "roleradar.other");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NonOwnerSubject_Returns_403Forbidden()
    {
        var token = GenerateJwt(InvalidOwnerSub, "roleradar.api");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidOwnerToken_Returns_200OK()
    {
        var token = GenerateJwt(ValidOwnerSub, "roleradar.api");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
