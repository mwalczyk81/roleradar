using MongoDB.Driver;

namespace RoleRadar.Tests.Contract.Fixtures;

public sealed class DatabaseCapabilityMatrixReporter
{
    public static DatabaseCapabilityMatrixReporter Instance { get; } = new();

    public void RecordResult(string testName, string category, bool passed, double durationMs, string? errorMessage = null)
    {
        throw new NotImplementedException();
    }

    public Task GenerateReportsAsync(IMongoDatabase database, string outputDirectory)
    {
        throw new NotImplementedException();
    }

    public Task<string> GenerateJsonReportAsync(IMongoDatabase database)
    {
        throw new NotImplementedException();
    }

    public Task<string> GenerateMarkdownReportAsync(IMongoDatabase database)
    {
        throw new NotImplementedException();
    }
}
