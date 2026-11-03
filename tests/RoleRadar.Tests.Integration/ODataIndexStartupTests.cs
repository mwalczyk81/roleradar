namespace RoleRadar.Tests.Integration;

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Api.OData;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reading;
using Xunit;

/// <summary>
/// Phase 7 (E): an API process that only serves OData reads must still have the sort indexes. The host boots
/// against an empty Mongo, receives nothing but GETs, and the nulls-last and collation indexes must exist.
/// </summary>
public sealed class ODataIndexStartupTests : IAsyncLifetime
{
    private const string Owner = "index-startup-owner";

    private readonly ODataTestHost _host = new("odata_index_startup_test_db", Owner);

    public async ValueTask InitializeAsync()
    {
        await _host.StartDatabaseAsync();
        await _host.StartApiAsync();
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    // collection, index name, second key when it is a has_* flag (compound nulls-last), whether it is a collation index
    private static readonly (string Collection, string Index, string? Flag, bool Collation)[] Expected =
    [
        (TrackingMongoOptions.PostingsCollectionName, "ix_postings_owner_score_asc_nulls_last", "has_score", false),
        (TrackingMongoOptions.PostingsCollectionName, "ix_postings_owner_score_desc_nulls_last", "has_score", false),
        (TrackingMongoOptions.PostingsCollectionName, "ix_postings_owner_title_collation", null, true),
        (TrackingMongoOptions.CompaniesCollectionName, "ix_companies_owner_name_collation", null, true),
        (TrackingMongoOptions.ApplicationsCollectionName, "ix_applications_owner_first_response_asc_nulls_last", "has_first_response", false),
        (TrackingMongoOptions.ApplicationsCollectionName, "ix_applications_owner_first_response_desc_nulls_last", "has_first_response", false),
        (TrackingMongoOptions.ApplicationsCollectionName, "ix_applications_owner_last_event_asc_nulls_last", "has_last_event", false),
        (TrackingMongoOptions.ApplicationsCollectionName, "ix_applications_owner_last_event_desc_nulls_last", "has_last_event", false),
        (TrackingMongoOptions.ApplicationsCollectionName, "ix_applications_owner_company_name_collation", null, true),
        (MongoBootstrapAndIndexes.NotesCollectionName, "ix_notes_owner_updated_at", null, false),
        (MongoBootstrapAndIndexes.NotesCollectionName, "ix_notes_owner_title_collation", null, true),
        (TrackingMongoOptions.CriteriaCollectionName, "ix_criteria_owner_effective_to_asc_nulls_last", "has_effective_to", false),
        (TrackingMongoOptions.CriteriaCollectionName, "ix_criteria_owner_effective_to_desc_nulls_last", "has_effective_to", false),
        (TrackingMongoOptions.CriteriaCollectionName, "ix_criteria_owner_key_collation", null, true),
        (TrackingMongoOptions.SearchRunsCollectionName, "ix_search_runs_owner_new_postings", null, false)
    ];

    [Fact]
    public async Task ReadOnlyHost_HasTheSortIndexes_AfterOnlyODataGets()
    {
        // Only reads, one per entity set, each sorted on its nulls-last (or newest-first) field.
        foreach (var query in new[]
        {
            "Postings?$orderby=score desc",
            "Companies?$orderby=name asc",
            "Applications?$orderby=firstResponseAt asc",
            "Notes?$orderby=updatedAt desc",
            "Criteria?$orderby=effectiveTo asc",
            "SearchRuns?$orderby=runDate desc"
        })
        {
            var (status, body) = await _host.GetJsonAsync(Owner, "/odata/" + query);
            Assert.True(status == HttpStatusCode.OK, $"{query}: {(int)status} {body.RootElement}");
        }

        var problems = new List<string>();
        foreach (var (collection, indexName, flag, collation) in Expected)
        {
            var indexes = (await _host.Database.GetCollection<BsonDocument>(collection).Indexes.ListAsync()).ToList();
            var index = indexes.SingleOrDefault(i => i["name"].AsString == indexName);
            if (index is null)
            {
                problems.Add($"{collection}: missing '{indexName}'");
                continue;
            }

            var key = index["key"].AsBsonDocument.Elements.ToList();
            if (key[0].Name != "owner_id" || key[0].Value.ToInt32() != 1)
            {
                problems.Add($"{collection}.{indexName}: owner_id must be the first ascending key");
            }

            if (flag is not null && (key[1].Name != flag || key[1].Value.ToInt32() != -1))
            {
                problems.Add($"{collection}.{indexName}: second key must be {flag} descending");
            }

            if (collation && (index["collation"]["locale"].AsString != "en" || index["collation"]["strength"].ToInt32() != 2))
            {
                problems.Add($"{collection}.{indexName}: collation must be en strength 2");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task IndexCreationFailure_IsLogged_AndDoesNotStopTheHost()
    {
        // A Mongo that cannot be reached: creating indexes fails, the startup service must still complete.
        var client = new MongoClient("mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=300&connectTimeoutMS=300");
        var initializer = new ReadIndexInitializer(client, Options.Create(new MongoOptions { DatabaseName = "unreachable_db" }));
        var logger = new CapturingLogger<ReadIndexStartupService>();

        var service = new ReadIndexStartupService(initializer, logger);
        await service.StartAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.NotNull(entry.Exception);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
