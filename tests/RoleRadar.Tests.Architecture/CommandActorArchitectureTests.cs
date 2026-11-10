using System.Reflection;
using RoleRadar.Application.Abstractions;
using Xunit;

namespace RoleRadar.Tests.Architecture;

/// <summary>
/// Feature 013 (ADR 0041, research R-010): a command must name its actor. A default such as "mcp-stdio" or "system"
/// lets a caller write an audit entry that blames the wrong party, so no command may declare one.
/// </summary>
public sealed class CommandActorArchitectureTests
{
    private const int MinimumActorCommands = 5;

    private static readonly Assembly ApplicationAssembly = typeof(RoleRadar.Application.DependencyInjection).Assembly;

    private static IEnumerable<(Type Command, ParameterInfo Actor)> ActorParameters() =>
        ApplicationAssembly.GetExportedTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .SelectMany(t => t.GetConstructors().Select(c => (Type: t, Ctor: c)))
            .SelectMany(x => x.Ctor.GetParameters()
                .Where(p => string.Equals(p.Name, "Actor", StringComparison.Ordinal))
                .Select(p => (x.Type, p)));

    [Fact]
    public void ActorParameterSentinel_FindsEnoughCommands_SoTheRuleIsNotVacuous()
    {
        var commands = ActorParameters().Select(a => a.Command).Distinct().ToArray();

        Assert.True(
            commands.Length >= MinimumActorCommands,
            $"Expected at least {MinimumActorCommands} commands with an Actor parameter, found {commands.Length}.");
    }

    [Fact]
    public void NoCommandActorParameter_HasADefaultValue()
    {
        var offenders = ActorParameters()
            .Where(a => a.Actor.HasDefaultValue)
            .Select(a => $"{a.Command.Name} (default: {a.Actor.DefaultValue ?? "null"})")
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Every command must name its actor explicitly. These declare a default: {string.Join(", ", offenders)}");
    }
}
