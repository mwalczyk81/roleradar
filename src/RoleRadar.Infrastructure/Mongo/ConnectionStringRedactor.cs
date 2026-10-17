using MongoDB.Driver;

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
            return new MongoClient(settings);
        }
        catch (Exception ex) when (ex is MongoConfigurationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException(InvalidConnectionStringErrorMessage);
        }
    }

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
}
