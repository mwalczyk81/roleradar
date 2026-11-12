using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using MongoDB.Driver.Core.Events;
using MongoDB.Driver.Core.Servers;
using RoleRadar.Domain.Common;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Redacts sensitive connection credentials, tokens, and query parameters from MongoDB connection strings,
/// returning only scheme-less host:port list plus replicaSet name (or "&lt;redacted&gt;" if parsing fails).
/// </summary>
public static class ConnectionStringRedactor
{
    public const string RedactedFallback = "<redacted>";
    public const string InvalidConnectionStringErrorMessage =
        "ROLERADAR_MONGO_CONNECTION_STRING is not a valid MongoDB connection string. The value is hidden.";

    /// <summary>
    /// Validates the connection string and creates a configured <see cref="MongoClient"/>,
    /// resolving SRV records eagerly and catching <see cref="MongoConfigurationException"/>,
    /// <see cref="FormatException"/>, and <see cref="ArgumentException"/> so invalid input is never echoed.
    /// </summary>
    public static MongoClient CreateClient(string connectionString, TimeSpan timeout)
    {
        try
        {
            var trimmed = connectionString.Trim();
            var mongoUrl = MongoUrl.Create(trimmed);

            // If mongodb+srv://, resolve records eagerly to catch unresolvable host configurations at startup
            if (trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
                mongoUrl = mongoUrl.Resolve(resolveHosts: true);
            }

            var settings = MongoClientSettings.FromUrl(mongoUrl);
            settings.ServerSelectionTimeout = timeout;
            settings.ConnectTimeout = timeout;

            // Notice a stalled server quickly (feature 013, FR-024). The driver's default is a streaming monitor that waits the
            // heartbeat interval plus the connect timeout (about 13 s) before it gives up on a frozen server, and an operation
            // already sent to that server waits for the same verdict: measured at 14 s against a paused container. Polling
            // every second with a short timeout fails such an operation in a few seconds. The cost is one small hello per second.
            settings.ServerMonitoringMode = ServerMonitoringMode.Poll;
            settings.HeartbeatInterval = TimeSpan.FromSeconds(1);
            settings.HeartbeatTimeout = TimeSpan.FromSeconds(2);

            // No driver-level read retry. After a failed read it would select a server again and wait out the whole
            // server-selection timeout a second time, doubling the time to a 503 (5.9 s measured against a paused container).
            // Callers that want a retry own it: the web retries GETs, bounded, in its outbound policy.
            settings.RetryReads = false;

            // Feature 015 (FR-005, ADR 0050): Enable native MongoDB.Driver ActivitySource tracing with QueryTextMaxLength = 0
            // so command spans (find, insert, update, commitTransaction) are emitted without serializing db.query.text.
            settings.TracingOptions = new TracingOptions
            {
                Disabled = false,
                QueryTextMaxLength = 0,
            };

            // Feature 015 Gate 4 (RR-69, T061, ADR 0052): Record MongoDB command operations from driver events
            settings.ClusterConfigurator = cb =>
            {
                cb.Subscribe<CommandStartedEvent>(e =>
                {
                    if (e.Command != null && e.Command.ElementCount > 0)
                    {
                        var firstElem = e.Command.GetElement(0);
                        if (firstElem.Value != null && firstElem.Value.IsString)
                        {
                            var candidate = firstElem.Value.AsString;
                            if (KnownCollections.Contains(candidate))
                            {
                                ActiveCommandCollections[e.RequestId] = candidate;
                            }
                        }
                    }
                });

                cb.Subscribe<CommandSucceededEvent>(e =>
                {
                    ActiveCommandCollections.TryRemove(e.RequestId, out var collectionName);
                    RoleRadarStoreMetrics.RecordOperation(
                        e.Duration.TotalSeconds,
                        RoleRadarStoreMetrics.SystemMongoDb,
                        e.CommandName,
                        collectionName,
                        errorType: null);
                });

                cb.Subscribe<CommandFailedEvent>(e =>
                {
                    ActiveCommandCollections.TryRemove(e.RequestId, out var collectionName);
                    RoleRadarStoreMetrics.RecordOperation(
                        e.Duration.TotalSeconds,
                        RoleRadarStoreMetrics.SystemMongoDb,
                        e.CommandName,
                        collectionName,
                        errorType: e.Failure?.GetType().Name);
                });
            };

            return new MongoClient(settings);
        }
        catch (Exception ex) when (ex is MongoConfigurationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException(InvalidConnectionStringErrorMessage);
        }
    }

    private static readonly ConcurrentDictionary<int, string> ActiveCommandCollections = new();

    internal static readonly HashSet<string> KnownCollections = new(StringComparer.Ordinal)
    {
        TrackingMongoOptions.CompaniesCollectionName,
        TrackingMongoOptions.CareerSourcesCollectionName,
        TrackingMongoOptions.PostingsCollectionName,
        TrackingMongoOptions.PostingSignalsCollectionName,
        TrackingMongoOptions.CriteriaCollectionName,
        TrackingMongoOptions.ApplicationsCollectionName,
        TrackingMongoOptions.ApplicationEventsCollectionName,
        TrackingMongoOptions.ContactsCollectionName,
        TrackingMongoOptions.SearchRunsCollectionName,
        MongoBootstrapAndIndexes.NotesCollectionName,
        MongoBootstrapAndIndexes.NoteRevisionsCollectionName,
        MongoBootstrapAndIndexes.AuditLogCollectionName,
    };

    public static string Redact(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return RedactedFallback;
        }

        try
        {
            var trimmed = connectionString.Trim();
            var isSrv = trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase);
            var mongoUrl = new MongoUrl(trimmed);
            string hostsPart;
            if (mongoUrl.Servers != null && mongoUrl.Servers.Any())
            {
                hostsPart = string.Join(",", mongoUrl.Servers.Select(s => FormatServerAddress(s, isSrv)));
            }
            else if (mongoUrl.Server != null)
            {
                hostsPart = FormatServerAddress(mongoUrl.Server, isSrv);
            }
            else
            {
                return RedactedFallback;
            }

            if (string.IsNullOrWhiteSpace(hostsPart))
            {
                return RedactedFallback;
            }

            if (!string.IsNullOrWhiteSpace(mongoUrl.ReplicaSetName))
            {
                return $"{hostsPart} ({mongoUrl.ReplicaSetName})";
            }

            return hostsPart;
        }
        catch
        {
            return RedactedFallback;
        }
    }

    private static string FormatServerAddress(MongoServerAddress server, bool isSrv)
    {
        // For SRV connections, MongoDB driver defaults the port to 53 (DNS) or 27017 prior to DNS resolution.
        // If it's SRV and port is 53, omit port so host alone is displayed.
        if (isSrv && server.Port == 53)
        {
            return server.Host;
        }

        // For standard hosts or IPv6: MongoServerAddress formats as host:port or [ipv6]:port appropriately
        if (server.Host.Contains(':') && !server.Host.StartsWith('['))
        {
            return $"[{server.Host}]:{server.Port}";
        }

        return $"{server.Host}:{server.Port}";
    }

    public static string RedactSensitiveText(string? text, string? connectionString = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = Regex.Replace(text, @"(mongodb(?:\+srv)?:\/\/)([^:@\s]+):([^@\s]+)@", "$1<redacted>:<redacted>@", RegexOptions.IgnoreCase);

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            try
            {
                var url = new MongoUrl(connectionString.Trim());
                if (!string.IsNullOrEmpty(url.Password))
                {
                    result = result.Replace(url.Password, "<redacted>", StringComparison.Ordinal);
                }
                if (!string.IsNullOrEmpty(url.Username))
                {
                    result = result.Replace(url.Username, "<redacted>", StringComparison.Ordinal);
                }
            }
            catch
            {
                // Ignore parse errors
            }
        }

        return result;
    }
}
