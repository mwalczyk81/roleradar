using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

public static class TrackingIndexInitializer
{
    // Stub implementation for test-first phase; does not create indexes so contract tests fail on assertions
    public static Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
