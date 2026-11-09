namespace RoleRadar.Mcp.Stdio.Tools;

/// <summary>
/// The audit actor for every write made through the stdio MCP host (feature 013, ADR 0041). Commands no longer default
/// their actor, so each tool names it here.
/// </summary>
internal static class McpActor
{
    internal const string Name = "mcp-stdio";
}
