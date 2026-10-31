using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Authentication;

public sealed class TokenValidationMatrixContractTests : IAsyncLifetime
{
    private const string ValidIssuer = "https://identity.roleradar.local";
    private const string ValidAudience = "roleradar.api";
    private const string AllowedSubject = "0192e4a1-0000-7000-8000-000000000001";
    private const string IntruderSubject = "0192e4a1-0000-7000-8000-999999999999";

    private static readonly string[] MultipleReadScopes = ["openid", "roleradar.api.read"];
    private static readonly string[] MultipleFullScopes = ["openid", "roleradar.api"];
    private readonly SharedMongoFixture _sharedMongo;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _rsaKey;
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _ecdsaKey;
    private Microsoft.AspNetCore.Builder.WebApplication? _app;
    private HttpClient? _httpClient;
    private Guid _seededCompanyId;

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

        using var scope = _app.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var seedRes = await companyHandler.HandleAsync(new RecordCompanyCommand(
            Name: "Scope Enforcement Test Corp",
            Description: "Fictional company for scope enforcement contract tests",
            OwnershipType: "private",
            Status: "neutral",
            Headquarters: "Seattle, WA"));
        Assert.True(seedRes.IsSuccess);
        _seededCompanyId = seedRes.Value!.Company.Id;
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
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", AllowedSubject),
                new Claim("scope", "roleradar.api")
            ]),
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

    [Fact]
    public async Task ReadOnlyScopeToken_OnGet_Returns200Ok_ForBothSpaceDelimitedAndMultipleScopeClaims()
    {
        // 1. Space-delimited single "scope" claim containing roleradar.api.read
        var singleClaimToken = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10),
            scopes: ["openid profile roleradar.api.read"]);

        using var req1 = CreateAuthenticatedGet("/api/v1/companies", singleClaimToken);
        var resp1 = await _httpClient!.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // 2. Multiple "scope" claims
        var multiClaimToken = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10),
            scopes: MultipleReadScopes);

        using var req2 = CreateAuthenticatedGet("/api/v1/criteria", multiClaimToken);
        var resp2 = await _httpClient!.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
    }

    [Fact]
    public async Task ReadOnlyScopeToken_OnWrite_Returns403Forbidden()
    {
        var readOnlyToken = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10),
            scopes: ["roleradar.api.read"]);

        // PATCH /api/v1/companies/{id} with read-only scope -> 403 Forbidden
        using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/companies/{_seededCompanyId}")
        {
            Content = JsonContent.Create(new { status = "target" })
        };
        patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readOnlyToken);
        var patchResp = await _httpClient!.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);

        // POST /api/v1/postings/{id}/transition with read-only scope -> 403 Forbidden
        using var postReq = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/postings/{Guid.NewGuid()}/transition")
        {
            Content = JsonContent.Create(new
            {
                toDisposition = "screened_out",
                reason = "Test",
                versionToken = Guid.NewGuid().ToString()
            })
        };
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", readOnlyToken);
        var postResp = await _httpClient!.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postResp.StatusCode);
    }

    [Fact]
    public async Task FullScopeToken_OnWrite_Returns200Ok()
    {
        var fullScopeToken = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10),
            scopes: MultipleFullScopes);

        using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/companies/{_seededCompanyId}")
        {
            Content = JsonContent.Create(new { status = "target" })
        };
        patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fullScopeToken);
        var patchResp = await _httpClient!.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
    }

    [Fact]
    public async Task TokenWithNoScope_Returns403Forbidden_OnBothReadAndWrite()
    {
        var noScopeToken = CreateRs256Token(
            issuer: ValidIssuer,
            audience: ValidAudience,
            subject: AllowedSubject,
            expiresUtc: DateTime.UtcNow.AddMinutes(10),
            scopes: []);

        using var getReq = CreateAuthenticatedGet("/api/v1/companies", noScopeToken);
        var getResp = await _httpClient!.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);

        using var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/companies/{_seededCompanyId}")
        {
            Content = JsonContent.Create(new { status = "target" })
        };
        patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", noScopeToken);
        var patchResp = await _httpClient!.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);
    }

    private string CreateRs256Token(
        string issuer,
        string audience,
        string subject,
        DateTime expiresUtc,
        DateTime? notBeforeUtc = null,
        IReadOnlyList<string>? scopes = null)
    {
        var claims = new List<Claim> { new("sub", subject) };
        var effectiveScopes = scopes ?? ["roleradar.api"];
        foreach (var scopeValue in effectiveScopes)
        {
            claims.Add(new Claim("scope", scopeValue));
        }

        var creds = new SigningCredentials(_rsaKey, SecurityAlgorithms.RsaSha256);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
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
