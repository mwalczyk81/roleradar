using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

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
        ("Microsoft.AspNetCore.HttpLogging", LogLevel.Information),
        ("Duende.IdentityServer.Validation.AuthorizeRequestValidator", LogLevel.Warning),
        ("Duende.IdentityServer.Endpoints.AuthorizeEndpoint", LogLevel.Warning),
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
        IConfiguration? configuration = null,
        Action<OpenTelemetryLoggerOptions>? configureLogging = null)
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
            var hasConfiguration = !string.IsNullOrEmpty(configuration?[$"Logging:LogLevel:{category}"])
                || !string.IsNullOrEmpty(configuration?[$"Logging:LogLevel:{category.Replace('.', ':')}"])
                || !string.IsNullOrEmpty(configured?[category]);

            if (!hasConfiguration)
            {
                builder.AddFilter(category, level);
            }
        }

        var endpoint = configuration?[TelemetrySetup.OtlpEndpointEnvVar]
            ?? Environment.GetEnvironmentVariable(TelemetrySetup.OtlpEndpointEnvVar);

        var isConfigured = !string.IsNullOrWhiteSpace(endpoint);
        if (isConfigured || configureLogging != null)
        {
            var defaultServiceName = service.StartsWith("roleradar-", StringComparison.OrdinalIgnoreCase)
                ? service
                : $"roleradar-{service}";
            var resolvedServiceName = TelemetrySetup.ResolveServiceName(defaultServiceName, configuration);
            var flushTimeoutMs = TelemetrySetup.ResolveFlushTimeoutMs(configuration);

            builder.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = false;
                logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                    serviceName: resolvedServiceName,
                    serviceNamespace: TelemetrySetup.ServiceNamespace,
                    serviceVersion: typeof(TelemetrySetup).Assembly.GetName().Version?.ToString() ?? "0.1.0",
                    autoGenerateServiceInstanceId: false));

                logging.AddProcessor(new RoleRadarLogScrubbingProcessor());

                if (isConfigured)
                {
                    logging.AddOtlpExporter(otlp =>
                    {
                        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
                        {
                            otlp.Endpoint = endpointUri;
                        }

                        otlp.TimeoutMilliseconds = flushTimeoutMs;
                        otlp.BatchExportProcessorOptions.ExporterTimeoutMilliseconds = flushTimeoutMs;
                        otlp.BatchExportProcessorOptions.ScheduledDelayMilliseconds = Math.Min(1000, flushTimeoutMs);
                    });
                }

                configureLogging?.Invoke(logging);
            });
        }

        return builder;
    }
}
