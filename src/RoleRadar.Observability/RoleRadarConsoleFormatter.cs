using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace RoleRadar.Observability;

/// <summary>Output shape of <see cref="RoleRadarConsoleFormatter"/>, chosen by ROLERADAR_LOG_FORMAT.</summary>
public enum LogFormat
{
    /// <summary>One readable line per record (the default outside containers).</summary>
    Text,

    /// <summary>One JSON object per line (set by the shipped Dockerfiles).</summary>
    Json,
}

/// <summary>Which stream the console provider writes to.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Names the console output stream (stdout or stderr); the name is used across the 013 plan, tasks and ADR 0041.")]
public enum LogStream
{
    /// <summary>info and below to stdout, warn and above to stderr (API, Identity).</summary>
    Split,

    /// <summary>Everything to stderr, keeping stdout for command output and MCP frames (CLI, MCP).</summary>
    StdErr,
}

public sealed class RoleRadarFormatterOptions : ConsoleFormatterOptions
{
    /// <summary>The <c>service</c> field: web, api, identity, cli or mcp.</summary>
    public string Service { get; set; } = "app";

    public LogFormat Format { get; set; } = LogFormat.Text;
}

/// <summary>
/// The one console formatter every RoleRadar .NET host uses (ADR 0041). Writes the record shape in
/// specs/013-operability/contracts/log-records.md, always on one line, and passes every field through
/// <see cref="LogScrubber"/>.
/// </summary>
public sealed class RoleRadarConsoleFormatter : ConsoleFormatter
{
    public const string FormatterName = "roleradar";

    // ASP.NET Core's hosting scope adds these to every request; they are noise next to correlationId.
    private static readonly HashSet<string> IgnoredScopeKeys = new(StringComparer.Ordinal)
    {
        "{OriginalFormat}", "RequestId", "RequestPath", "ConnectionId", "SpanId", "TraceId", "ParentId",
    };

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    private readonly Func<RoleRadarFormatterOptions> _options;

    public RoleRadarConsoleFormatter(IOptionsMonitor<RoleRadarFormatterOptions> options)
        : base(FormatterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = () => options.CurrentValue;
    }

    private RoleRadarConsoleFormatter(RoleRadarFormatterOptions options)
        : base(FormatterName)
    {
        _options = () => options;
    }

    /// <summary>A formatter with fixed options, for tests and tools that do not use the options system.</summary>
    public static RoleRadarConsoleFormatter Create(string service, LogFormat format) =>
        new(new RoleRadarFormatterOptions { Service = service, Format = format });

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        var options = _options();

        var message = logEntry.Formatter is null
            ? Convert.ToString(logEntry.State, CultureInfo.InvariantCulture) ?? string.Empty
            : logEntry.Formatter(logEntry.State, logEntry.Exception);

        string? correlationId = null;
        var props = new List<KeyValuePair<string, object?>>();

        scopeProvider?.ForEachScope((scope, _) => Collect(scope, props, ref correlationId), (object?)null);
        if (logEntry.State is IEnumerable<KeyValuePair<string, object?>> stateValues)
        {
            foreach (var pair in stateValues)
            {
                if (!IgnoredScopeKeys.Contains(pair.Key))
                {
                    props.Add(pair);
                }
            }
        }

        var record = new Record(
            DateTimeOffset.UtcNow,
            LevelName(logEntry.LogLevel),
            options.Service,
            logEntry.Category,
            LogScrubber.ScrubText(RedactSensitiveTemplateValues(message, logEntry.State)),
            logEntry.EventId,
            correlationId,
            Deduplicate(props),
            logEntry.Exception);

        var line = options.Format == LogFormat.Json ? ToJson(record) : ToText(record);
        textWriter.Write(line);
        textWriter.Write('\n');
    }

    /// <summary>
    /// A value logged under a sensitive template name (for example <c>{Password}</c>) is redacted in the props, but the
    /// rendered message already contains it. Replace those values in the message too. Very short values are left to
    /// the text rules, so a one-character value cannot mangle the message.
    /// </summary>
    private static string RedactSensitiveTemplateValues<TState>(string message, TState state)
    {
        if (state is not IEnumerable<KeyValuePair<string, object?>> pairs || message.Length == 0)
        {
            return message;
        }

        var result = message;
        foreach (var (key, value) in pairs)
        {
            if (!LogScrubber.IsSensitiveKey(key))
            {
                continue;
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(text) && text.Length >= 4)
            {
                result = result.Replace(text, LogScrubber.Redacted, StringComparison.Ordinal);
            }
        }

        return result;
    }

    private static void Collect(object? scope, List<KeyValuePair<string, object?>> props, ref string? correlationId)
    {
        if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            foreach (var pair in pairs)
            {
                if (string.Equals(pair.Key, CorrelationId.ScopeKey, StringComparison.Ordinal))
                {
                    correlationId = Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                }
                else if (!IgnoredScopeKeys.Contains(pair.Key))
                {
                    props.Add(pair);
                }
            }
        }
    }

    private static List<KeyValuePair<string, string?>> Deduplicate(List<KeyValuePair<string, object?>> props)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<KeyValuePair<string, string?>>();
        foreach (var (key, value) in props)
        {
            var scrubbed = value is null ? null : LogScrubber.ScrubProperty(key, value);
            if (seen.TryGetValue(key, out var index))
            {
                result[index] = new(key, scrubbed);
            }
            else
            {
                seen[key] = result.Count;
                result.Add(new(key, scrubbed));
            }
        }

        return result;
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "trace",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "fatal",
        _ => "none",
    };

    private static string ToJson(Record record)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("ts", record.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("level", record.Level);
            writer.WriteString("service", record.Service);
            writer.WriteString("category", record.Category);
            writer.WriteString("msg", record.Message);
            if (!string.IsNullOrEmpty(record.EventId.Name))
            {
                writer.WriteString("event", record.EventId.Name);
            }

            if (record.EventId.Id != 0)
            {
                writer.WriteNumber("eventId", record.EventId.Id);
            }

            if (!string.IsNullOrEmpty(record.CorrelationId))
            {
                writer.WriteString("correlationId", record.CorrelationId);
            }

            if (record.Props.Count > 0)
            {
                writer.WriteStartObject("props");
                foreach (var (key, value) in record.Props)
                {
                    WriteValue(writer, key, value);
                }

                writer.WriteEndObject();
            }

            if (record.Exception is { } exception)
            {
                writer.WriteStartObject("error");
                writer.WriteString("type", exception.GetType().FullName);
                writer.WriteString("message", LogScrubber.ScrubText(exception.Message));
                writer.WriteString("stack", LogScrubber.ScrubText(exception.ToString()));
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, string key, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(key);
        }
        else if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole) && whole.ToString(CultureInfo.InvariantCulture) == value)
        {
            writer.WriteNumber(key, whole);
        }
        else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) && value.Contains('.', StringComparison.Ordinal) && !value.EndsWith('.'))
        {
            writer.WriteNumber(key, real);
        }
        else if (value is "True" or "False")
        {
            writer.WriteBoolean(key, value == "True");
        }
        else
        {
            writer.WriteString(key, value);
        }
    }

    private static string ToText(Record record)
    {
        var sb = new StringBuilder();
        sb.Append(record.Timestamp.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            .Append(' ').Append(record.Level)
            .Append(' ').Append(record.Service)
            .Append(" [").Append(string.IsNullOrEmpty(record.CorrelationId) ? "-" : record.CorrelationId).Append("] ")
            .Append(OneLine(record.Message));

        foreach (var (key, value) in record.Props)
        {
            sb.Append(' ').Append(key).Append('=').Append(OneLine(value ?? "null"));
        }

        if (record.Exception is { } exception)
        {
            sb.Append(" error=").Append(exception.GetType().FullName).Append(": ").Append(OneLine(LogScrubber.ScrubText(exception.Message)));
        }

        return sb.ToString();
    }

    private static string OneLine(string value) =>
        value.Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal);

    private sealed record Record(
        DateTimeOffset Timestamp,
        string Level,
        string Service,
        string Category,
        string Message,
        EventId EventId,
        string? CorrelationId,
        IReadOnlyList<KeyValuePair<string, string?>> Props,
        Exception? Exception);
}
