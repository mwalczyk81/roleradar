using System.Collections.Frozen;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;

namespace RoleRadar.Observability;

/// <summary>
/// Enforces the Feature 015 Telemetry Privacy Gate (ADR 0050, FR-012 to FR-014, SC-002) on every
/// <see cref="Activity"/> before export. Applies a closed operational attribute allowlist, an explicit
/// sensitive-key drop list, URL query/fragment/userinfo stripping, status-description clearing, and
/// span exception-event sanitization.
/// </summary>
public sealed class RoleRadarSpanAttributeProcessor : BaseProcessor<Activity>
{
    public static FieldInfo? ActivityEventsField { get; } =
        typeof(Activity).GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic);
    /// <summary>
    /// Closed allowlist of operational span attribute keys permitted on exported RoleRadar spans.
    /// Any attribute key not present in this set is removed before export.
    /// </summary>
    public static IReadOnlySet<string> AllowedAttributeKeys { get; } = new[]
    {
        // HTTP & Network operational attributes
        "http.request.method",
        "http.request.method_original",
        "http.response.status_code",
        "http.route",
        "url.scheme",
        "url.path",
        "url.full",
        "server.address",
        "server.port",
        "network.protocol.version",
        "network.peer.address",
        "network.transport",

        // MongoDB & database operational metadata (never query text or filter literals)
        "db.system",
        "db.system.name",
        "db.namespace",
        "db.name",
        "db.collection.name",
        "db.mongodb.collection",
        "db.operation",
        "db.operation.name",
        "db.operation.summary",
        "db.command.name",
        "db.query.summary",
        "db.response.status_code",
        "db.mongodb.lsid",
        "db.mongodb.txn_number",
        "db.mongodb.server_connection_id",
        "db.mongodb.driver_connection_id",
        "db.mongodb.cursor_id",

        // Error & RoleRadar operational tags
        "error.type",
        "exception.type",
        "otel.status_code",
        "roleradar.actor",
        "roleradar.command",
        "roleradar.query",
        "roleradar.tool",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Explicit drop list of sensitive attribute keys that are unconditionally removed.
    /// </summary>
    public static IReadOnlySet<string> DroppedAttributeKeys { get; } = new[]
    {
        "url.query",
        "http.target",
        "http.url",
        "db.query.text",
        "db.statement",
        "db.connection_string",
        "exception.message",
        "exception.stacktrace",
        "otel.status_description",
        "enduser.id",
        "user.id",
        "client.address",
        "user_agent.original",
        "http.user_agent",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // 1. Clear free-text StatusDescription while preserving Status code (Unset, Ok, Error).
        if (!string.IsNullOrEmpty(data.StatusDescription))
        {
            data.SetStatus(data.Status, null);
        }

        // 2. Sanitize span events (e.g. "exception" events): retain only exception.type and copy to error.type.
        var originalEvents = data.Events.ToList();
        if (originalEvents.Count > 0)
        {
            ActivityEventsField?.SetValue(data, null);

            foreach (var ev in originalEvents)
            {
                string? exceptionType = null;
                var sanitizedEventTags = new ActivityTagsCollection();

                foreach (var tag in ev.Tags)
                {
                    if (string.Equals(tag.Key, "exception.type", StringComparison.OrdinalIgnoreCase)
                        && tag.Value is string exType
                        && !string.IsNullOrWhiteSpace(exType))
                    {
                        exceptionType = exType;
                        sanitizedEventTags["exception.type"] = exType;
                    }
                    else if (!IsDroppedOrUnlisted(tag.Key))
                    {
                        sanitizedEventTags[tag.Key] = tag.Value;
                    }
                }

                if (exceptionType is not null && data.GetTagItem("error.type") is null)
                {
                    data.SetTag("error.type", exceptionType);
                }

                data.AddEvent(new ActivityEvent(ev.Name, ev.Timestamp, sanitizedEventTags));
            }
        }

        // 3. Enforce the closed attribute allowlist, drop list, header prefix filter, and URL sanitization.
        var updates = new List<(string Key, object? Value)>();
        foreach (var tag in data.TagObjects)
        {
            var key = tag.Key;
            if (IsDroppedOrUnlisted(key))
            {
                updates.Add((key, null));
                continue;
            }

            if (tag.Value is string strValue)
            {
                if (string.Equals(key, "url.full", StringComparison.OrdinalIgnoreCase))
                {
                    var sanitized = SanitizeFullUrl(strValue);
                    if (!string.Equals(sanitized, strValue, StringComparison.Ordinal))
                    {
                        updates.Add((key, sanitized));
                    }
                }
                else if (string.Equals(key, "url.path", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(key, "http.route", StringComparison.OrdinalIgnoreCase))
                {
                    var sanitized = StripQueryAndFragment(strValue);
                    if (!string.Equals(sanitized, strValue, StringComparison.Ordinal))
                    {
                        updates.Add((key, sanitized));
                    }
                }
            }
        }

        foreach (var (key, value) in updates)
        {
            data.SetTag(key, value);
        }
    }

    private static bool IsDroppedOrUnlisted(string key)
    {
        if (DroppedAttributeKeys.Contains(key))
        {
            return true;
        }

        if (key.StartsWith("http.request.header.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("http.response.header.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !AllowedAttributeKeys.Contains(key);
    }

    public static string SanitizeFullUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return string.Empty;
        }

        var withoutQueryOrFragment = StripQueryAndFragment(rawUrl);

        if (Uri.TryCreate(withoutQueryOrFragment, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri)
            {
                UserName = string.Empty,
                Password = string.Empty,
                Query = string.Empty,
                Fragment = string.Empty,
            };
            return builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        }

        return StripUserInfoFallback(withoutQueryOrFragment);
    }

    public static string StripQueryAndFragment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var qIdx = value.IndexOf('?', StringComparison.Ordinal);
        var fIdx = value.IndexOf('#', StringComparison.Ordinal);

        var cutIndex = (qIdx, fIdx) switch
        {
            ( >= 0, >= 0) => Math.Min(qIdx, fIdx),
            ( >= 0, _) => qIdx,
            (_, >= 0) => fIdx,
            _ => -1,
        };

        return cutIndex >= 0 ? value[..cutIndex] : value;
    }

    private static string StripUserInfoFallback(string value)
    {
        var schemeSep = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSep < 0)
        {
            return value;
        }

        var authorityStart = schemeSep + 3;
        var pathStart = value.IndexOf('/', authorityStart);
        var authorityEnd = pathStart >= 0 ? pathStart : value.Length;
        var atIdx = value.IndexOf('@', authorityStart, authorityEnd - authorityStart);
        if (atIdx < 0)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, authorityStart), value.AsSpan(atIdx + 1));
    }
}
