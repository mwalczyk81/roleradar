using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace RoleRadar.Tests.Architecture;

/// <summary>
/// Feature 013 (ADR 0041): RoleRadar.Observability is shared by all four hosts, so it must stay dependency-free,
/// and the inner layers must never reach it.
/// </summary>
public sealed class ObservabilityArchitectureTests
{
    private static readonly Assembly ObservabilityAssembly = typeof(RoleRadar.Observability.TraceIds).Assembly;
    private static readonly Assembly DomainAssembly = typeof(RoleRadar.Domain.Common.OwnerId).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(RoleRadar.Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(RoleRadar.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly IdentityAssembly = typeof(RoleRadar.Identity.Program).Assembly;
    private static readonly Assembly ApiAssembly = typeof(RoleRadar.Api.Program).Assembly;

    private static string[] ReferencedNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToArray();

    [Fact]
    public void Observability_HasPublicTypes_SoTheRulesBelowAreNotVacuous()
    {
        Assert.NotEmpty(ObservabilityAssembly.GetExportedTypes());
    }

    [Fact]
    public void Observability_ReferencesNoRoleRadarAssembly()
    {
        var offenders = ReferencedNames(ObservabilityAssembly)
            .Where(n => n.StartsWith("RoleRadar.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0, $"RoleRadar.Observability must not reference: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Observability_ReferencesNoAspNetMongoOrDuende()
    {
        var offenders = ReferencedNames(ObservabilityAssembly)
            .Where(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                || n.StartsWith("MongoDB.", StringComparison.Ordinal)
                || n.StartsWith("Duende", StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0, $"RoleRadar.Observability must not reference: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void InnerLayers_DoNotReferenceObservability()
    {
        var offenders = new[] { DomainAssembly, ApplicationAssembly, InfrastructureAssembly }
            .Where(a => ReferencedNames(a).Contains("RoleRadar.Observability", StringComparer.Ordinal))
            .Select(a => a.GetName().Name)
            .ToArray();

        Assert.True(offenders.Length == 0, $"These layers must not reference RoleRadar.Observability: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Identity_DoesNotReferenceMongo_SoSignInEventsStayLogsOnly()
    {
        var offenders = ReferencedNames(IdentityAssembly)
            .Where(n => n.StartsWith("MongoDB.", StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0, $"RoleRadar.Identity must not reference: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void ExactlyOneMeterNamedRoleRadarStore_ExistsAcrossSolution()
    {
        var assemblies = new[]
        {
            DomainAssembly,
            ApplicationAssembly,
            InfrastructureAssembly,
            ObservabilityAssembly,
            IdentityAssembly,
            ApiAssembly
        };

        var typesWithStoreMeter = new List<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                foreach (var field in fields)
                {
                    if (field.FieldType == typeof(System.Diagnostics.Metrics.Meter))
                    {
                        if (!field.IsStatic)
                        {
                            continue;
                        }

                        var meter = field.GetValue(null) as System.Diagnostics.Metrics.Meter;
                        if (meter != null && string.Equals(meter.Name, "RoleRadar.Store", StringComparison.Ordinal))
                        {
                            typesWithStoreMeter.Add($"{type.FullName}.{field.Name}");
                        }
                    }
                }
            }
        }

        Assert.True(typesWithStoreMeter.Count == 1,
            $"Expected exactly one Meter named 'RoleRadar.Store', but found {typesWithStoreMeter.Count}: {string.Join(", ", typesWithStoreMeter)}");

        // Also fail on any source file under src/ that constructs a Meter named "RoleRadar.Store" outside RoleRadarStoreMetrics.cs
        var repoRoot = LocateRepositoryRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        var csFiles = Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories);

        var offendingSourceFiles = new List<string>();
        foreach (var file in csFiles)
        {
            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, "RoleRadarStoreMetrics.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            if (Regex.IsMatch(text, @"\bnew\s+(?:System\.Diagnostics\.Metrics\.)?Meter\s*\([^)]*?(?:""RoleRadar\.Store""|RoleRadarStoreMetrics\.MeterName|StoreMeterName)", RegexOptions.CultureInvariant))
            {
                offendingSourceFiles.Add(Path.GetRelativePath(repoRoot, file));
            }
        }

        Assert.True(
            offendingSourceFiles.Count == 0,
            $"Found unauthorized Meter(\"RoleRadar.Store\") construction in source files:\n{string.Join("\n", offendingSourceFiles)}");
    }

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")) &&
                Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing RoleRadar.sln and src directory.");
    }

    // ---- Gate 5 (feature 015, ADR 0049): the AppHost is a thin orchestrator that talks to the hosts only as processes ----

    [Fact]
    public void AppHost_ReferencesNoInnerLayerOrSharedObservabilityProject()
    {
        var csproj = Path.Combine(ObservabilityRepoRoot(), "src", "RoleRadar.AppHost", "RoleRadar.AppHost.csproj");
        Assert.True(File.Exists(csproj), $"Expected {csproj} (Gate 5).");

        var text = File.ReadAllText(csproj);
        foreach (var forbidden in new[] { "RoleRadar.Domain", "RoleRadar.Application", "RoleRadar.Infrastructure", "RoleRadar.Observability", "RoleRadar.Cli", "RoleRadar.Mcp.Stdio", "MongoDB.Driver" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppHost_ExportsOtlpOverLoopbackOnly_AndHttpProtobufForTheWebApp()
    {
        var appHostDir = Path.Combine(ObservabilityRepoRoot(), "src", "RoleRadar.AppHost");
        var program = Path.Combine(appHostDir, "Program.cs");
        var launch = Path.Combine(appHostDir, "Properties", "launchSettings.json");
        Assert.True(File.Exists(program) && File.Exists(launch), "Expected AppHost Program.cs and Properties/launchSettings.json (Gate 5).");

        // The web app uses the OTLP HTTP exporters, so it must be pointed at the dashboard's HTTP receiver.
        Assert.Contains("OtlpProtocol.HttpProtobuf", File.ReadAllText(program), StringComparison.Ordinal);

        var launchText = File.ReadAllText(launch);
        Assert.Contains("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", launchText, StringComparison.Ordinal);
        Assert.Contains("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL", launchText, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", launchText, StringComparison.Ordinal);
    }

    private static string ObservabilityRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
