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
    bool IsDryRun,
    int CriteriaExamined = 0,
    int CriteriaUpdated = 0);

public interface IReadModelReconciliationService
{
    Task<ReconcileResult> ReconcilePostingsAsync(bool dryRun, CancellationToken cancellationToken = default);
    Task<ReconcileResult> ReconcileApplicationsAsync(bool dryRun, CancellationToken cancellationToken = default);
    Task<ReconcileResult> ReconcileCriteriaAsync(bool dryRun, CancellationToken cancellationToken = default);
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

        // ADR 0031: has_application is true exactly when an application exists for (owner, posting).
        var applicationsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        var applicationLinks = await applicationsCollection
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Project(Builders<BsonDocument>.Projection.Include("owner_id").Include("posting_id"))
            .ToListAsync(cancellationToken);
        var postingsWithApplication = new HashSet<(string Owner, Guid PostingId)>();
        foreach (var link in applicationLinks)
        {
            if (link.Contains("owner_id") && link["owner_id"].IsString && link.Contains("posting_id") && link["posting_id"].IsGuid)
            {
                postingsWithApplication.Add((link["owner_id"].AsString, link["posting_id"].AsGuid));
            }
        }

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

            var ownerId = doc.Contains("owner_id") && doc["owner_id"].IsString ? doc["owner_id"].AsString : string.Empty;
            var expectedHasApplication = postingsWithApplication.Contains((ownerId, id));
            var currentHasApplication = doc.Contains(TrackingBsonFieldNames.Postings.HasApplication) && doc[TrackingBsonFieldNames.Postings.HasApplication].IsBoolean
                ? doc[TrackingBsonFieldNames.Postings.HasApplication].AsBoolean
                : (bool?)null;

            bool needsUpdate = currentRank != expectedRank ||
                               currentHasScore != hasScoreExpected ||
                               currentCompanyName != expectedCompanyName ||
                               currentHasApplication != expectedHasApplication;

            if (needsUpdate)
            {
                updated++;
                if (!dryRun)
                {
                    var update = Builders<BsonDocument>.Update
                        .Set("disposition_rank", expectedRank)
                        .Set("has_score", hasScoreExpected)
                        .Set("company_name", expectedCompanyName)
                        .Set(TrackingBsonFieldNames.Postings.HasApplication, expectedHasApplication);

                    await postings.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                        update,
                        cancellationToken: cancellationToken);
                }
            }
        }

        return new ReconcileResult(examined, updated, 0, 0, dryRun);
    }

    public async Task<ReconcileResult> ReconcileApplicationsAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        var appsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        var postingsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var companiesCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        var eventsCollection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);

        var allApps = await appsCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
        var allPostings = await postingsCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
        var allCompanies = await companiesCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);
        var allEvents = await eventsCollection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);

        var postingMap = allPostings.ToDictionary(
            p => p["_id"].AsGuid,
            p => (
                Title: p.Contains("title") && !p["title"].IsBsonNull ? p["title"].AsString : string.Empty,
                CompanyId: p.Contains("company_id") && !p["company_id"].IsBsonNull ? p["company_id"].AsGuid : Guid.Empty,
                CompanyName: p.Contains("company_name") && !p["company_name"].IsBsonNull ? p["company_name"].AsString : string.Empty
            ));

        var companyMap = allCompanies.ToDictionary(
            c => c["_id"].AsGuid,
            c => c.Contains("name") && !c["name"].IsBsonNull ? c["name"].AsString : string.Empty);

        var eventsByApp = allEvents
            .Where(e => e.Contains("application_id") && !e["application_id"].IsBsonNull)
            .GroupBy(e => e["application_id"].AsGuid)
            .ToDictionary(g => g.Key, g => g.ToList());

        int examined = allApps.Count;
        int updated = 0;

        foreach (var doc in allApps)
        {
            var appId = doc["_id"].AsGuid;
            var postingId = doc.Contains("posting_id") && !doc["posting_id"].IsBsonNull ? doc["posting_id"].AsGuid : Guid.Empty;

            postingMap.TryGetValue(postingId, out var postingInfo);
            var expectedPostingTitle = postingInfo.Title ?? string.Empty;

            // The posting is the source of truth for company_id (UpsertApplicationAsync copies it from there).
            // Fall back to the document's own value only when the posting is missing or has no company.
            var expectedCompanyId = postingInfo.CompanyId != Guid.Empty
                ? postingInfo.CompanyId
                : (doc.Contains("company_id") && !doc["company_id"].IsBsonNull ? doc["company_id"].AsGuid : Guid.Empty);

            string expectedCompanyName = postingInfo.CompanyName;
            if (string.IsNullOrWhiteSpace(expectedCompanyName) && expectedCompanyId != Guid.Empty && companyMap.TryGetValue(expectedCompanyId, out var cName))
            {
                expectedCompanyName = cName;
            }
            expectedCompanyName ??= string.Empty;

            eventsByApp.TryGetValue(appId, out var appEventsList);
            appEventsList ??= new List<BsonDocument>();

            DateTime defaultCreated = doc.Contains("created_at") && !doc["created_at"].IsBsonNull
                ? doc["created_at"].ToUniversalTime()
                : (doc.Contains("applied_at") && !doc["applied_at"].IsBsonNull ? doc["applied_at"].ToUniversalTime() : DateTime.UtcNow);

            var milestones = MongoApplicationRepository.ComputeApplicationMilestones(appEventsList, defaultCreated);
            var hasInterview = milestones.HasInterview;
            var firstResponseAt = milestones.FirstResponseAt;
            var lastEventAt = milestones.LastEventAt;

            // Legacy documents only carry outcome and submitted_date; derive status and applied_at with the
            // same helpers SerializeApplication uses so the two code paths cannot drift.
            var expectedStatus = MongoApplicationRepository.DeriveStatus(
                doc.Contains("outcome") && doc["outcome"].IsString ? doc["outcome"].AsString : null);
            var expectedAppliedAt = MongoApplicationRepository.DeriveAppliedAt(
                doc.Contains("submitted_date") && doc["submitted_date"].IsString ? doc["submitted_date"].AsString : null,
                defaultCreated);
            bool hasFirstResponse = firstResponseAt.HasValue;
            bool hasLastEvent = lastEventAt.HasValue;

            var currentPostingTitle = doc.Contains("posting_title") && !doc["posting_title"].IsBsonNull ? doc["posting_title"].AsString : null;
            var currentCompanyName = doc.Contains("company_name") && !doc["company_name"].IsBsonNull ? doc["company_name"].AsString : null;
            var currentHasInterview = doc.Contains("has_interview") && !doc["has_interview"].IsBsonNull ? doc["has_interview"].AsBoolean : (bool?)null;
            var currentHasFirstResponse = doc.Contains("has_first_response") && !doc["has_first_response"].IsBsonNull ? doc["has_first_response"].AsBoolean : (bool?)null;
            var currentHasLastEvent = doc.Contains("has_last_event") && !doc["has_last_event"].IsBsonNull ? doc["has_last_event"].AsBoolean : (bool?)null;
            var currentHasOffer = ReadFlag(doc, TrackingBsonFieldNames.Applications.HasOffer);
            var currentHasRejection = ReadFlag(doc, TrackingBsonFieldNames.Applications.HasRejection);
            var currentRejectedBeforeInterview = ReadFlag(doc, TrackingBsonFieldNames.Applications.RejectedBeforeInterview);

            bool firstResponseMatches = (!firstResponseAt.HasValue && (!doc.Contains("first_response_at") || doc["first_response_at"].IsBsonNull))
                || (firstResponseAt.HasValue && doc.Contains("first_response_at") && !doc["first_response_at"].IsBsonNull && Math.Abs((doc["first_response_at"].ToUniversalTime() - firstResponseAt.Value).TotalMilliseconds) < 1000);

            bool lastEventMatches = (!lastEventAt.HasValue && (!doc.Contains("last_event_at") || doc["last_event_at"].IsBsonNull))
                || (lastEventAt.HasValue && doc.Contains("last_event_at") && !doc["last_event_at"].IsBsonNull && Math.Abs((doc["last_event_at"].ToUniversalTime() - lastEventAt.Value).TotalMilliseconds) < 1000);

            var currentStatus = doc.Contains("status") && doc["status"].IsString ? doc["status"].AsString : null;
            var currentCompanyId = doc.Contains("company_id") && doc["company_id"].IsGuid ? doc["company_id"].AsGuid : Guid.Empty;
            bool appliedAtMatches = doc.Contains("applied_at") && doc["applied_at"].IsValidDateTime
                && Math.Abs((doc["applied_at"].ToUniversalTime() - expectedAppliedAt).TotalMilliseconds) < 1000;

            bool needsUpdate = currentStatus != expectedStatus
                || !appliedAtMatches
                || currentCompanyId != expectedCompanyId
                || currentPostingTitle != expectedPostingTitle
                || currentCompanyName != expectedCompanyName
                || currentHasInterview != hasInterview
                || currentHasFirstResponse != hasFirstResponse
                || currentHasLastEvent != hasLastEvent
                || currentHasOffer != milestones.HasOffer
                || currentHasRejection != milestones.HasRejection
                || currentRejectedBeforeInterview != milestones.RejectedBeforeInterview
                || !firstResponseMatches
                || !lastEventMatches;

            if (needsUpdate)
            {
                updated++;
                if (!dryRun)
                {
                    BsonValue firstRespVal = firstResponseAt.HasValue ? BsonDateTime.Create(firstResponseAt.Value) : BsonNull.Value;
                    BsonValue lastEventVal = lastEventAt.HasValue ? BsonDateTime.Create(lastEventAt.Value) : BsonNull.Value;

                    var update = Builders<BsonDocument>.Update
                        .Set("status", expectedStatus)
                        .Set("applied_at", BsonDateTime.Create(expectedAppliedAt))
                        .Set("posting_title", expectedPostingTitle)
                        .Set("company_name", expectedCompanyName)
                        .Set("has_interview", hasInterview)
                        .Set("has_first_response", hasFirstResponse)
                        .Set("has_last_event", hasLastEvent)
                        .Set("first_response_at", firstRespVal)
                        .Set("last_event_at", lastEventVal)
                        .Set(TrackingBsonFieldNames.Applications.HasOffer, milestones.HasOffer)
                        .Set(TrackingBsonFieldNames.Applications.HasRejection, milestones.HasRejection)
                        .Set(TrackingBsonFieldNames.Applications.RejectedBeforeInterview, milestones.RejectedBeforeInterview);

                    if (expectedCompanyId != Guid.Empty)
                    {
                        update = update.Set("company_id", new BsonBinaryData(expectedCompanyId, GuidRepresentation.Standard));
                    }

                    await appsCollection.UpdateOneAsync(
                        Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                        update,
                        cancellationToken: cancellationToken);
                }
            }
        }

        return new ReconcileResult(0, 0, examined, updated, dryRun);
    }

    public async Task<ReconcileResult> ReconcileCriteriaAsync(bool dryRun, CancellationToken cancellationToken = default)
    {
        var criteria = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName);
        var all = await criteria.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync(cancellationToken);

        int updated = 0;

        foreach (var doc in all)
        {
            // Criteria written before Phase 5 carry effective_to but no has_effective_to flag.
            var expected = doc.Contains("effective_to") && !doc["effective_to"].IsBsonNull;
            var current = doc.Contains("has_effective_to") && doc["has_effective_to"].IsBoolean
                ? doc["has_effective_to"].AsBoolean
                : (bool?)null;

            if (current == expected)
            {
                continue;
            }

            updated++;
            if (!dryRun)
            {
                await criteria.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", doc["_id"]),
                    Builders<BsonDocument>.Update.Set("has_effective_to", expected),
                    cancellationToken: cancellationToken);
            }
        }

        return new ReconcileResult(0, 0, 0, 0, dryRun, all.Count, updated);
    }

    private static bool? ReadFlag(BsonDocument doc, string name) =>
        doc.Contains(name) && doc[name].IsBoolean ? doc[name].AsBoolean : null;

    public static int MapDispositionRank(string disposition)
    {
        if (TrackingEnumExtensions.TryParseDisposition(disposition, out var parsed))
        {
            return MongoPostingRepository.MapDispositionRank(parsed);
        }

        throw new ArgumentOutOfRangeException(nameof(disposition), disposition, $"Unknown posting disposition '{disposition}'.");
    }
}
