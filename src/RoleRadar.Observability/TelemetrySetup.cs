using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RoleRadar.Observability;

/// <summary>
/// Shared OpenTelemetry registration for RoleRadar .NET hosts (Feature 015, FR-001 to FR-005, FR-015, FR-017, SC-003).
/// Configures resource identity, activity sources, <see cref="RoleRadarSpanAttributeProcessor"/>, and conditional
/// OTLP export when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is non-empty.
/// </summary>
public static class TelemetrySetup
{
    public const string ServiceNamespace = "roleradar";
    public const int DefaultFlushTimeoutMs = 2000;
    public const int MinFlushTimeoutMs = 100;
    public const int MaxFlushTimeoutMs = 5000;
    public const string FlushTimeoutEnvVar = "ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS";
    public const string FlushTimeoutVariable = FlushTimeoutEnvVar;
    public const string OtlpEndpointEnvVar = "OTEL_EXPORTER_OTLP_ENDPOINT";
    public const string OtlpEndpointVariable = OtlpEndpointEnvVar;
    public const string OtlpServiceNameEnvVar = "OTEL_SERVICE_NAME";
    public const string ServiceNameVariable = OtlpServiceNameEnvVar;

    private static readonly string DefaultServiceVersion =
        typeof(TelemetrySetup).Assembly.GetName().Version?.ToString() ?? "0.1.0";

    public static IServiceCollection AddRoleRadarTelemetry(
        this IServiceCollection services,
        string defaultServiceName,
        IConfiguration? configuration = null,
        Action<TracerProviderBuilder>? configureTracing = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultServiceName);

        var resolvedServiceName = ResolveServiceName(defaultServiceName, configuration);
        var endpoint = ResolveOtlpEndpoint(configuration);
        var flushTimeoutMs = ResolveFlushTimeoutMs(configuration);

        ValidateActivityEventsField(RoleRadarSpanAttributeProcessor.ActivityEventsField);

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            services.Configure<OtlpExporterOptions>(otlp =>
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

        services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.Clear();
                resource.AddService(
                    serviceName: resolvedServiceName,
                    serviceNamespace: ServiceNamespace,
                    serviceVersion: DefaultServiceVersion,
                    autoGenerateServiceInstanceId: false);
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new AlwaysOnSampler())
                    .AddSource(
                        TraceIds.ActivitySourceName,
                        "System.Net.Http",
                        "MongoDB.Driver")
                    .AddProcessor(new RoleRadarSpanAttributeProcessor());

                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    tracing.AddOtlpExporter(otlp =>
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

                configureTracing?.Invoke(tracing);
            });

        return services;
    }

    public static bool IsOtlpEndpointConfigured(IConfiguration? configuration = null) =>
        IsOtlpEndpointConfigured(ResolveOtlpEndpoint(configuration));

    public static bool IsOtlpEndpointConfigured(string? rawEndpoint) =>
        !string.IsNullOrWhiteSpace(rawEndpoint);

    public static int ResolveFlushTimeoutMs(IConfiguration? configuration = null)
    {
        var raw = configuration?[FlushTimeoutEnvVar] ?? Environment.GetEnvironmentVariable(FlushTimeoutEnvVar);
        return ResolveFlushTimeoutMs(raw);
    }

    public static int ResolveFlushTimeoutMs(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out var parsed))
        {
            return Math.Clamp(parsed, MinFlushTimeoutMs, MaxFlushTimeoutMs);
        }

        return DefaultFlushTimeoutMs;
    }

    public static string ResolveServiceName(string defaultServiceName, IConfiguration? configuration = null)
    {
        var overrideName = configuration?[OtlpServiceNameEnvVar] ?? Environment.GetEnvironmentVariable(OtlpServiceNameEnvVar);
        return ResolveServiceName(defaultServiceName, overrideName);
    }

    public static string ResolveServiceName(string defaultServiceName, string? configuredServiceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultServiceName);

        return !string.IsNullOrWhiteSpace(configuredServiceName) ? configuredServiceName.Trim() : defaultServiceName.Trim();
    }

    public static bool FlushRoleRadarTelemetryWithBoundedTimeout(
        IServiceProvider services,
        IConfiguration? configuration = null,
        int? timeoutMs = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        TracerProvider? tracerProvider;
        try
        {
            tracerProvider = services.GetService<TracerProvider>();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        if (tracerProvider is null)
        {
            return false;
        }

        var effectiveTimeout = timeoutMs.HasValue
            ? Math.Clamp(timeoutMs.Value, MinFlushTimeoutMs, MaxFlushTimeoutMs)
            : ResolveFlushTimeoutMs(configuration);

        try
        {
            return tracerProvider.ForceFlush(effectiveTimeout);
        }
        catch
        {
            return false;
        }
    }

    public static void ValidateActivityEventsField(System.Reflection.FieldInfo? field)
    {
        if (field is null)
        {
            throw new InvalidOperationException(
                "System.Diagnostics.Activity._events private field could not be resolved. " +
                "RoleRadar telemetry privacy gate requires Activity._events reflection to sanitize span exception events.");
        }
    }

    private static string? ResolveOtlpEndpoint(IConfiguration? configuration)
    {
        var fromConfig = configuration?[OtlpEndpointEnvVar];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return fromConfig.Trim();
        }

        var fromEnv = Environment.GetEnvironmentVariable(OtlpEndpointEnvVar);
        return !string.IsNullOrWhiteSpace(fromEnv) ? fromEnv.Trim() : null;
    }
}
