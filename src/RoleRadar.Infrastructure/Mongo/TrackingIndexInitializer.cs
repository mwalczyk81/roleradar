using MongoDB.Bson;
using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

public static class TrackingIndexInitializer
{
    public static async Task EnsureIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        await EnsureCompanyIndexesAsync(database, cancellationToken);
        await EnsureCareerSourceIndexesAsync(database, cancellationToken);
        await EnsurePostingIndexesAsync(database, cancellationToken);
        await EnsurePostingSignalIndexesAsync(database, cancellationToken);
        await EnsureCriteriaIndexesAsync(database, cancellationToken);
        await EnsureApplicationIndexesAsync(database, cancellationToken);
        await EnsureApplicationEventIndexesAsync(database, cancellationToken);
        await EnsureContactIndexesAsync(database, cancellationToken);
        await EnsureSearchRunIndexesAsync(database, cancellationToken);
        await EnsureNoteTrackingIndexesAsync(database, cancellationToken);
    }

    private static async Task EnsureCompanyIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("normalized_name"),
                new CreateIndexOptions { Name = "ux_companies_owner_normalized_name", Unique = true }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("status"),
                new CreateIndexOptions { Name = "ix_companies_owner_status" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureCareerSourceIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.CareerSourcesCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Ascending("company_id")
                    .Ascending("ats_type")
                    .Ascending("tenant")
                    .Ascending("slug"),
                new CreateIndexOptions { Name = "ux_career_sources_composite", Unique = true }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("ats_type").Ascending("tenant"),
                new CreateIndexOptions { Name = "ix_career_sources_owner_ats_tenant" }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("company_id").Ascending("is_active"),
                new CreateIndexOptions { Name = "ix_career_sources_owner_company_active" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsurePostingIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("dedup_key_a"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_postings_owner_dedup_key_a",
                    Unique = true,
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type("dedup_key_a", BsonType.String)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("dedup_key_b"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_postings_owner_dedup_key_b",
                    Unique = true,
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type("dedup_key_b", BsonType.String)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("dedup_key_c"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_postings_owner_dedup_key_c",
                    Unique = true,
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type("dedup_key_c", BsonType.String)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("company_id").Ascending("normalized_title"),
                new CreateIndexOptions { Name = "ix_postings_owner_company_normalized_title" }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("disposition").Ascending("resurface_policy"),
                new CreateIndexOptions { Name = "ix_postings_owner_disposition_resurface" }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("company_id").Ascending("disposition"),
                new CreateIndexOptions { Name = "ix_postings_owner_company_disposition" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsurePostingSignalIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("posting_id").Descending("observed_at"),
                new CreateIndexOptions { Name = "ix_posting_signals_owner_posting_observed" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureCriteriaIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("key"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_criteria_owner_key_active",
                    Unique = true,
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Eq("is_active", true)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("effective_from").Ascending("effective_to"),
                new CreateIndexOptions { Name = "ix_criteria_owner_effective" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureApplicationIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("posting_id"),
                new CreateIndexOptions { Name = "ux_applications_owner_posting", Unique = true }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("outcome"),
                new CreateIndexOptions { Name = "ix_applications_owner_outcome" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureApplicationEventIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("application_id").Ascending("event_date"),
                new CreateIndexOptions { Name = "ix_application_events_timeline" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureContactIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("is_redacted"),
                new CreateIndexOptions { Name = "ix_contacts_owner_redacted" })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureSearchRunIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("started_at"),
                new CreateIndexOptions { Name = "ux_search_runs_owner_started", Unique = true })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private static async Task EnsureNoteTrackingIndexesAsync(IMongoDatabase database, CancellationToken cancellationToken)
    {
        var collection = database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        var models = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("posting_id"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ix_notes_owner_posting_partial",
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type("posting_id", BsonType.Binary)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("company_id"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ix_notes_owner_company_partial",
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type("company_id", BsonType.Binary)
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys.Ascending("owner_id").Ascending("application_id"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ix_notes_owner_application_partial",
                    PartialFilterExpression = Builders<BsonDocument>.Filter.Type("application_id", BsonType.Binary)
                })
        };
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }
}
