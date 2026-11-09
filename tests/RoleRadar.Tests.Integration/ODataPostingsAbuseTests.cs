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
using MongoDB.Driver;
using RoleRadar.Api;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ODataPostingsAbuseTestsFixture : IAsyncLifetime
{
    private const string TestSecret = "this-is-a-very-long-test-secret-key-for-odata-abuse-32-bytes!";
    private const string TestIssuer = "https://test-identity.local";
    private const string TestAudience = "roleradar.api";
    private const string OwnerSub = "postings-abuse-owner";

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
                    ["DatabaseName"] = "odata_abuse_test_db",
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

        var token = GenerateJwt(OwnerSub);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    public HttpClient Client => _client;
}

/// <summary>Read-only tests share one container and API host (see <see cref="ODataPostingsAbuseTestsFixture"/>); each test is independent of the others.</summary>
public sealed class ODataPostingsAbuseTests : IClassFixture<ODataPostingsAbuseTestsFixture>
{
    private readonly HttpClient _client;

    public ODataPostingsAbuseTests(ODataPostingsAbuseTestsFixture fixture) => _client = fixture.Client;

    [Fact]
    public async Task Top_Exceeding_100_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$top=101");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Negative_Skip_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$skip=-5");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unlisted_Property_In_Filter_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$filter=NonExistentProperty eq 'hacked'");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unlisted_Property_In_OrderBy_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$orderby=NonExistentProperty asc");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disallowed_Option_Expand_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$expand=Company");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Disallowed_Option_Apply_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$apply=aggregate(Score with sum as TotalScore)");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OrderBy_With_Function_Expression_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$orderby=tolower(title)");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrderBy_With_Multiple_Properties_Including_Unlisted_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$orderby=title,foo");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Node_Count_Limit_Exceeded_Returns_400_Problem_Details()
    {
        // 105 binary OR nodes exceeds MaxNodeCount (100)
        var filter = string.Join(" or ", Enumerable.Range(0, 105).Select(i => $"title eq 'role{i}'"));
        var response = await _client.GetAsync($"/odata/Postings?$filter={Uri.EscapeDataString(filter)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Depth_Limit_Exceeded_Returns_400_Problem_Details()
    {
        // 15 levels of nested binary expressions exceeding max depth (10)
        var filter = "title eq 'role0'";
        for (var i = 1; i <= 15; i++)
        {
            filter = $"title eq 'role{i}' and ({filter})";
        }

        var response = await _client.GetAsync($"/odata/Postings?$filter={Uri.EscapeDataString(filter)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Arithmetic_Operator_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$filter=score add 1 eq 5");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Lambda_Any_Operator_Returns_400_Problem_Details()
    {
        var response = await _client.GetAsync("/odata/Postings?$filter=title/any()");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("validation_error", body, StringComparison.OrdinalIgnoreCase);
    }
}
