namespace RoleRadar.Tests.Integration.Insights;

using System.Text.RegularExpressions;
using MongoDB.Driver;

/// <summary>
/// Fails closed unless the connection string points at the test's own Testcontainers endpoint and the database
/// name is a throwaway <c>insights-parity-&lt;guid&gt;</c> name. Called before the first seed write, so the
/// Insights tests can never write to (or read from) a developer's running MongoDB.
/// </summary>
internal static partial class InsightsSeedGuard
{
    public const string DatabaseNamePrefix = "insights-parity-";

    public static string NewDatabaseName() => DatabaseNamePrefix + Guid.NewGuid().ToString("N");

    public static void AssertIsolated(string? connectionString, string? databaseName, string containerHost, int containerPort)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Insights seed guard: no connection string supplied.");
        }

        if (string.IsNullOrWhiteSpace(databaseName) || !ThrowawayName().IsMatch(databaseName))
        {
            throw new InvalidOperationException(
                $"Insights seed guard: database name must match '{DatabaseNamePrefix}<32 hex chars>'.");
        }

        if (string.IsNullOrWhiteSpace(containerHost) || containerPort <= 0)
        {
            throw new InvalidOperationException("Insights seed guard: the container endpoint is unknown.");
        }

        MongoUrl url;
        try
        {
            url = MongoUrl.Create(connectionString);
        }
        catch (Exception ex) when (ex is MongoConfigurationException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("Insights seed guard: the connection string could not be parsed.");
        }

        if (url.Servers.Count() != 1)
        {
            throw new InvalidOperationException("Insights seed guard: exactly one server endpoint is required.");
        }

        var server = url.Servers.Single();
        var sameHost = string.Equals(server.Host, containerHost, StringComparison.OrdinalIgnoreCase);
        if (!sameHost || server.Port != containerPort)
        {
            // Deliberately does not echo the host or port: they could identify a real server.
            throw new InvalidOperationException("Insights seed guard: the connection endpoint is not this test's container.");
        }
    }

    [GeneratedRegex("^insights-parity-[0-9a-f]{32}$")]
    private static partial Regex ThrowawayName();
}
