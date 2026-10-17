using System.Reflection;
using ModelContextProtocol.Server;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class McpToolSurfaceArchitectureTests
{
    private static readonly string[] ExpectedToolNames =
    [
        "note_create",
        "note_get",
        "note_append",
        "note_replace_section",
        "note_search",
        "note_archive"
    ];

    [Fact]
    public void NoteMcpTools_ExposesAtMost8Tools_Exactly6Tools_AndZeroDeleteTools()
    {
        var toolType = typeof(NoteMcpTools);

        // Find all methods decorated with [McpServerTool]
        var toolMethods = toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => (Method: m, Attribute: m.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(x => x.Attribute is not null)
            .Select(x => (MethodName: x.Method.Name, ToolName: x.Attribute!.Name ?? x.Method.Name))
            .ToArray();

        // 1. Tool ceiling check (<= 8 tools)
        Assert.True(
            toolMethods.Length <= 8,
            $"NoteMcpTools exposes {toolMethods.Length} tools, which exceeds the mandatory architecture ceiling of at most 8 tools.");

        // 2. Exact 6 tools check
        Assert.Equal(
            ExpectedToolNames.Length,
            toolMethods.Length);

        var actualToolNames = toolMethods.Select(t => t.ToolName).OrderBy(n => n).ToArray();
        var sortedExpected = ExpectedToolNames.OrderBy(n => n).ToArray();
        Assert.Equal(sortedExpected, actualToolNames);

        // 3. Zero delete tools check
        foreach (var (methodName, toolName) in toolMethods)
        {
            Assert.False(
                toolName.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                toolName.Contains("remove", StringComparison.OrdinalIgnoreCase) ||
                toolName.Contains("drop", StringComparison.OrdinalIgnoreCase) ||
                methodName.Contains("delete", StringComparison.OrdinalIgnoreCase) ||
                methodName.Contains("remove", StringComparison.OrdinalIgnoreCase),
                $"Tool '{toolName}' ({methodName}) violates the append-only/archive-only architecture: zero delete tools allowed.");
        }
    }
}
