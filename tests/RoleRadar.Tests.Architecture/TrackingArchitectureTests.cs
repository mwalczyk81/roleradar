using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class TrackingArchitectureTests
{
    [Fact]
    public void McpToolSurface_TotalToolsMustNotExceedCeilingOf12()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var toolMethods = mcpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToArray();

        Assert.True(
            toolMethods.Length <= 12,
            $"Total MCP tools count is {toolMethods.Length}, which exceeds the mandatory architecture ceiling of <= 12 tools.");
    }

    [Fact]
    public void TrackingRepositories_MustNotExpose_DeleteOrRemoveMethods()
    {
        var trackingRepoInterfaces = new[]
        {
            typeof(ICompanyRepository),
            typeof(IPostingRepository),
            typeof(ICriterionRepository),
            typeof(IApplicationRepository),
            typeof(IContactRepository),
            typeof(ISearchRunRepository),
            typeof(ITrackingTransactionContext)
        };

        foreach (var repoType in trackingRepoInterfaces)
        {
            var methods = repoType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                Assert.False(
                    m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Contains("Drop", StringComparison.OrdinalIgnoreCase),
                    $"Tracking repository interface '{repoType.Name}' must not expose delete method '{m.Name}'.");
            }
        }
    }

    [Fact]
    public void McpTools_MustZeroExpose_ContactTypesOrParameters()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var mcpTypes = mcpAssembly.GetTypes();

        foreach (var t in mcpTypes)
        {
            Assert.DoesNotContain("Contact", t.Name, StringComparison.OrdinalIgnoreCase);

            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var m in methods)
            {
                if (m.GetCustomAttribute<McpServerToolAttribute>() is not null)
                {
                    Assert.DoesNotContain("contact", m.Name, StringComparison.OrdinalIgnoreCase);
                    var parameters = m.GetParameters();
                    foreach (var p in parameters)
                    {
                        Assert.False(
                            p.ParameterType.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase) &&
                            !p.ParameterType.Name.Contains("Context", StringComparison.OrdinalIgnoreCase),
                            $"MCP tool '{m.Name}' must not accept contact parameter '{p.Name}'.");
                    }
                }
            }
        }
    }

    [Fact]
    public void Infrastructure_AddRoleRadarInfrastructure_RegistersICompanyRepository()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = "mongodb://127.0.0.1:27017/?replicaSet=rs0";
            opts.DatabaseName = "roleradar_arch_test";
        });

        using var provider = services.BuildServiceProvider();
        var companyRepo = provider.GetService<ICompanyRepository>();
        var postingRepo = provider.GetService<IPostingRepository>();
        var criterionRepo = provider.GetService<ICriterionRepository>();
        var applicationRepo = provider.GetService<IApplicationRepository>();
        var contactRepo = provider.GetService<IContactRepository>();
        var searchRunRepo = provider.GetService<ISearchRunRepository>();
        var integrityRepo = provider.GetService<RoleRadar.Application.Integrity.IIntegrityRepository>();
        Assert.NotNull(companyRepo);
        Assert.NotNull(postingRepo);
        Assert.NotNull(criterionRepo);
        Assert.NotNull(applicationRepo);
        Assert.NotNull(contactRepo);
        Assert.NotNull(searchRunRepo);
        Assert.NotNull(integrityRepo);
    }

    [Fact]
    public void McpTools_PostingTools_AreExposedOnServer()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var toolNames = mcpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.Contains("company_record", toolNames);
        Assert.Contains("posting_record", toolNames);
        Assert.Contains("posting_transition", toolNames);
        Assert.Contains("tracker_query", toolNames);
        Assert.Contains("tracker_log", toolNames);
        Assert.Contains("application_record", toolNames);
    }
}
