namespace RoleRadar.Application.Abstractions;

public interface ICsvRecord
{
    int LineNumber { get; }
    int FieldCount { get; }
    IReadOnlyList<string> Fields { get; }
    IReadOnlyList<string>? Headers { get; }
    string? this[int index] { get; }
    string? this[string headerName] { get; }
    bool TryGet(string headerName, out string? value);
}

public interface ICsvParser
{
    IEnumerable<ICsvRecord> Read(TextReader reader, bool hasHeader = true);
    IReadOnlyList<string> ReadHeader(TextReader reader);
}

public interface ITrackerScratchScope : IAsyncDisposable
{
    string ScratchDatabaseName { get; }
    string TargetDatabaseName { get; }
}

public interface ITrackerScratchManager
{
    Task<ITrackerScratchScope> CreateScopeAsync(string targetDatabaseName, CancellationToken cancellationToken = default);
    Task<int> CleanupOrphanedScratchDatabasesAsync(string targetDatabaseName, TimeSpan? minimumAge = null, CancellationToken cancellationToken = default);
}

public interface ITrackingContextFactory
{
    string TargetDatabaseName { get; }
    ITrackingTransactionContext CreateContext(string databaseName);
}
