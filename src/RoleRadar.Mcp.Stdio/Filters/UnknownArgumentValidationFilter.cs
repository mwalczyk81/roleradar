using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoleRadar.Mcp.Stdio.Tools;

namespace RoleRadar.Mcp.Stdio.Filters;

/// <summary>
/// Intercepts tool calls and rejects unknown arguments with a ValidationError envelope,
/// preserving schema contract integrity and preventing silent parameter discard.
/// </summary>
public static class UnknownArgumentValidationFilter
{
    private static readonly Dictionary<string, HashSet<string>> KnownToolParameters = BuildKnownToolParameters();

    public static McpRequestFilter<CallToolRequestParams, CallToolResult> Create()
    {
        return next => async (context, cancellationToken) =>
        {
            var toolName = context.Params.Name;
            var args = context.Params.Arguments;

            if (args != null && args.Count > 0 && KnownToolParameters.TryGetValue(toolName, out var validParams))
            {
                var unknownArgs = args.Keys
                    .Where(k => !validParams.Contains(k))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToList();

                if (unknownArgs.Count > 0)
                {
                    var errorMessage = $"Unknown argument(s): {string.Join(", ", unknownArgs.Select(a => $"'{a}'"))}.";
                    var jsonError = JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = new
                        {
                            code = "ValidationError",
                            message = errorMessage
                        }
                    });

                    return new CallToolResult
                    {
                        Content = [new TextContentBlock { Text = jsonError }]
                    };
                }
            }

            return await next(context, cancellationToken);
        };
    }

    private static Dictionary<string, HashSet<string>> BuildKnownToolParameters()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var toolTypes = new[] { typeof(NoteMcpTools), typeof(TrackingMcpTools) };

        foreach (var type in toolTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (attr == null)
                {
                    continue;
                }

                var toolName = attr.Name ?? method.Name;
                var paramNames = method.GetParameters()
                    .Where(p => p.ParameterType != typeof(CancellationToken))
                    .Select(p => p.Name!)
                    .ToHashSet(StringComparer.Ordinal);

                map[toolName] = paramNames;
            }
        }

        return map;
    }
}
