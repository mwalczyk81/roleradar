using RoleRadar.Application.Abstractions;

namespace RoleRadar.Cli;

/// <summary>
/// The CLI's <see cref="IRequestContext"/> (feature 013, plan D6): one id per process run, the same id that
/// <see cref="Program.RunId"/> puts in the logging scope.
/// </summary>
internal sealed class RunRequestContext : IRequestContext
{
    public string? CorrelationId => Program.RunId;
}
