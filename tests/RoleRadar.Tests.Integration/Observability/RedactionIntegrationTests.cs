using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Observability;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Hosting;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Observability;

/// <summary>
/// Feature 013 US1 (FR-012, FR-013, SC-002, SC-003): sentinel secrets pushed through the real API, CLI, MCP and
/// Application paths never reach log output. Every captured line must parse as JSON. Identity's cases live in the
/// Contract project (IdentityRedactionContractTests) because the Identity test server is there. Values are fictional.
/// </summary>
[Trait("Lane", "default")]
public sealed class RedactionIntegrationTests(SharedMongoFixture mongo)
{
    private const string SentinelPassword = "SENTINEL-PW-7f3a"; // gitleaks:allow (fictional redaction test vector, not a credential)
    private const string SentinelUser = "rr-fictional-user";
    private const string SentinelBody = "SENTINEL-NOTE-BODY-7f3a";
    private const string SentinelAppend = "SENTINEL-APPENDED-7f3a";
    private const string SentinelToken = "SENTINEL-TOKEN-7f3a";
    private const string Subject = "0192e4a1-0000-7000-8000-0000000000aa";
    private const string Issuer = "https://test-identity.local";
    private const string Audience = "roleradar.api";
    private const string SigningSecret = "this-is-a-very-long-test-secret-key-for-redaction-32-bytes!";
    private static readonly string[] FictionalTags = ["fictional"];

    private static string UnreachableMongoWithCredentials =>
        $"mongodb://{SentinelUser}:{SentinelPassword}@127.0.0.1:1/?directConnection=true";

    private static List<JsonElement> ParseAll(IEnumerable<string> lines)
    {
        var records = new List<JsonElement>();
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            records.Add(doc.RootElement.Clone());
        }

        return records;
    }

    private static void AssertNoSentinels(string text, params string[] sentinels)
    {
        foreach (var sentinel in sentinels)
        {
            Assert.DoesNotContain(sentinel, text, StringComparison.Ordinal);
        }
    }

    private static string ValidToken(string subject = Subject)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret));
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subject), new Claim("scope", "roleradar.api")],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartApiAsync(string connectionString, string databaseName, FormatterLogCapture capture)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningSecret));
        var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Logging.AddProvider(capture);
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = connectionString,
                    ["DatabaseName"] = databaseName,
                    ["Authentication:Authority"] = Issuer,
                    ["Authentication:Audience"] = Audience,
                    ["Owner:AllowedSubjects:0"] = Subject,
                });
                builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.Authority = string.Empty;
                    opts.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration { Issuer = Issuer });
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = Issuer,
                        ValidAudience = Audience,
                        IssuerSigningKey = key,
                    };
                });
            });

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, new HttpClient { BaseAddress = new Uri(address) });
    }

    [Fact]
    public async Task Api_BearerTokens_AreNeverLogged()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("api", LogFormat.Json));
        var (app, client) = await StartApiAsync(mongo.ConnectionString, mongo.CreateDatabaseName("rrtest_redact_api"), capture);
        try
        {
            var forged = $"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ7{SentinelToken}In0.{SentinelToken}";
            using (var bad = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings"))
            {
                bad.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forged);
                await client.SendAsync(bad);
            }

            var valid = ValidToken();
            using (var good = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings"))
            {
                good.Headers.Authorization = new AuthenticationHeaderValue("Bearer", valid);
                await client.SendAsync(good);
            }

            // Non-vacuous capture first.
            Assert.NotEmpty(capture.Lines);
            var records = ParseAll(capture.Lines);
            Assert.Contains(records, r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted");
            AssertNoSentinels(capture.Text, SentinelToken, forged, valid);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Api_ConnectionStringCredentials_AreNeverLogged_WhenMongoIsUnreachable()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("api", LogFormat.Json));
        var (app, client) = await StartApiAsync(UnreachableMongoWithCredentials, "rrtest_redact_unreachable", capture);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/odata/Postings");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ValidToken());
            await client.SendAsync(request);

            Assert.NotEmpty(capture.Lines);
            var records = ParseAll(capture.Lines);
            Assert.Contains(records, r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted");
            AssertNoSentinels(capture.Text, SentinelPassword, SentinelUser);
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Cli_ConnectionStringCredentials_AreNeverLogged()
    {
        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = UnreachableMongoWithCredentials,
            ["ROLERADAR_MONGO_DATABASE"] = "rrtest_redact_cli",
            ["ROLERADAR_LOG_FORMAT"] = "json",
            ["ROLERADAR_LOG_LEVEL"] = "Debug",
        };

        var (_, stdout, stderr) = await LaunchedCliRunner.RunCliAsync("status", env, forceContainer: false);

        // The CLI also writes its human-readable error text to stderr; log records are the lines that start with "{".
        var lines = stderr.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith('{')).ToList();
        Assert.NotEmpty(lines);
        var records = ParseAll(lines);
        Assert.Contains(records, r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 3001);
        AssertNoSentinels(stderr, SentinelPassword, SentinelUser);
        AssertNoSentinels(stdout, SentinelPassword);
    }

    [Fact]
    public async Task Mcp_ToolArguments_AreNeverLogged()
    {
        var databaseName = mongo.CreateDatabaseName("rrtest_redact_mcp");
        var env = new Dictionary<string, string>(mongo.CreateProcessEnvironment(databaseName))
        {
            ["ROLERADAR_LOG_FORMAT"] = "json",
        };

        string stderr;
        await using (var mcp = await McpProcessClient.StartAsync(McpHostLocator.GetMcpDllPath(), env, forceContainer: false))
        {
            await mcp.CallToolRawAsync("note_create", new
            {
                title = $"Fictional note Bearer {SentinelToken}",
                body = SentinelBody,
                area = "general",
                tags = FictionalTags,
                provenanceKind = "stated",
                provenanceDate = "2026-11-09",
            });
            await mcp.CallToolRawAsync("note_create", new
            {
                title = "Too long",
                body = new string('x', 8 * 1024 * 1024 + 1) + SentinelBody,
                area = "general",
                provenanceKind = "stated",
                provenanceDate = "2026-11-09",
            });
            stderr = mcp.GetStderr();
        }

        var lines = stderr.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        Assert.NotEmpty(lines);
        var records = ParseAll(lines);
        Assert.Contains(records, r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 1001);
        AssertNoSentinels(stderr, SentinelBody, SentinelToken);
    }

    [Fact]
    public async Task NoteHandlers_NeverLogNoteBodiesOrAppendedText()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("cli", LogFormat.Json));
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(capture));
        services.AddRoleRadarApplication();
        var options = mongo.CreateOptions(mongo.CreateDatabaseName("rrtest_redact_notes"));
        services.AddRoleRadarInfrastructure(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.DatabaseName = options.DatabaseName;
        });
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var create = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();
        var append = scope.ServiceProvider.GetRequiredService<ICommandHandler<AppendToNoteCommand, NoteUpdatedDto>>();

        var created = await create.HandleAsync(new CreateNoteCommand("Fictional", SentinelBody, "general", ["fictional"], "stated", "2026-11-09", null, Actor: "test-redaction"));
        Assert.True(created.IsSuccess);
        await append.HandleAsync(new AppendToNoteCommand(created.Value!.Id, SentinelAppend, Actor: "test-redaction"));
        await create.HandleAsync(new CreateNoteCommand("Too long", new string('x', 8 * 1024 * 1024 + 1) + SentinelBody, "general", null, "stated", "2026-11-09", null, Actor: "test-redaction"));

        Assert.NotEmpty(capture.Lines);
        var records = ParseAll(capture.Lines);
        Assert.Contains(records, r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 1001);
        AssertNoSentinels(capture.Text, SentinelBody, SentinelAppend);
    }
}
