using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using NetArchTest.Rules;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class ContainerReadinessArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Note).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(INoteRepository).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(MongoNoteRepository).Assembly;
    private static readonly Assembly McpAssembly = typeof(NoteMcpTools).Assembly;

    [Fact]
    public void Domain_And_Application_MustNotDependOn_ContainerHostingOrDockerTypes()
    {
        var domainResult = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Docker",
                "Container",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.Hosting.Abstractions")
            .GetResult();

        Assert.True(
            domainResult.IsSuccessful,
            $"Domain depends on container or hosting types: {string.Join(", ", domainResult.FailingTypeNames ?? [])}");

        var appResult = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Docker",
                "Container",
                "Microsoft.Extensions.Hosting",
                "Microsoft.Extensions.Hosting.Abstractions")
            .GetResult();

        Assert.True(
            appResult.IsSuccessful,
            $"Application depends on container or hosting types: {string.Join(", ", appResult.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void McpToolSurface_TotalToolsMustBeExactly12()
    {
        var toolMethods = McpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToArray();

        Assert.Equal(12, toolMethods.Length);
    }

    [Fact]
    public void InwardLayering_MustBeStrictlyPreserved()
    {
        var domainResult = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RoleRadar.Application",
                "RoleRadar.Infrastructure",
                "RoleRadar.Mcp.Stdio",
                "RoleRadar.Cli")
            .GetResult();

        Assert.True(
            domainResult.IsSuccessful,
            $"Domain inward layering violation: {string.Join(", ", domainResult.FailingTypeNames ?? [])}");

        var appResult = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RoleRadar.Infrastructure",
                "RoleRadar.Mcp.Stdio",
                "RoleRadar.Cli")
            .GetResult();

        Assert.True(
            appResult.IsSuccessful,
            $"Application inward layering violation: {string.Join(", ", appResult.FailingTypeNames ?? [])}");

        var infraResult = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RoleRadar.Mcp.Stdio",
                "RoleRadar.Cli")
            .GetResult();

        Assert.True(
            infraResult.IsSuccessful,
            $"Infrastructure inward layering violation: {string.Join(", ", infraResult.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void SourceCode_MustNotUse_BannedDateTimeApis()
    {
        var repoRoot = FindRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"Source directory not found at: {srcDir}");

        var csFiles = Directory.GetFiles(srcDir, "*.cs", SearchOption.AllDirectories);
        var bannedPattern = new Regex(@"\b(DateTime\.Now|DateTime\.Today|TimeZoneInfo\.Local)\b|\.ToLocalTime\s*\(", RegexOptions.Compiled);

        var violations = new List<string>();

        foreach (var file in csFiles)
        {
            var relativePath = Path.GetRelativePath(repoRoot, file);
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var match = bannedPattern.Match(line);
                if (match.Success)
                {
                    // Ignore comments
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*'))
                    {
                        continue;
                    }

                    violations.Add($"{relativePath}:{i + 1} uses banned API '{match.Value}' -> {trimmed}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Detected banned non-UTC or local DateTime APIs in src/:\n{string.Join("\n", violations)}");
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "RoleRadar.sln")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            current = parent.FullName;
        }

        throw new InvalidOperationException("Could not find repository root containing RoleRadar.sln");
    }
}
