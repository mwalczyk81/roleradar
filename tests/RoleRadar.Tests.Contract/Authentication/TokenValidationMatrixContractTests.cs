using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Authentication;

public sealed class TokenValidationMatrixContractTests : IAsyncLifetime
{
    private const string ValidIssuer = "https://identity.roleradar.local";
    private const string ValidAudience = "roleradar.api";
    private const string AllowedSubject = "0192e4a1-0000-7000-8000-000000000001";
    private const string IntruderSubject = "0192e4a1-0000-7000-8000-999999999999";

    private readonly SharedMongoFixture _sharedMongo;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _rsaKey;
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _ecdsaKey;
    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private HttpClient? _httpClient;

    public TokenValidationMatrixContractTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
        _rsa = RSA.Create(2048);
        _rsaKey = new RsaSecurityKey(_rsa) { KeyId = "test-rsa-key-1" };
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _ecdsaKey = new ECDsaSecurityKey(_ecdsa) { KeyId = "test-ecdsa-key-1" };
    }

    public async ValueTask InitializeAsync()
    {
        var dbName = _sharedMongo.CreateDatabaseName("rrtest_auth_matrix");
        _app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _sharedMongo.ConnectionString,
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
                    oidcConfig.SigningKeys.Add(_rsaKey);
                    oidcConfig.SigningKeys.Add(_ecdsaKey);
                    options.Configuration = oidcConfig;
                    options.TokenValidationParameters.ValidIssuer = ValidIssuer;
                    options.TokenValidationParameters.ValidAudience = ValidAudience;
                    options.TokenValidationParameters.IssuerSigningKeys = [_rsaKey, _ecdsaKey];
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
        _rsa.Dispose();
        _ecdsa.Dispose();
    }

    [Fact]
    public async Task MissingToken_Returns401Unauthorized()
    {
        var response = await _httpClient!.GetAsync("/api/v1/criteria");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuer_Returns401Unauthorized()
    {
        var token = CreateRs256Token(
            issuer: "https://wrong-issuer.roleradar.local",
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10));

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_Returns401Unauthorized()
    {
        var token = CreateRs256Token(
            issuer: ValidIssuer,
            audience: "wrong.audience",
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10));

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnauthorizedSymmetricAlgorithm_Hs256_Returns401Unauthorized()
    {
        var symmetricKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("super-secret-symmetric-hmac-key-256-bits-long!!"));
        var creds = new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            Subject = new ClaimsIdentity([new Claim("sub", AllowedSubject)]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = creds
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateEncodedJwt(descriptor);

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExpiredToken_BeyondClockSkew_Returns401Unauthorized()
    {
        var token = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            notBeforeUtc: DateTime.UtcNow.AddMinutes(-20),
            expiresUtc: DateTime.UtcNow.AddMinutes(-5));

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonAllowlistedSubject_Returns403Forbidden()
    {
        var token = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: IntruderSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10));

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ValidAllowlistedSubject_Rs256_Returns200Ok()
    {
        var token = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10));

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidAllowlistedSubject_Es256_Returns200Ok()
    {
        var creds = new SigningCredentials(_ecdsaKey, SecurityAlgorithms.EcdsaSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = ValidIssuer,
            Audience = ValidAudience,
            Subject = new ClaimsIdentity([new Claim("sub", AllowedSubject)]),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = creds
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateEncodedJwt(descriptor);

        using var request = CreateAuthenticatedGet("/api/v1/criteria", token);
        var response = await _httpClient!.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private string CreateRs256Token(
        string issuer,
        string audience,
        string subject,
        DateTime expiresUtc,
        DateTime? notBeforeUtc = null)
    {
        var creds = new SigningCredentials(_rsaKey, SecurityAlgorithms.RsaSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("sub", subject)]),
            NotBefore = notBeforeUtc ?? DateTime.UtcNow.AddMinutes(-1),
            Expires = expiresUtc,
            SigningCredentials = creds
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.CreateEncodedJwt(descriptor);
    }

    private static HttpRequestMessage CreateAuthenticatedGet(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
