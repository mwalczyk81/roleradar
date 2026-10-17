using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Mongo;

public sealed class MongoReadinessProbeLoggingTests
{
    private sealed class TestLogger : ILogger<MongoReadinessProbe>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    [Fact]
    public async Task OfflineFailure_LogsOneWarningLineWithoutExceptionObject_AndLogsExceptionOnlyAtDebug()
    {
        var options = Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://user:s3cr3t@unreachable-host.local:27017/?replicaSet=rs0&authSource=admin",
            ReadinessTimeout = TimeSpan.FromMilliseconds(50)
        });

        var logger = new TestLogger();

        // Pass a mock or client pointing to an unreachable host
        var clientSettings = MongoClientSettings.FromConnectionString(options.Value.ConnectionString);
        clientSettings.ServerSelectionTimeout = TimeSpan.FromMilliseconds(50);
        var client = new MongoClient(clientSettings);

        var probe = new MongoReadinessProbe(client, options, logger);

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.DatastoreOffline, result.Error?.Code);

        // Assert credential redaction in the result error message and metadata
        Assert.DoesNotContain("s3cr3t", result.Error?.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authSource", result.Error?.Message, StringComparison.OrdinalIgnoreCase);

        // Warning log must exist, have no exception object, and not contain the secret
        var warningEntries = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
        Assert.Single(warningEntries);
        var warning = warningEntries[0];
        Assert.Null(warning.Exception);
        Assert.DoesNotContain("s3cr3t", warning.Message, StringComparison.OrdinalIgnoreCase);

        // Debug log must contain the exception
        var debugEntries = logger.Entries.Where(e => e.Level == LogLevel.Debug).ToList();
        Assert.Single(debugEntries);
        var debug = debugEntries[0];
        Assert.NotNull(debug.Exception);
    }
}
