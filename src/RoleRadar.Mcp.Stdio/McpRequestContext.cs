using RoleRadar.Application.Abstractions;

namespace RoleRadar.Mcp.Stdio;

/// <summary>
/// The MCP host's <see cref="IRequestContext"/> (feature 013, plan D6). The server resolves each tool call in its own
/// DI scope, and the call filter sets the id on that scope's instance, so every audit entry of one call shares it.
/// </summary>
internal sealed class McpRequestContext : IRequestContext
{
    public string? CorrelationId { get; set; }
}
