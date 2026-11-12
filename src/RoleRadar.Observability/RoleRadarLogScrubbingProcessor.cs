using OpenTelemetry;
using OpenTelemetry.Logs;

namespace RoleRadar.Observability;

/// <summary>
/// Gate 3 processor (ADR 0051, RR-86, RR-72) that sanitizes <see cref="LogRecord"/> instances
/// before they are exported over OTLP.
/// Scrubs Body, FormattedMessage, Attributes (redacting sensitive keys and scrubbing string values),
/// and Exception text.
/// </summary>
public sealed class RoleRadarLogScrubbingProcessor : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Body is string bodyStr)
        {
            data.Body = LogScrubber.ScrubText(bodyStr);
        }

        if (data.FormattedMessage != null)
        {
            data.FormattedMessage = LogScrubber.ScrubText(data.FormattedMessage);
        }

        List<KeyValuePair<string, object?>>? scrubbedAttrs = null;
        if (data.Attributes != null && data.Attributes.Count > 0)
        {
            scrubbedAttrs = new List<KeyValuePair<string, object?>>(data.Attributes.Count + 2);
            foreach (var kv in data.Attributes)
            {
                if (string.Equals(kv.Key, "exception.stacktrace", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (LogScrubber.IsSensitiveKey(kv.Key))
                {
                    scrubbedAttrs.Add(new KeyValuePair<string, object?>(kv.Key, LogScrubber.Redacted));
                }
                else if (kv.Value is string strVal)
                {
                    scrubbedAttrs.Add(new KeyValuePair<string, object?>(kv.Key, LogScrubber.ScrubText(strVal)));
                }
                else
                {
                    scrubbedAttrs.Add(kv);
                }
            }
        }

        if (data.Exception != null)
        {
            var originalType = data.Exception.GetType().FullName ?? data.Exception.GetType().Name;
            var scrubbedMsg = LogScrubber.ScrubText(data.Exception.Message);
            data.Exception = null;

            scrubbedAttrs ??= new List<KeyValuePair<string, object?>>(2);
            scrubbedAttrs.Add(new KeyValuePair<string, object?>("exception.type", originalType));
            scrubbedAttrs.Add(new KeyValuePair<string, object?>("exception.message", scrubbedMsg));
        }

        if (scrubbedAttrs != null)
        {
            data.Attributes = scrubbedAttrs;
        }
    }
}
