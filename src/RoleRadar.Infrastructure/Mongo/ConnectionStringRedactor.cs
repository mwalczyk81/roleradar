using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Redacts sensitive connection credentials, tokens, and query parameters from MongoDB connection strings,
/// returning only scheme-less host:port list plus replicaSet name (or "&lt;redacted&gt;" if parsing fails).
/// </summary>
public static class ConnectionStringRedactor
{
    public const string RedactedFallback = "<redacted>";

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
