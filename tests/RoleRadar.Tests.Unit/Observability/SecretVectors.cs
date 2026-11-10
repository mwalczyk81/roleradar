using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Fictional connection strings with credentials, shared by the LogScrubber tests and the ConnectionStringRedactor
/// cross-check so the two redaction rules cannot drift apart (research R-003).
/// </summary>
internal static class SecretVectors
{
    public const string Password = "SENTINEL-PW-7f3a"; // gitleaks:allow (fictional redaction test vector, not a credential)
    public const string User = "rr-fictional-user";

    public static TheoryData<string> MongoConnectionStrings => new()
    {
        $"mongodb://{User}:{Password}@mongo.test:27017/?replicaSet=rs0",
        $"mongodb+srv://{User}:{Password}@cluster0.example.test/roleradar",
        $"Failed to connect: mongodb://{User}:{Password}@10.0.0.5:27017,10.0.0.6:27017/db?authSource=admin (timeout)",
        $"MONGODB://{User}:{Password}@Mongo.Test",
    };
}
