using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Authentication;

public sealed class CorsRejectionContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _sharedMongo;
    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private HttpClient? _httpClient;

    public CorsRejectionContractTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
    }

    public async ValueTask InitializeAsync()
    {
        var dbName = _sharedMongo.CreateDatabaseName("rrtest_cors");
        _app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _sharedMongo.ConnectionString,
                    ["DatabaseName"] = dbName,
                    ["Authentication:Authority"] = "https://identity.roleradar.local",
                    ["Authentication:Audience"] = "roleradar.api",
                    ["Owner:AllowedSubjects:0"] = "0192e4a1-0000-7000-8000-000000000001"
                });
            });

        await _app.StartAsync();
        var server = _app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        _httpClient = new HttpClient { BaseAddress = new Uri(address) };
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestWithOriginHeader_ReceivesZeroAccessControlAllowOriginHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "https://browser-app.example.com");

        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task PreflightOptionsRequestWithOriginHeader_ReceivesZeroAccessControlAllowOriginHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/criteria");
        request.Headers.Add("Origin", "https://browser-app.example.com");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        var response = await _httpClient!.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Methods"));
    }
}
