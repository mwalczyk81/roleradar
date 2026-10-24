using System.Text.Json;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerMcpToolSchemaContractTests
{
    private readonly SharedMongoFixture _fixture;

    private static readonly string[] ExpectedToolNames =
    [
        "note_create",
        "note_get",
        "note_append",
        "note_replace_section",
        "note_search",
        "note_archive",
        "company_record",
        "posting_record",
        "posting_transition",
        "tracker_query",
        "tracker_log",
        "application_record"
    ];

    public ContainerMcpToolSchemaContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    [Fact]
    public async Task ContainerMcp_ToolsList_ExposesExactly12Tools_WithIdenticalSchemasAndDescriptions()
    {
        var mcpDllPath = McpHostLocator.GetMcpDllPath();
        var uniqueDb = _fixture.CreateDatabaseName("rrtest_tools_schema");
        var env = _fixture.CreateProcessEnvironment(uniqueDb);

        // 1. Query host tools listing
        await using var hostClient = await McpProcessClient.StartAsync(mcpDllPath, env, forceContainer: false);
        var hostToolsJson = await hostClient.ListToolsAsync();
        var hostTools = hostToolsJson.GetProperty("tools").EnumerateArray().ToList();

        Assert.Equal(12, hostTools.Count);

        // 2. Query container tools listing
        await using var containerClient = await McpProcessClient.StartAsync(mcpDllPath, env, forceContainer: true);
        var containerToolsJson = await containerClient.ListToolsAsync();
        var containerTools = containerToolsJson.GetProperty("tools").EnumerateArray().ToList();

        // Assert exactly 12 tools in container
        Assert.Equal(12, containerTools.Count);

        var hostMap = hostTools.ToDictionary(t => t.GetProperty("name").GetString()!);
        var containerMap = containerTools.ToDictionary(t => t.GetProperty("name").GetString()!);

        // Assert all expected 12 tools are present
        foreach (var expectedName in ExpectedToolNames)
        {
            Assert.True(containerMap.ContainsKey(expectedName), $"Missing expected tool '{expectedName}' in container.");
            Assert.True(hostMap.ContainsKey(expectedName), $"Missing expected tool '{expectedName}' on host.");

            var hostTool = hostMap[expectedName];
            var containerTool = containerMap[expectedName];

            // Verify description identity
            var hostDesc = hostTool.GetProperty("description").GetString();
            var containerDesc = containerTool.GetProperty("description").GetString();
            Assert.False(string.IsNullOrWhiteSpace(containerDesc), $"Tool '{expectedName}' has empty description in container.");
            Assert.Equal(hostDesc, containerDesc);

            // Verify inputSchema identity
            var hostSchema = hostTool.GetProperty("inputSchema").GetRawText();
            var containerSchema = containerTool.GetProperty("inputSchema").GetRawText();
            Assert.Equal(hostSchema, containerSchema);
        }
    }
}
