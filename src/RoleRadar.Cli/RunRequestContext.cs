using RoleRadar.Application.Abstractions;

namespace RoleRadar.Cli;

/// <summary>
/// The CLI's <see cref="IRequestContext"/> (feature 013, plan D6; feature 014): one trace id per process run, the same id the
/// run's activity puts in every log record (<see cref="Program.RunId"/>).
/// </summary>
internal sealed class RunRequestContext : IRequestContext
{
    public string? CorrelationId => Program.RunId;
}
