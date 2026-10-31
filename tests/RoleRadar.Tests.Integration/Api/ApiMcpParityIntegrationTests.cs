using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Api;

public sealed class ApiMcpParityIntegrationTests(IsolatedContainerMongoFixture mongoFixture)
    : IClassFixture<IsolatedContainerMongoFixture>
{
    private const string ValidIssuer = "https://identity.roleradar.local";
    private const string ValidAudience = "roleradar.api";
    private const string AllowedSubject = "0192e4a1-0000-7000-8000-000000000001";
    private static readonly string[] SeedCategoryTags = ["robotics", "ai"];

    private readonly IsolatedContainerMongoFixture _mongo = mongoFixture;

    [Fact]
    public async Task McpCreatedData_IsVisibleViaRestApi_And_ApiWritesEmitMcpOperationNamesWithApiUserActor()
    {
        var dbName = $"rrtest_api_mcp_parity_{Guid.CreateVersion7():N}";
        var mcpEnv = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = _mongo.HostMongoUri,
            ["ROLERADAR_MONGO_DATABASE"] = dbName
        };
        var mcpDll = GetMcpDllPath();

        // 1. Seed company and posting through the shipped MCP stdio server
        await using var mcpClient = await StartMcpClientAsync(mcpDll, mcpEnv);

        var companyResult = await mcpClient.CallToolAsync("company_record", new
        {
            name = "Chronos Robotics",
            description = "Autonomous warehouse robotics",
            ownershipType = "private",
            status = "neutral",
            headquarters = "Boston, MA",
            categoryTags = SeedCategoryTags
        });

        var companyId = companyResult.GetProperty("data").GetProperty("company").GetProperty("id").GetGuid();

        var postingResult = await mcpClient.CallToolAsync("posting_record", new
        {
            companyId = companyId.ToString(),
            title = "Principal Robotics Control Engineer",
            requisitionId = "CHR-555",
            atsTenant = "chronos",
            location = "Remote",
            score = 8.8m
        });

        var postingData = postingResult.GetProperty("data").GetProperty("posting");
        var postingId = postingData.GetProperty("id").GetGuid();
        var postingVersionToken = postingData.GetProperty("versionToken").GetGuid();

        // 2. Start RoleRadar.Api against the same Mongo database
        using var rsa = RSA.Create(2048);
        var rsaKey = new RsaSecurityKey(rsa) { KeyId = "parity-rsa-1" };

        await using var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = _mongo.HostMongoUri,
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
        var baseAddress = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
        var creds = new SigningCredentials(rsaKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityTokenHandler().CreateEncodedJwt(new SecurityTokenDescriptor
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
        });
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 3. Verify MCP-created posting and company are visible through the REST API
        var getPostingResp = await httpClient.GetAsync($"/api/v1/postings/{postingId}");
        Assert.Equal(HttpStatusCode.OK, getPostingResp.StatusCode);
        var postingJson = await getPostingResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Principal Robotics Control Engineer", postingJson.GetProperty("title").GetString());
        Assert.Equal("Chronos Robotics", postingJson.GetProperty("companyName").GetString());

        // 4. Perform write operations via REST API:
        //    a) PATCH /api/v1/companies/{id} -> company_update
        var patchCompanyResp = await httpClient.PatchAsJsonAsync($"/api/v1/companies/{companyId}", new
        {
            status = "target"
        });
        Assert.Equal(HttpStatusCode.OK, patchCompanyResp.StatusCode);

        //    b) POST /api/v1/postings/{id}/transition -> posting_transition
        var transitionResp = await httpClient.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "screened_out",
            reason = "Focus on pure software roles first",
            versionToken = postingVersionToken.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, transitionResp.StatusCode);

        // 5. Verify append-only audit log entries have operation names matching MCP tools and actor "api-user"
        using var scope = app.Services.CreateScope();
        var noteRepo = scope.ServiceProvider.GetRequiredService<INoteRepository>();
        var auditsResult = await noteRepo.GetAuditEntriesAsync(OwnerId.Default.Value);
        Assert.True(auditsResult.IsSuccess);

        var apiAudits = auditsResult.Value!
            .Where(a => a.Actor == "api-user")
            .ToArray();

        Assert.Contains(apiAudits, a => a.Operation == "company_update" && a.EntityId == companyId);
        Assert.Contains(apiAudits, a => a.Operation == "posting_transition" && a.EntityId == postingId);

        await app.StopAsync();
    }

    private static string GetMcpDllPath()
    {
        var mcpDllPath = typeof(RoleRadar.Mcp.Stdio.Program).Assembly.Location;
        if (string.IsNullOrWhiteSpace(mcpDllPath) || !File.Exists(mcpDllPath))
        {
            mcpDllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Mcp.Stdio.dll");
        }

        Assert.True(File.Exists(mcpDllPath), $"Expected built MCP server assembly at '{mcpDllPath}'.");
        return mcpDllPath;
    }

    private static async Task<LocalMcpClient> StartMcpClientAsync(string mcpDllPath, IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{mcpDllPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var (k, v) in env)
        {
            psi.Environment[k] = v;
        }

        var proc = Process.Start(psi)!;
        Assert.NotNull(proc);
        proc.BeginErrorReadLine();

        var client = new LocalMcpClient(proc);
        await client.InitializeAsync();
        return client;
    }

    private sealed class LocalMcpClient(Process process) : IAsyncDisposable
    {
        private readonly Process _process = process;
        private int _nextId = 1;

        public async Task InitializeAsync()
        {
            var initJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = _nextId++,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "integration-test-client", version = "1.0.0" }
                }
            });

            await _process.StandardInput.WriteLineAsync(initJson);
            await _process.StandardInput.FlushAsync();
            var initResponse = await _process.StandardOutput.ReadLineAsync();
            Assert.NotNull(initResponse);

            var notifJson = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await _process.StandardInput.WriteLineAsync(notifJson);
            await _process.StandardInput.FlushAsync();
        }

        public async Task<JsonElement> CallToolAsync(string name, object arguments)
        {
            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = id,
                method = "tools/call",
                @params = new
                {
                    name = name,
                    arguments = arguments
                }
            });

            await _process.StandardInput.WriteLineAsync(request);
            await _process.StandardInput.FlushAsync();

            var responseLine = await _process.StandardOutput.ReadLineAsync();
            Assert.NotNull(responseLine);

            using var doc = JsonDocument.Parse(responseLine);
            var content = doc.RootElement.GetProperty("result").GetProperty("content");
            var text = content[0].GetProperty("text").GetString()!;
            return JsonDocument.Parse(text).RootElement.Clone();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.StandardInput.Close();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(cts.Token);
            }
            catch
            {
                try { _process.Kill(entireProcessTree: true); } catch { }
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
