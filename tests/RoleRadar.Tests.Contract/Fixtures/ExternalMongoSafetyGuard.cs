using System.Collections.Concurrent;
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

    public static IReadOnlyCollection<string> TrackedTestDatabases => TrackedDatabases.ToArray();

    public static void ValidateOptIn(string? optInValue)
    {
        throw new NotImplementedException("ExternalMongoSafetyGuard not implemented yet.");
    }

    public static void ValidateConnectionString(string connectionString, bool isOptInEnabled = true)
    {
        throw new NotImplementedException("ExternalMongoSafetyGuard not implemented yet.");
    }

    public static void ValidateExistingDatabases(IEnumerable<string> databaseNames)
    {
        throw new NotImplementedException("ExternalMongoSafetyGuard not implemented yet.");
    }

    public static void HandleListDatabasesException(Exception exception)
    {
        throw new NotImplementedException("ExternalMongoSafetyGuard not implemented yet.");
    }

    public static string GenerateAndRegisterTestDatabase()
    {
        throw new NotImplementedException("ExternalMongoSafetyGuard not implemented yet.");
    }

    public static Task DropAllTrackedTestDatabasesAsync(IMongoClient client)
    {
        throw new NotImplementedException("ExternalMongoSafetyGuard not implemented yet.");
    }
}
