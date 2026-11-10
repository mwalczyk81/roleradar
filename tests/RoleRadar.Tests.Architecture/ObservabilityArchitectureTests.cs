using System.Reflection;
using Xunit;

namespace RoleRadar.Tests.Architecture;

/// <summary>
/// Feature 013 (ADR 0041): RoleRadar.Observability is shared by all four hosts, so it must stay dependency-free,
/// and the inner layers must never reach it.
/// </summary>
public sealed class ObservabilityArchitectureTests
{
    private static readonly Assembly ObservabilityAssembly = typeof(RoleRadar.Observability.CorrelationId).Assembly;
    private static readonly Assembly DomainAssembly = typeof(RoleRadar.Domain.Common.OwnerId).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(RoleRadar.Application.DependencyInjection).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(RoleRadar.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly IdentityAssembly = typeof(RoleRadar.Identity.Program).Assembly;

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
}
