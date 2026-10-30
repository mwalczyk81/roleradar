using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Tests.Contract.Fixtures;

namespace RoleRadar.Tests.Contract.Api;

internal sealed class ApiContractTestHost : IAsyncDisposable
{
    public const string ValidIssuer = "https://identity.roleradar.local";
    public const string ValidAudience = "roleradar.api";
    public const string AllowedSubject = "0192e4a1-0000-7000-8000-000000000001";

    private readonly RSA _rsa;
    private readonly RsaSecurityKey _rsaKey;
    private readonly WebApplication _app;

    public HttpClient Client { get; }
    public IServiceProvider Services => _app.Services;
    public string DatabaseName { get; }

    private ApiContractTestHost(
        RSA rsa,
        RsaSecurityKey rsaKey,
        WebApplication app,
        HttpClient client,
        string databaseName)
    {
        _rsa = rsa;
        _rsaKey = rsaKey;
        _app = app;
        Client = client;
        DatabaseName = databaseName;
    }

    public static async Task<ApiContractTestHost> StartAsync(
        SharedMongoFixture sharedMongo,
        string dbPrefix = "rrtest_api_contract")
    {
        var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "api-contract-rsa-1" };
        var dbName = sharedMongo.CreateDatabaseName(dbPrefix);

        var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = sharedMongo.ConnectionString,
                    ["DatabaseName"] = dbName,
                    ["Authentication:Authority"] = ValidIssuer,
                    ["Authentication:Audience"] = ValidAudience,
                    ["Owner:AllowedSubjects:0"] = AllowedSubject
                });

                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = string.Empty;
                    options.RequireHttpsMetadata = false;
                    var oidcConfig = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
                    {
                        Issuer = ValidIssuer
                    };
                    oidcConfig.SigningKeys.Add(rsaKey);
                    options.Configuration = oidcConfig;
                    options.TokenValidationParameters.ValidIssuer = ValidIssuer;
                    options.TokenValidationParameters.ValidAudience = ValidAudience;
                    options.TokenValidationParameters.IssuerSigningKeys = [rsaKey];
                });
            });

        await app.StartAsync();
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        var client = new HttpClient { BaseAddress = new Uri(address) };
        var host = new ApiContractTestHost(rsa, rsaKey, app, client, dbName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.CreateValidOwnerToken());

        return host;
    }

    public string CreateValidOwnerToken(string subject = AllowedSubject)
    {
        var creds = new SigningCredentials(_rsaKey, SecurityAlgorithms.RsaSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = creds
        };

        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        _rsa.Dispose();
    }
}
