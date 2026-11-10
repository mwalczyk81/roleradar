using System.Text.Json;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Hosting;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Observability;

/// <summary>
/// Feature 013 US1 (FR-001, FR-014): with ROLERADAR_LOG_FORMAT=json the CLI and the MCP host write one JSON object per
/// stderr line, with their service name and a trace id (the CLI's run id, MCP's per-call id; feature 014), and never log to
/// stdout. Launched as real processes so the test sees exactly what production writes.
/// </summary>
[Trait("Lane", "default")]
public sealed class HostLogFormatIntegrationTests(SharedMongoFixture mongo)
{
    private static List<string> NonEmptyLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();

    private static JsonElement ParseObject(string line)
    {
        using var doc = JsonDocument.Parse(line);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        return doc.RootElement.Clone();
    }

    private static bool LooksLikeLogRecord(string line)
    {
        try
        {
            var element = ParseObject(line);
            return element.TryGetProperty("level", out _) && element.TryGetProperty("service", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Cli_WritesJsonRecordsToStderr_WithItsRunId_AndNothingToStdout()
    {
        var env = new Dictionary<string, string>(mongo.CreateProcessEnvironment(mongo.CreateDatabaseName("rrtest_logfmt_cli")))
        {
            ["ROLERADAR_LOG_FORMAT"] = "json",
            ["ROLERADAR_LOG_LEVEL"] = "Debug",
        };

        var (exitCode, stdout, stderr) = await LaunchedCliRunner.RunCliAsync("verify", env, forceContainer: false);
        Assert.Equal(0, exitCode);

        // Non-vacuous capture first: at least one stderr line, and the verify query's QueryStarting record among them.
        var lines = NonEmptyLines(stderr);
        Assert.NotEmpty(lines);
        var records = lines.Select(ParseObject).ToList();
        Assert.Contains(records, r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 2001);

        Assert.All(records, r =>
        {
            Assert.Equal("cli", r.GetProperty("service").GetString());
            Assert.Matches("^[0-9a-f]{32}$", r.GetProperty("traceId").GetString()!);
            Assert.False(r.TryGetProperty("correlationId", out _));
        });
        Assert.Single(records.Select(r => r.GetProperty("traceId").GetString()).Distinct());
        Assert.DoesNotContain(NonEmptyLines(stdout), LooksLikeLogRecord);
    }

    [Fact]
    public async Task Mcp_WritesJsonRecordsToStderr_WithAPerCallId()
    {
        var env = new Dictionary<string, string>(mongo.CreateProcessEnvironment(mongo.CreateDatabaseName("rrtest_logfmt_mcp")))
        {
            ["ROLERADAR_LOG_FORMAT"] = "json",
            ["Logging__LogLevel__Default"] = "Debug",
        };

        string stderr;
        await using (var mcp = await McpProcessClient.StartAsync(McpHostLocator.GetMcpDllPath(), env, forceContainer: false))
        {
            await mcp.CallToolRawAsync("note_search", new { query = "fictional" });
            await mcp.CallToolRawAsync("note_search", new { query = "another" });
            stderr = mcp.GetStderr();
        }

        // Non-vacuous capture first: lines exist and the tool calls' query records are among them.
        var lines = NonEmptyLines(stderr);
        Assert.NotEmpty(lines);
        var records = lines.Select(ParseObject).ToList();
        var queryStarting = records.Where(r => r.TryGetProperty("eventId", out var e) && e.GetInt32() == 2001).ToList();
        Assert.True(queryStarting.Count >= 2, $"Expected the two tool calls' QueryStarting records. Stderr:\n{stderr}");

        Assert.All(records, r => Assert.Equal("mcp", r.GetProperty("service").GetString()));
        var callIds = queryStarting.Select(r => r.GetProperty("traceId").GetString()).ToList();
        Assert.All(callIds, id => Assert.Matches("^[0-9a-f]{32}$", id!));
        Assert.Equal(callIds.Count, callIds.Distinct().Count());
    }
}
