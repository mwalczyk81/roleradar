namespace RoleRadar.Infrastructure.Mongo.Reconcile;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo.Repositories;

public sealed record ReconcileResult(
    int PostingsExamined,
    int PostingsUpdated,
    int ApplicationsExamined,
    int ApplicationsUpdated,
    bool IsDryRun);

public interface IReadModelReconciliationService
{
    Task<ReconcileResult> ReconcilePostingsAsync(bool dryRun, CancellationToken cancellationToken = default);
    Task<ReconcileResult> ReconcileApplicationsAsync(bool dryRun, CancellationToken cancellationToken = default);
}

public sealed class ReadModelReconciliationService : IReadModelReconciliationService
{
    private readonly IMongoDatabase _database;

    public ReadModelReconciliationService(IMongoDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    public async Task<ReconcileResult> ReconcilePostingsAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        var postings = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var companies = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);

        var allPostings = await postings.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
        var allCompanies = await companies.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
        var companyNameMap = allCompanies.ToDictionary(
            c => c["_id"].AsGuid,
            c => c.Contains("name") ? c["name"].AsString : string.Empty);

        int examined = allPostings.Count;
        int updated = 0;

        foreach (var doc in allPostings)
        {
            var id = doc["_id"].AsGuid;
            var dispositionStr = doc.Contains("disposition") ? doc["disposition"].AsString : "unscored";
            var expectedRank = MapDispositionRank(dispositionStr);

            var hasScoreExpected = doc.Contains("score") && !doc["score"].IsBsonNull;
            var companyId = doc.Contains("company_id") ? doc["company_id"].AsGuid : Guid.Empty;
            companyNameMap.TryGetValue(companyId, out var expectedCompanyName);
            expectedCompanyName ??= string.Empty;

            var currentRank = doc.Contains("disposition_rank") ? doc["disposition_rank"].AsInt32 : (int?)null;
            var currentHasScore = doc.Contains("has_score") ? doc["has_score"].AsBoolean : (bool?)null;
            var currentCompanyName = doc.Contains("company_name") ? doc["company_name"].AsString : null;

            bool needsUpdate = currentRank != expectedRank ||
                               currentHasScore != hasScoreExpected ||
                               currentCompanyName != expectedCompanyName;

            if (needsUpdate)
            {
                updated++;
                if (!dryRun)
                {
                    var update = Builders<BsonDocument>.Update
                        .Set("disposition_rank", expectedRank)
                        .Set("has_score", hasScoreExpected)
                        .Set("company_name", expectedCompanyName);

                    await postings.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                        update,
                        cancellationToken: cancellationToken);
                }
            }
        }

        return new ReconcileResult(examined, updated, 0, 0, dryRun);
    }

    public Task<ReconcileResult> ReconcileApplicationsAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        // Handled in Phase 4
        return Task.FromResult(new ReconcileResult(0, 0, 0, 0, dryRun));
    }

    public static int MapDispositionRank(string disposition)
    {
        if (TrackingEnumExtensions.TryParseDisposition(disposition, out var parsed))
        {
            return MongoPostingRepository.MapDispositionRank(parsed);
        }

        throw new ArgumentOutOfRangeException(nameof(disposition), disposition, $"Unknown posting disposition '{disposition}'.");
    }
}
