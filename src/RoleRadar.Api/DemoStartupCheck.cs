using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Api;

/// <summary>
/// Start-up wiring of the demo guard (feature 012, ADR 0039). With demo mode on, the API starts only against the demo
/// database on the demo Mongo service. With demo mode off, it refuses the demo database, so the demo data is never
/// served as real. Any other configuration is untouched.
/// </summary>
public static class DemoStartupCheck
{
    public static void Enforce(string? demoModeValue, string? databaseName, string? connectionString)
    {
        var result = DemoModeGuard.CheckServe(demoModeValue, databaseName, connectionString);
        if (!result.Allowed)
        {
            var message = "[FATAL] RoleRadar.Api refused to start: " + result.Message;
            Console.Error.WriteLine(message);
            throw new InvalidOperationException(message);
        }
    }
}
