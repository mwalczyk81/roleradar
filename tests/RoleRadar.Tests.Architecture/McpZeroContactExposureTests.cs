using System.Reflection;
using ModelContextProtocol.Server;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class McpZeroContactExposureTests
{
    [Fact]
    public void McpToolSurface_ZeroContactExposureAcrossAllToolsParametersAndDescriptions()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var types = mcpAssembly.GetTypes();

        foreach (var type in types)
        {
            Assert.DoesNotContain("Contact", type.Name, StringComparison.OrdinalIgnoreCase);

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
                if (toolAttr is not null)
                {
                    // Tool name must not reference contact
                    Assert.DoesNotContain("contact", method.Name, StringComparison.OrdinalIgnoreCase);
                    Assert.DoesNotContain("contact", toolAttr.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);

                    // Parameters: only referralContactId on application_record is allowed
                    foreach (var param in method.GetParameters())
                    {
                        if (param.Name == "referralContactId")
                        {
                            Assert.Equal("application_record", toolAttr.Name ?? method.Name);
                            continue;
                        }

                        Assert.DoesNotContain("contact", param.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                        Assert.False(
                            param.ParameterType.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase) &&
                            !param.ParameterType.Name.Contains("Context", StringComparison.OrdinalIgnoreCase),
                            $"MCP tool '{toolAttr.Name ?? method.Name}' must not accept contact type parameter '{param.Name}'.");
                    }
                }
            }
        }
    }
}
