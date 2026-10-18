using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using MongoDB.Driver;

namespace RoleRadar.Tests.Contract.Fixtures;

public class OptInMissingException(string message) : InvalidOperationException(message);
public class TlsRequiredException(string message) : InvalidOperationException(message);
public class ConflictingTlsParametersException(string message) : ArgumentException(message);
public class RedactedPermissionException(string message) : UnauthorizedAccessException(message);
public class ForeignDatabaseDetectedException(string message) : InvalidOperationException(message);

public static class ExternalMongoSafetyGuard
{
    private static readonly ConcurrentBag<string> TrackedDatabases = [];
    private static readonly HashSet<string> SystemDatabases = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "config", "local"
    };

    private static readonly Regex TlsConflictRegex = new(
        @"(?=.*\b(tls|ssl)=false\b)(?=.*\b(tls|ssl)=true\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyCollection<string> TrackedTestDatabases => TrackedDatabases.ToArray();

    public static void ValidateOptIn(string? optInValue)
    {
        if (optInValue != "1")
        {
            throw new OptInMissingException(
                "External MongoDB testing requires explicit opt-in via environment variable ROLERADAR_EXTERNAL_TEST_OPT_IN=1.");
        }
    }

    public static void ValidateConnectionString(string connectionString, bool isOptInEnabled = true)
    {
        if (!isOptInEnabled)
        {
            throw new OptInMissingException(
                "External MongoDB testing requires explicit opt-in via environment variable ROLERADAR_EXTERNAL_TEST_OPT_IN=1.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        // Check for conflicting TLS parameters in the connection string
        if (TlsConflictRegex.IsMatch(connectionString))
        {
            throw new ConflictingTlsParametersException(
                "Conflicting TLS/SSL parameters specified in connection URI (e.g. tls=false with ssl=true).");
        }

        var mongoUrl = new MongoUrl(connectionString);

        // Check query string parameters for explicit tls=false / ssl=false
        var hasExplicitTlsDisabled = connectionString.Contains("tls=false", StringComparison.OrdinalIgnoreCase) ||
                                     connectionString.Contains("ssl=false", StringComparison.OrdinalIgnoreCase);

        var isTlsActive = mongoUrl.UseTls && !hasExplicitTlsDisabled;

        foreach (var server in mongoUrl.Servers)
        {
            var isLoopback = IsLoopbackHost(server.Host);
            if (!isLoopback && !isTlsActive)
            {
                throw new TlsRequiredException(
                    $"TLS is strictly required for non-loopback MongoDB host '{server.Host}'. Plaintext external connections are rejected.");
            }
        }
    }

    public static bool IsLoopbackHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        // Docker internal host is explicitly non-loopback
        if (host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var cleanHost = host.Trim('[', ']');

        if (cleanHost.Equals("::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(cleanHost, out var ip))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                return bytes[0] == 127;
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            {
                return IPAddress.IsLoopback(ip);
            }
        }

        return false;
    }

    public static void ValidateExistingDatabases(IEnumerable<string> databaseNames)
    {
        var foreignDatabases = databaseNames
            .Where(db => !SystemDatabases.Contains(db))
            .Where(db => !db.StartsWith("rrtest_", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (foreignDatabases.Length > 0)
        {
            throw new ForeignDatabaseDetectedException(
                $"Target server contains non-test database(s): {string.Join(", ", foreignDatabases)}. Aborting to prevent accidental data loss.");
        }
    }

    public static void HandleListDatabasesException(Exception exception)
    {
        _ = exception;
        throw new RedactedPermissionException(
            "External database guard failed: user lacks 'listDatabases' cluster privilege to verify database prefix safety. Provide an account with cluster monitor or listDatabases privileges, or use a dedicated test cluster.");
    }

    public static string GenerateAndRegisterTestDatabase()
    {
        var dbName = $"rrtest_{Guid.CreateVersion7():N}";
        TrackedDatabases.Add(dbName);
        return dbName;
    }

    public static async Task DropAllTrackedTestDatabasesAsync(IMongoClient client)
    {
        while (TrackedDatabases.TryTake(out var dbName))
        {
            try
            {
                await client.DropDatabaseAsync(dbName);
            }
            catch
            {
                // Best effort cleanup during shutdown/teardown
            }
        }
    }
}
