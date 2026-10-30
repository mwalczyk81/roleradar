using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Oidc;

public sealed class IdpAgnosticOidcIntegrationTests(IsolatedContainerMongoFixture mongoFixture)
    : IClassFixture<IsolatedContainerMongoFixture>
{
    private const string ClientSecret = "IdpAgnosticWebSecret!2026";
    private readonly IsolatedContainerMongoFixture _mongo = mongoFixture;

    [Fact]
    public async Task StandardOidcDiscovery_AuthorizationCodePkce_TokenRotation_And_AuthenticatedApiCalls_SucceedEndToEnd()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"roleradar-oidc-e2e-{Guid.NewGuid():N}.db");
        var keyDir = Path.Combine(Path.GetTempPath(), $"roleradar-oidc-keys-{Guid.NewGuid():N}");
        var mongoDbName = $"rrtest_oidc_e2e_{Guid.CreateVersion7():N}";

        try
        {
            // 1. Start ephemeral Identity host on a dynamic loopback port
            await using var identityApp = RoleRadar.Identity.Program.CreateApp(
                ["--urls", "http://127.0.0.1:0"],
                builder =>
                {
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:IdentityConnection"] = $"Data Source={dbPath}",
                        ["Clients:Web:Secret"] = ClientSecret,
                        ["Clients:Web:RedirectUri"] = "https://localhost:5003/signin-oidc"
                    });
                    builder.Services.PostConfigure<Duende.IdentityServer.Configuration.IdentityServerOptions>(o =>
                    {
                        o.KeyManagement.KeyPath = keyDir;
                    });
                });

            await identityApp.StartAsync();

            var identityServer = identityApp.Services.GetRequiredService<IServer>();
            var identityBaseUrl = identityServer.Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');

            // Provision the single owner account
            Guid ownerSubId;
            string totpSecret;
            using (var scope = identityApp.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
                var prov = new OwnerProvisioningService(db);
                var provisioned = await prov.ProvisionOwnerAsync("oidc-owner", "StrongPass!2026#Oidc");
                ownerSubId = provisioned.UserId;
                totpSecret = provisioned.AuthenticatorKey;
                var initialCode = prov.ComputeTotpCode(totpSecret);
                Assert.True(await prov.VerifyTotpAndActivateAsync(ownerSubId, initialCode));
            }

            // 2. Discover OIDC metadata strictly via standard /.well-known/openid-configuration
            using var idpHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = true,
                CookieContainer = new CookieContainer()
            };
            using var idpClient = new HttpClient(idpHandler) { BaseAddress = new Uri(identityBaseUrl) };

            var discoveryResp = await idpClient.GetAsync("/.well-known/openid-configuration");
            Assert.Equal(HttpStatusCode.OK, discoveryResp.StatusCode);
            var discoveryJson = await discoveryResp.Content.ReadFromJsonAsync<JsonElement>();

            var discoveredIssuer = discoveryJson.GetProperty("issuer").GetString()!;
            var authorizeEndpoint = discoveryJson.GetProperty("authorization_endpoint").GetString()!;
            var tokenEndpoint = discoveryJson.GetProperty("token_endpoint").GetString()!;
            var jwksUri = discoveryJson.GetProperty("jwks_uri").GetString()!;

            Assert.False(string.IsNullOrWhiteSpace(discoveredIssuer));
            Assert.False(string.IsNullOrWhiteSpace(authorizeEndpoint));
            Assert.False(string.IsNullOrWhiteSpace(tokenEndpoint));
            Assert.False(string.IsNullOrWhiteSpace(jwksUri));

            // 3. Start RoleRadar.Api configured strictly with standard OIDC discovery against the Identity host
            await using var apiApp = RoleRadar.Api.Program.CreateApp(
                ["--urls", "http://127.0.0.1:0"],
                builder =>
                {
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:MongoConnection"] = _mongo.HostMongoUri,
                        ["DatabaseName"] = mongoDbName,
                        ["Authentication:Authority"] = discoveredIssuer,
                        ["Authentication:Audience"] = "roleradar.api",
                        ["Owner:AllowedSubjects:0"] = ownerSubId.ToString()
                    });

                    builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.Authority = discoveredIssuer;
                        options.MetadataAddress = $"{identityBaseUrl}/.well-known/openid-configuration";
                        options.RequireHttpsMetadata = false;
                        options.TokenValidationParameters.ValidIssuer = discoveredIssuer;
                        options.TokenValidationParameters.ValidAudience = "roleradar.api";
                    });
                });

            await apiApp.StartAsync();

            var apiServer = apiApp.Services.GetRequiredService<IServer>();
            var apiBaseUrl = apiServer.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

            // Seed a company in MongoDB via Application handler
            Guid seededCompanyId;
            using (var scope = apiApp.Services.CreateScope())
            {
                var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
                var seedRes = await companyHandler.HandleAsync(new RecordCompanyCommand(
                    Name: "Vanguard Telemetry Corp",
                    Description: "Distributed observability platform",
                    OwnershipType: "private",
                    Status: "target",
                    Headquarters: "Denver, CO"));
                Assert.True(seedRes.IsSuccess);
                seededCompanyId = seedRes.Value!.Company.Id;
            }

            // 4. Perform standard OAuth2/OIDC Authorization Code + PKCE flow using discovered endpoints
            var codeVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk-0123456789abcdef";
            var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            var codeChallenge = Base64UrlEncoder(challengeBytes);

            var authQuery = $"{authorizeEndpoint}?client_id=roleradar-web" +
                $"&redirect_uri={Uri.EscapeDataString("https://localhost:5003/signin-oidc")}" +
                $"&response_type=code" +
                $"&scope={Uri.EscapeDataString("openid profile roleradar.api offline_access")}" +
                $"&code_challenge={codeChallenge}" +
                $"&code_challenge_method=S256" +
                $"&state=oidc_state_1";

            var authChallengeResp = await idpClient.GetAsync(authQuery);
            Assert.True(authChallengeResp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod);

            var loginLocation = authChallengeResp.Headers.Location!;
            var loginPathAndQuery = loginLocation.IsAbsoluteUri ? loginLocation.PathAndQuery : loginLocation.ToString();

            var loginGetResp = await idpClient.GetAsync(loginPathAndQuery);
            var loginHtml = await loginGetResp.Content.ReadAsStringAsync();
            var antiForgeryToken = ExtractRequestVerificationToken(loginHtml, loginPathAndQuery, loginGetResp.StatusCode);
            var queryParams = HttpUtility.ParseQueryString(new Uri(new Uri(identityBaseUrl), loginPathAndQuery).Query);
            var returnUrl = queryParams["ReturnUrl"] ?? queryParams["returnUrl"] ?? "/connect/authorize/callback";

            // Step 1: Submit username + password
            var step1PostResp = await idpClient.PostAsync(loginPathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Username"] = "oidc-owner",
                ["Input.Password"] = "StrongPass!2026#Oidc",
                ["ReturnUrl"] = returnUrl,
                ["RequiresTwoFactor"] = "false",
                ["__RequestVerificationToken"] = antiForgeryToken
            }));
            Assert.Equal(HttpStatusCode.OK, step1PostResp.StatusCode);
            var step2Html = await step1PostResp.Content.ReadAsStringAsync();
            var step2AntiForgeryToken = ExtractRequestVerificationToken(step2Html, loginPathAndQuery, step1PostResp.StatusCode);

            string liveTotp;
            using (var scope = identityApp.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
                var prov = new OwnerProvisioningService(db);
                liveTotp = prov.ComputeTotpCode(totpSecret);
            }

            // Step 2: Submit 6-digit TOTP verification code
            var loginPostResp = await idpClient.PostAsync(loginPathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["TwoFactorInput.Code"] = liveTotp,
                ["ReturnUrl"] = returnUrl,
                ["RequiresTwoFactor"] = "true",
                ["__RequestVerificationToken"] = step2AntiForgeryToken
            }));

            Assert.True(loginPostResp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod);
            var callbackLoc = loginPostResp.Headers.Location!;
            var callbackPath = callbackLoc.IsAbsoluteUri ? callbackLoc.PathAndQuery : callbackLoc.ToString();
            var callbackResp = await idpClient.GetAsync(callbackPath);
            Assert.True(callbackResp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod);

            var clientRedirect = callbackResp.Headers.Location!;
            var authCode = HttpUtility.ParseQueryString(clientRedirect.Query)["code"];
            Assert.False(string.IsNullOrWhiteSpace(authCode));

            // 5. Exchange authorization code at discovered token_endpoint
            var tokenResp = await idpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "roleradar-web",
                ["client_secret"] = ClientSecret,
                ["code"] = authCode!,
                ["redirect_uri"] = "https://localhost:5003/signin-oidc",
                ["code_verifier"] = codeVerifier
            }));
            Assert.Equal(HttpStatusCode.OK, tokenResp.StatusCode);

            var tokenPayload = await tokenResp.Content.ReadFromJsonAsync<JsonElement>();
            var accessToken = tokenPayload.GetProperty("access_token").GetString()!;
            var refreshToken = tokenPayload.GetProperty("refresh_token").GetString()!;

            // Verify strictly standard OIDC/JWT claims on issued access token
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
            Assert.Equal(discoveredIssuer, jwt.Issuer);
            Assert.Contains("roleradar.api", jwt.Audiences);
            Assert.Contains(jwt.Claims, c => c.Type == "sub" && c.Value == ownerSubId.ToString());

            // 6. Call RoleRadar.Api using the discovered OIDC access token
            using var apiClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
            apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var getCompaniesResp = await apiClient.GetAsync("/api/v1/companies");
            Assert.Equal(HttpStatusCode.OK, getCompaniesResp.StatusCode);
            var companiesJson = await getCompaniesResp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, companiesJson.GetProperty("totalCount").GetInt32());
            Assert.Equal(seededCompanyId, companiesJson.GetProperty("items")[0].GetProperty("id").GetGuid());

            // 7. Rotate refresh token via discovered token_endpoint and call API with new access token
            var refreshResp = await idpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = "roleradar-web",
                ["client_secret"] = ClientSecret,
                ["refresh_token"] = refreshToken
            }));
            Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
            var refreshedPayload = await refreshResp.Content.ReadFromJsonAsync<JsonElement>();
            var newAccessToken = refreshedPayload.GetProperty("access_token").GetString()!;

            apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccessToken);
            var getCompanyDetailResp = await apiClient.GetAsync($"/api/v1/companies/{seededCompanyId}");
            Assert.Equal(HttpStatusCode.OK, getCompanyDetailResp.StatusCode);

            await apiApp.StopAsync();
            await identityApp.StopAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }
            if (Directory.Exists(keyDir))
            {
                try { Directory.Delete(keyDir, recursive: true); } catch { }
            }
        }
    }

    private static string ExtractRequestVerificationToken(string html, string url, HttpStatusCode status)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        if (!match.Success)
        {
            match = Regex.Match(html, @"value=""([^""]+)""[^>]*name=""__RequestVerificationToken""");
        }

        Assert.True(match.Success, $"Could not find __RequestVerificationToken at '{url}' (HTTP {(int)status}). HTML:\n{html}");
        return match.Groups[1].Value;
    }

    private static string Base64UrlEncoder(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
