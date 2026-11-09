namespace RoleRadar.Infrastructure.Mongo;

using MongoDB.Driver;

/// <summary>Outcome of a demo guard check. The message never echoes a connection string, host or database name input.</summary>
public sealed record DemoGuardResult(bool Allowed, string Message)
{
    public static DemoGuardResult Allow { get; } = new(true, "Allowed.");

    public static DemoGuardResult Refuse(string message) => new(false, message);
}

/// <summary>
/// Fails closed unless demo mode (<c>ROLERADAR_DEMO=1</c>) is on, the database is exactly the demo database and the
/// single server is the demo Mongo service. Production callers use <see cref="CheckForDemoStack"/> or
/// <see cref="CheckServe"/>, which supply the fixed demo host; only tests pass their own host to
/// <see cref="Check"/>. See ADR 0039.
/// </summary>
public static class DemoModeGuard
{
    /// <summary>Name of the switch. Not a secret.</summary>
    public const string DemoModeVariable = "ROLERADAR_DEMO";

    /// <summary>The only database demo mode may write or serve. Not a secret.</summary>
    public const string DemoDatabaseName = "roleradar-demo";

    /// <summary>The compose service name of the demo's Mongo. Not a secret.</summary>
    public const string DemoMongoHost = "demo-mongo";

    private const string DemoModeOn = "1";

    /// <summary>Production check for the demo seed and demo account commands: strict, with the fixed demo host.</summary>
    public static DemoGuardResult CheckForDemoStack(string? demoModeValue, string? databaseName, string? connectionString) =>
        Check(demoModeValue, databaseName, connectionString, DemoMongoHost);

    /// <summary>
    /// Strict check with an explicit allowed host. For tests that run against a throwaway container endpoint.
    /// Production code must not call this overload (an architecture test enforces it).
    /// </summary>
    public static DemoGuardResult Check(string? demoModeValue, string? databaseName, string? connectionString, string? allowedHost)
    {
        if (!string.Equals(demoModeValue, DemoModeOn, StringComparison.Ordinal))
        {
            return DemoGuardResult.Refuse("Demo mode is not switched on.");
        }

        if (!string.Equals(databaseName, DemoDatabaseName, StringComparison.Ordinal))
        {
            return DemoGuardResult.Refuse($"The database is not the demo database '{DemoDatabaseName}'.");
        }

        if (string.IsNullOrWhiteSpace(allowedHost))
        {
            return DemoGuardResult.Refuse("No demo server is configured.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return DemoGuardResult.Refuse("No connection string was supplied.");
        }

        MongoUrl url;
        try
        {
            url = MongoUrl.Create(connectionString);
        }
        catch (Exception ex) when (ex is MongoConfigurationException or FormatException or ArgumentException)
        {
            return DemoGuardResult.Refuse("The connection string could not be parsed.");
        }

        var servers = url.Servers.ToArray();
        if (servers.Length != 1)
        {
            return DemoGuardResult.Refuse("Exactly one server endpoint is required.");
        }

        if (!string.Equals(servers[0].Host, allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            // Deliberately does not echo the host: it could identify a real server.
            return DemoGuardResult.Refuse("The server is not the demo Mongo service.");
        }

        return DemoGuardResult.Allow;
    }

    /// <summary>
    /// Start-up check for hosts that serve data (the API). With demo mode on it requires the demo stack. With demo
    /// mode off it refuses the demo database, so the demo data is never served as real. An ambiguous switch value
    /// refuses. Any other configuration is not the guard's business and is allowed.
    /// </summary>
    public static DemoGuardResult CheckServe(string? demoModeValue, string? databaseName, string? connectionString)
    {
        if (string.Equals(demoModeValue, DemoModeOn, StringComparison.Ordinal))
        {
            return CheckForDemoStack(demoModeValue, databaseName, connectionString);
        }

        if (!string.IsNullOrEmpty(demoModeValue) && !string.Equals(demoModeValue, "0", StringComparison.Ordinal))
        {
            return DemoGuardResult.Refuse("The demo mode switch has an unrecognised value; use 1 or leave it unset.");
        }

        if (string.Equals(databaseName, DemoDatabaseName, StringComparison.Ordinal))
        {
            return DemoGuardResult.Refuse("The demo database is only served in demo mode.");
        }

        return DemoGuardResult.Allow;
    }
}
