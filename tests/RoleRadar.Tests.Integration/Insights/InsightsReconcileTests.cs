namespace RoleRadar.Tests.Integration.Insights;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Xunit;

/// <summary>
/// Pre-007 documents lack the five derived fields. <c>reconcile-read-models</c> must report them in a dry run
/// without writing, fix them on a confirmed run, and then report zero. Runs only against the fixture's own
/// Testcontainers database.
/// </summary>
public sealed class InsightsReconcileTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    [Fact]
    public async Task LegacyDocuments_AreReportedThenFixedThenClean()
    {
        var db = fixture.Database;
        await InsightsSeed.StripDerivedFieldsAsync(db);

        var postingCount = (int)await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        var applicationCount = (int)await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        Assert.Equal(InsightsSeed.Postings.Count, postingCount);
        Assert.Equal(InsightsSeed.Postings.Count(p => p.HasApplication), applicationCount);

        var service = new ReadModelReconciliationService(db);

        // Dry run: reports every legacy document, writes nothing.
        var dryPostings = await service.ReconcilePostingsAsync(dryRun: true);
        var dryApplications = await service.ReconcileApplicationsAsync(dryRun: true);
        Assert.Equal(postingCount, dryPostings.PostingsExamined);
        Assert.Equal(postingCount, dryPostings.PostingsUpdated);
        Assert.Equal(applicationCount, dryApplications.ApplicationsExamined);
        Assert.Equal(applicationCount, dryApplications.ApplicationsUpdated);
        Assert.Equal(0, await CountMissingAsync(db));

        var sampleBefore = await InsightsTestSupport.PostingDocAsync(db, "age-50-silent");
        Assert.Null(InsightsTestSupport.Flag(sampleBefore, "has_application"));

        // Confirmed run writes the fields.
        await service.ReconcilePostingsAsync(dryRun: false);
        await service.ReconcileApplicationsAsync(dryRun: false);

        var problems = new List<string>();
        foreach (var seed in InsightsSeed.Postings)
        {
            var posting = await InsightsTestSupport.PostingDocAsync(db, seed.Key);
            if (InsightsTestSupport.Flag(posting, "has_application") != seed.HasApplication)
            {
                problems.Add($"{seed.Key}: has_application not backfilled correctly");
            }

            if (!seed.HasApplication)
            {
                continue;
            }

            var facts = InsightsOracle.Facts(seed);
            var app = await InsightsTestSupport.ApplicationDocAsync(db, seed.Key);
            if (InsightsTestSupport.Flag(app, "has_offer") != facts.HasOffer ||
                InsightsTestSupport.Flag(app, "has_rejection") != facts.HasRejection ||
                InsightsTestSupport.Flag(app, "rejected_before_interview") != (facts.HasRejection && facts.RejectedBeforeInterview))
            {
                problems.Add($"{seed.Key}: application flags not backfilled correctly");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        // Second dry run reports zero.
        var cleanPostings = await service.ReconcilePostingsAsync(dryRun: true);
        var cleanApplications = await service.ReconcileApplicationsAsync(dryRun: true);
        Assert.Equal(0, cleanPostings.PostingsUpdated);
        Assert.Equal(0, cleanApplications.ApplicationsUpdated);
    }

    private static async Task<long> CountMissingAsync(IMongoDatabase db)
    {
        // Counts documents that gained any of the five fields. After a dry run this must still be zero.
        var postings = await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Exists("has_application"));
        var applications = await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName)
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Or(
                Builders<BsonDocument>.Filter.Exists("has_offer"),
                Builders<BsonDocument>.Filter.Exists("has_rejection"),
                Builders<BsonDocument>.Filter.Exists("rejected_before_interview")));
        return postings + applications;
    }
}
