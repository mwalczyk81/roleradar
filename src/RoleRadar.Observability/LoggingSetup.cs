using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace RoleRadar.Observability;

/// <summary>
/// The one logging setup for every RoleRadar .NET host (ADR 0041). The format comes only from
/// <see cref="FormatVariable"/>; the setup never branches on an environment name, so DemoEnvironmentParityTests stays
/// green.
/// </summary>
public static class LoggingSetup
{
    public const string FormatVariable = "ROLERADAR_LOG_FORMAT";

    private static readonly (string Category, LogLevel Level)[] DefaultLevels =
    [
        ("Microsoft", LogLevel.Warning),
        ("System", LogLevel.Warning),
        ("Microsoft.Hosting.Lifetime", LogLevel.Information),
    ];

    /// <summary><c>json</c> (any case, trimmed) gives JSON; anything else, including unset, gives text.</summary>
    public static LogFormat ResolveFormat(string? value) =>
        string.Equals(value?.Trim(), "json", StringComparison.OrdinalIgnoreCase) ? LogFormat.Json : LogFormat.Text;

    /// <summary>
    /// Replaces the console output with <see cref="RoleRadarConsoleFormatter"/>. Callers clear other providers first
    /// (<c>ClearProviders()</c>) and, in hosts with a test hook, must call this before the hook runs so test capture
    /// providers are kept. Default levels apply only to categories the configuration does not set, so
    /// <c>Logging__LogLevel__*</c> overrides them.
    /// </summary>
    public static ILoggingBuilder AddRoleRadarLogging(
        this ILoggingBuilder builder,
        string service,
        LogStream stream,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        var format = ResolveFormat(Environment.GetEnvironmentVariable(FormatVariable));

        builder.AddConsole(options =>
        {
            options.FormatterName = RoleRadarConsoleFormatter.FormatterName;
            options.LogToStandardErrorThreshold = stream == LogStream.StdErr ? LogLevel.Trace : LogLevel.Warning;
        });
        builder.AddConsoleFormatter<RoleRadarConsoleFormatter, RoleRadarFormatterOptions>(options =>
        {
            options.Service = service;
            options.Format = format;
        });

        var configured = configuration?.GetSection("Logging:LogLevel");
        foreach (var (category, level) in DefaultLevels)
        {
            if (string.IsNullOrEmpty(configured?[category]))
            {
                builder.AddFilter(category, level);
            }
        }

        return builder;
    }
}
