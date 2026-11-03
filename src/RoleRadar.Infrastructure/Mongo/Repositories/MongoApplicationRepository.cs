using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoApplicationRepository : IApplicationRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _applications;
    private readonly IMongoCollection<BsonDocument> _applicationEvents;

    public MongoApplicationRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _applications = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        _applicationEvents = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName);
    }

    public async Task<Result<ApplicationEntity?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _applications.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _applications.Find(filter).FirstOrDefaultAsync(cancellationToken);

        return Result<ApplicationEntity?>.Success(doc is null ? null : MapApplication(doc));
    }

    public async Task<Result<ApplicationEntity?>> GetByPostingIdAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("posting_id", new BsonBinaryData(postingId, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _applications.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _applications.Find(filter).FirstOrDefaultAsync(cancellationToken);

        return Result<ApplicationEntity?>.Success(doc is null ? null : MapApplication(doc));
    }

    public async Task<Result<bool>> UpsertApplicationAsync(ApplicationEntity application, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", application.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(application.Id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;

        // Resolve posting and company details for denormalized read model
        var postings = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        var postFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", application.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(application.PostingId, GuidRepresentation.Standard)));

        var postingDoc = session is not null
            ? await postings.Find(session, postFilter).FirstOrDefaultAsync(cancellationToken)
            : await postings.Find(postFilter).FirstOrDefaultAsync(cancellationToken);

        string postingTitle = postingDoc is not null && postingDoc.Contains("title") && !postingDoc["title"].IsBsonNull
            ? postingDoc["title"].AsString
            : string.Empty;

        Guid companyId = postingDoc is not null && postingDoc.Contains("company_id") && !postingDoc["company_id"].IsBsonNull
            ? postingDoc["company_id"].AsGuid
            : Guid.Empty;

        string companyName = postingDoc is not null && postingDoc.Contains("company_name") && !postingDoc["company_name"].IsBsonNull
            ? postingDoc["company_name"].AsString
            : string.Empty;

        if (string.IsNullOrWhiteSpace(companyName) && companyId != Guid.Empty)
        {
            var companies = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
            var compFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("owner_id", application.OwnerId),
                Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(companyId, GuidRepresentation.Standard)));
            var compDoc = session is not null
                ? await companies.Find(session, compFilter).FirstOrDefaultAsync(cancellationToken)
                : await companies.Find(compFilter).FirstOrDefaultAsync(cancellationToken);
            if (compDoc is not null && compDoc.Contains("name") && !compDoc["name"].IsBsonNull)
            {
                companyName = compDoc["name"].AsString;
            }
        }

        // Query events to compute milestones
        var eventFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", application.OwnerId),
            Builders<BsonDocument>.Filter.Eq("application_id", new BsonBinaryData(application.Id, GuidRepresentation.Standard)));

        var events = session is not null
            ? await _applicationEvents.Find(session, eventFilter).ToListAsync(cancellationToken)
            : await _applicationEvents.Find(eventFilter).ToListAsync(cancellationToken);

        var (hasInterview, firstResponseAt, lastEventAt) = ComputeApplicationMilestones(events, application.CreatedAt.UtcDateTime);

        var doc = SerializeApplication(
            application,
            postingTitle,
            companyId,
            companyName,
            hasInterview,
            firstResponseAt,
            lastEventAt);

        var result = session is not null
            ? await _applications.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken)
            : await _applications.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);

        return Result<bool>.Success(result.IsAcknowledged);
    }

    public async Task<Result<bool>> AppendEventAsync(ApplicationEvent appEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appEvent);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var doc = SerializeApplicationEvent(appEvent);
        var session = MongoNoteRepository.CurrentSession;
        if (session is not null)
        {
            await _applicationEvents.InsertOneAsync(session, doc, cancellationToken: cancellationToken);
        }
        else
        {
            await _applicationEvents.InsertOneAsync(doc, cancellationToken: cancellationToken);
        }

        // Recompute milestones and update parent application
        var eventFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", appEvent.OwnerId),
            Builders<BsonDocument>.Filter.Eq("application_id", new BsonBinaryData(appEvent.ApplicationId, GuidRepresentation.Standard)));

        var allEvents = session is not null
            ? await _applicationEvents.Find(session, eventFilter).ToListAsync(cancellationToken)
            : await _applicationEvents.Find(eventFilter).ToListAsync(cancellationToken);

        var appFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", appEvent.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(appEvent.ApplicationId, GuidRepresentation.Standard)));

        var appDoc = session is not null
            ? await _applications.Find(session, appFilter).FirstOrDefaultAsync(cancellationToken)
            : await _applications.Find(appFilter).FirstOrDefaultAsync(cancellationToken);

        var defaultCreated = appDoc is not null && appDoc.Contains("created_at")
            ? appDoc["created_at"].ToUniversalTime()
            : appEvent.CreatedAt.UtcDateTime;

        var (hasInterview, firstResponseAt, lastEventAt) = ComputeApplicationMilestones(allEvents, defaultCreated);

        BsonValue firstResponseBson = firstResponseAt.HasValue ? BsonDateTime.Create(firstResponseAt.Value) : BsonNull.Value;
        BsonValue lastEventBson = lastEventAt.HasValue ? BsonDateTime.Create(lastEventAt.Value) : BsonNull.Value;
        var update = Builders<BsonDocument>.Update
            .Set("has_interview", hasInterview)
            .Set("first_response_at", firstResponseBson)
            .Set("has_first_response", firstResponseAt.HasValue)
            .Set("last_event_at", lastEventBson)
            .Set("has_last_event", lastEventAt.HasValue);

        if (session is not null)
        {
            await _applications.UpdateOneAsync(session, appFilter, update, cancellationToken: cancellationToken);
        }
        else
        {
            await _applications.UpdateOneAsync(appFilter, update, cancellationToken: cancellationToken);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<ApplicationEvent>>> GetEventsAsync(string ownerId, Guid applicationId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("application_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard)));

        var sort = Builders<BsonDocument>.Sort.Ascending("event_date").Ascending("created_at");

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _applicationEvents.Find(session, filter).Sort(sort).ToListAsync(cancellationToken)
            : await _applicationEvents.Find(filter).Sort(sort).ToListAsync(cancellationToken);

        var list = docs.Select(MapApplicationEvent).ToList();
        return Result<IReadOnlyList<ApplicationEvent>>.Success(list);
    }

    public async Task<Result<IReadOnlyList<ApplicationEntity>>> ListApplicationsAsync(string ownerId, string? outcome = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filterBuilder = Builders<BsonDocument>.Filter;
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            filterBuilder.Eq("owner_id", ownerId)
        };

        if (!string.IsNullOrWhiteSpace(outcome) && TrackingEnumExtensions.TryParseApplicationOutcome(outcome, out var parsedOutcome))
        {
            filters.Add(filterBuilder.Eq("outcome", parsedOutcome.ToSnakeCase()));
        }

        var filter = filterBuilder.And(filters);
        var skip = Math.Max(0, (page - 1) * pageSize);
        var limit = Math.Clamp(pageSize, 1, 200);

        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _applications.Find(session, filter).SortByDescending(d => d["submitted_date"]).Skip(skip).Limit(limit).ToListAsync(cancellationToken)
            : await _applications.Find(filter).SortByDescending(d => d["submitted_date"]).Skip(skip).Limit(limit).ToListAsync(cancellationToken);

        var list = docs.Select(MapApplication).ToList();
        return Result<IReadOnlyList<ApplicationEntity>>.Success(list);
    }

    public async Task<Result<int>> CountApplicationsAsync(string ownerId, string? outcome = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filterBuilder = Builders<BsonDocument>.Filter;
        var filters = new List<FilterDefinition<BsonDocument>>
        {
            filterBuilder.Eq("owner_id", ownerId)
        };

        if (!string.IsNullOrWhiteSpace(outcome) && TrackingEnumExtensions.TryParseApplicationOutcome(outcome, out var parsedOutcome))
        {
            filters.Add(filterBuilder.Eq("outcome", parsedOutcome.ToSnakeCase()));
        }

        var filter = filterBuilder.And(filters);
        var session = MongoNoteRepository.CurrentSession;
        var count = session is not null
            ? await _applications.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : await _applications.CountDocumentsAsync(filter, cancellationToken: cancellationToken);

        return Result<int>.Success((int)count);
    }

    internal static BsonDocument SerializeApplication(
        ApplicationEntity application,
        string postingTitle = "",
        Guid? companyId = null,
        string companyName = "",
        bool hasInterview = false,
        DateTime? firstResponseAt = null,
        DateTime? lastEventAt = null)
    {
        var desiredSalaryDoc = new BsonDocument
        {
            ["min"] = application.DesiredSalary.Min.HasValue ? BsonDecimal128.Create(application.DesiredSalary.Min.Value) : BsonNull.Value,
            ["max"] = application.DesiredSalary.Max.HasValue ? BsonDecimal128.Create(application.DesiredSalary.Max.Value) : BsonNull.Value,
            ["currency"] = application.DesiredSalary.Currency
        };

        var hasFirstResponse = firstResponseAt.HasValue;
        var hasLastEvent = lastEventAt.HasValue;

        DateTime appliedAtUtc = DeriveAppliedAt(application.SubmittedDate, application.CreatedAt.UtcDateTime);

        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(application.Id, GuidRepresentation.Standard),
            ["owner_id"] = application.OwnerId,
            ["posting_id"] = new BsonBinaryData(application.PostingId, GuidRepresentation.Standard),
            ["company_id"] = companyId.HasValue ? new BsonBinaryData(companyId.Value, GuidRepresentation.Standard) : BsonNull.Value,
            ["posting_title"] = postingTitle,
            ["company_name"] = companyName,
            ["submitted_date"] = application.SubmittedDate,
            ["applied_at"] = BsonDateTime.Create(appliedAtUtc),
            ["status"] = DeriveStatus(application.Outcome.ToSnakeCase()),
            ["has_interview"] = hasInterview,
            ["first_response_at"] = firstResponseAt.HasValue ? BsonDateTime.Create(firstResponseAt.Value) : BsonNull.Value,
            ["last_event_at"] = lastEventAt.HasValue ? BsonDateTime.Create(lastEventAt.Value) : BsonNull.Value,
            ["has_first_response"] = hasFirstResponse,
            ["has_last_event"] = hasLastEvent,
            ["desired_salary"] = desiredSalaryDoc,
            ["target_start_date"] = application.TargetStartDate is not null ? new BsonString(application.TargetStartDate) : BsonNull.Value,
            ["resume_variant"] = application.ResumeVariant.ToSnakeCase(),
            ["resume_pointer"] = application.ResumePointer is not null ? new BsonString(application.ResumePointer) : BsonNull.Value,
            ["referral_contact_id"] = application.ReferralContactId.HasValue
                ? new BsonBinaryData(application.ReferralContactId.Value, GuidRepresentation.Standard)
                : BsonNull.Value,
            ["confirmation_code"] = application.ConfirmationCode is not null ? new BsonString(application.ConfirmationCode) : BsonNull.Value,
            ["outcome"] = application.Outcome.ToSnakeCase(),
            ["outcome_reason"] = application.OutcomeReason is not null ? new BsonString(application.OutcomeReason) : BsonNull.Value,
            ["version_token"] = new BsonBinaryData(application.VersionToken, GuidRepresentation.Standard),
            ["created_at"] = BsonDateTime.Create(application.CreatedAt.UtcDateTime),
            ["updated_at"] = BsonDateTime.Create(application.UpdatedAt.UtcDateTime)
        };
    }

    /// <summary>
    /// Derives the read-model <c>status</c> (applied | rejected | closed) from the stored legacy
    /// <c>outcome</c> value. Unknown or missing values map to <c>applied</c>, matching <see cref="MapApplication"/>.
    /// Single implementation shared by <see cref="SerializeApplication"/> and the reconcile service.
    /// </summary>
    internal static string DeriveStatus(string? outcome)
    {
        var parsed = ApplicationOutcome.Applied;
        if (!string.IsNullOrWhiteSpace(outcome))
        {
            _ = TrackingEnumExtensions.TryParseApplicationOutcome(outcome, out parsed);
        }

        return parsed.ToSnakeCase();
    }

    /// <summary>
    /// Derives the read-model <c>applied_at</c> from <c>submitted_date</c> (yyyy-MM-dd) as UTC midnight,
    /// falling back to the supplied <paramref name="createdAtUtc"/> when the date is missing or unparseable.
    /// </summary>
    internal static DateTime DeriveAppliedAt(string? submittedDate, DateTime createdAtUtc)
    {
        return DateOnly.TryParseExact(submittedDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var subDate)
            ? subDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : createdAtUtc;
    }

    internal static (bool HasInterview, DateTime? FirstResponseAt, DateTime? LastEventAt) ComputeApplicationMilestones(
        IEnumerable<BsonDocument> events,
        DateTime defaultCreatedAt)
    {
        bool hasInterview = false;
        DateTime? firstResponseAt = null;
        DateTime? lastEventAt = null;

        // Only employer-originated events count as a "first response". follow_up and withdrawn are
        // the candidate's own actions, so they must not.
        var responseEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "interview", "offer", "offer_accepted", "offer_declined", "rejected"
        };

        foreach (var ev in events)
        {
            var evType = ev.Contains("event_type") && ev["event_type"].IsString ? ev["event_type"].AsString : string.Empty;
            if (string.Equals(evType, "interview", StringComparison.OrdinalIgnoreCase))
            {
                hasInterview = true;
            }

            // The event's own date (UTC midnight) is authoritative so that backfilled events order by
            // when they happened, not when they were inserted. created_at is only a fallback.
            DateTime evTimestamp;
            if (ev.Contains("event_date") && ev["event_date"].IsString
                && DateOnly.TryParseExact(ev["event_date"].AsString, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
            {
                evTimestamp = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            }
            else if (ev.Contains("created_at") && !ev["created_at"].IsBsonNull)
            {
                evTimestamp = ev["created_at"].ToUniversalTime();
            }
            else
            {
                evTimestamp = defaultCreatedAt;
            }

            if (lastEventAt is null || evTimestamp > lastEventAt.Value)
            {
                lastEventAt = evTimestamp;
            }

            if (responseEventTypes.Contains(evType))
            {
                if (firstResponseAt is null || evTimestamp < firstResponseAt.Value)
                {
                    firstResponseAt = evTimestamp;
                }
            }
        }

        return (hasInterview, firstResponseAt, lastEventAt);
    }

    private static ApplicationEntity MapApplication(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var postingId = doc["posting_id"].AsGuid;
        var submittedDate = doc["submitted_date"].AsString;

        var desiredSalary = new DesiredSalary();
        if (doc.Contains("desired_salary") && doc["desired_salary"].IsBsonDocument)
        {
            var salDoc = doc["desired_salary"].AsBsonDocument;
            decimal? min = salDoc.Contains("min") && !salDoc["min"].IsBsonNull ? (decimal)salDoc["min"].AsDecimal : null;
            decimal? max = salDoc.Contains("max") && !salDoc["max"].IsBsonNull ? (decimal)salDoc["max"].AsDecimal : null;
            var currency = salDoc.Contains("currency") && !salDoc["currency"].IsBsonNull ? salDoc["currency"].AsString : "USD";
            desiredSalary = new DesiredSalary(min, max, currency);
        }

        var targetStartDate = doc.Contains("target_start_date") && !doc["target_start_date"].IsBsonNull
            ? doc["target_start_date"].AsString
            : null;

        var resumeVariant = ResumeVariant.Tailored;
        if (doc.Contains("resume_variant") && !doc["resume_variant"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseResumeVariant(doc["resume_variant"].AsString, out resumeVariant);
        }

        var resumePointer = doc.Contains("resume_pointer") && !doc["resume_pointer"].IsBsonNull
            ? doc["resume_pointer"].AsString
            : null;

        Guid? referralContactId = null;
        if (doc.Contains("referral_contact_id") && !doc["referral_contact_id"].IsBsonNull)
        {
            referralContactId = doc["referral_contact_id"].AsGuid;
        }

        var confirmationCode = doc.Contains("confirmation_code") && !doc["confirmation_code"].IsBsonNull
            ? doc["confirmation_code"].AsString
            : null;

        var outcome = ApplicationOutcome.Applied;
        if (doc.Contains("outcome") && !doc["outcome"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseApplicationOutcome(doc["outcome"].AsString, out outcome);
        }

        var outcomeReason = doc.Contains("outcome_reason") && !doc["outcome_reason"].IsBsonNull
            ? doc["outcome_reason"].AsString
            : null;

        var versionToken = doc.Contains("version_token") ? doc["version_token"].AsGuid : Guid.Empty;
        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;
        var updatedAt = doc.Contains("updated_at")
            ? new DateTimeOffset(doc["updated_at"].ToUniversalTime(), TimeSpan.Zero)
            : createdAt;

        return ApplicationEntity.Rehydrate(
            id,
            ownerId,
            postingId,
            submittedDate,
            desiredSalary,
            targetStartDate,
            resumeVariant,
            resumePointer,
            referralContactId,
            confirmationCode,
            outcome,
            outcomeReason,
            versionToken,
            createdAt,
            updatedAt);
    }

    private static BsonDocument SerializeApplicationEvent(ApplicationEvent appEvent)
    {
        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(appEvent.Id, GuidRepresentation.Standard),
            ["owner_id"] = appEvent.OwnerId,
            ["application_id"] = new BsonBinaryData(appEvent.ApplicationId, GuidRepresentation.Standard),
            ["event_date"] = appEvent.EventDate,
            ["event_type"] = appEvent.EventType.ToSnakeCase(),
            ["event_source"] = appEvent.EventSource is not null ? new BsonString(appEvent.EventSource) : BsonNull.Value,
            ["notes"] = appEvent.Notes is not null ? new BsonString(appEvent.Notes) : BsonNull.Value,
            ["created_at"] = BsonDateTime.Create(appEvent.CreatedAt.UtcDateTime)
        };
    }

    private static ApplicationEvent MapApplicationEvent(BsonDocument doc)
    {
        var id = doc["_id"].AsGuid;
        var ownerId = doc["owner_id"].AsString;
        var applicationId = doc["application_id"].AsGuid;
        var eventDate = doc["event_date"].AsString;

        var eventType = ApplicationEventType.Applied;
        if (doc.Contains("event_type") && !doc["event_type"].IsBsonNull)
        {
            _ = TrackingEnumExtensions.TryParseApplicationEventType(doc["event_type"].AsString, out eventType);
        }

        var eventSource = doc.Contains("event_source") && !doc["event_source"].IsBsonNull
            ? doc["event_source"].AsString
            : null;

        var notes = doc.Contains("notes") && !doc["notes"].IsBsonNull
            ? doc["notes"].AsString
            : null;

        var createdAt = doc.Contains("created_at")
            ? new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

        return ApplicationEvent.Rehydrate(
            id,
            ownerId,
            applicationId,
            eventDate,
            eventType,
            eventSource,
            notes,
            createdAt);
    }
}
