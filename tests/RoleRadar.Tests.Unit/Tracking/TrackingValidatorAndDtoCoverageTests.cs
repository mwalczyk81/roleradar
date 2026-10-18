using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class TrackingValidatorAndDtoCoverageTests
{
    [Fact]
    public void RecordCompanyCommandValidator_ValidatesAllRules()
    {
        var validator = new RecordCompanyCommandValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        // Name required & max 150
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "")));
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "   ")));
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: new string('a', 151))));

        // Description max 300
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "Acme", Description: new string('d', 301))));

        // ParentCompany max 150
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "Acme", ParentCompany: new string('p', 151))));

        // Headquarters max 150
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "Acme", Headquarters: new string('h', 151))));

        // Tags count max 20
        var tooManyTags = Enumerable.Range(0, 21).Select(i => $"tag{i}").ToList();
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "Acme", CategoryTags: tooManyTags)));

        // Tag length max 50
        Assert.NotNull(validator.Validate(new RecordCompanyCommand(Name: "Acme", CategoryTags: [new string('t', 51)])));

        // Valid command returns null
        var valid = new RecordCompanyCommand(
            Name: "Acme Corp",
            Description: "Valid description",
            ParentCompany: "Acme Holdings",
            Headquarters: "Chicago, IL",
            CategoryTags: ["fintech", "remote"]);
        Assert.Null(validator.Validate(valid));
    }

    [Fact]
    public void RecordApplicationCommandValidator_ValidatesAllRules()
    {
        var validator = new RecordApplicationCommandValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        var postingId = Guid.CreateVersion7();

        // Empty posting ID
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: Guid.Empty, Action: "create")));

        // Empty action or unknown action
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "")));
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "invalid_action")));

        // Create action: missing or invalid submitted date
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "create", SubmittedDate: null)));
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "create", SubmittedDate: "invalid-date")));
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "create", SubmittedDate: "10/18/2026")));

        // Create action: missing or invalid resume variant
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: null)));
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: "custom")));

        // Create action: overrideDecided requires non-empty overrideReason
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: "tailored", OverrideDecided: true, OverrideReason: null)));
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: "tailored", OverrideDecided: true, OverrideReason: "   ")));

        // Create action: valid
        Assert.Null(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: "tailored")));
        Assert.Null(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: "as_is", OverrideDecided: true, OverrideReason: "Re-posted role")));

        // Append_event action: missing event
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(PostingId: postingId, Action: "append_event", Event: null)));

        // Append_event action: invalid eventType
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "append_event", Event: new ApplicationEventInputDto("invalid_type", "2026-10-18"))));

        // Append_event action: missing or invalid eventDate
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "append_event", Event: new ApplicationEventInputDto("interview", ""))));
        Assert.NotNull(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "append_event", Event: new ApplicationEventInputDto("interview", "2026/10/18"))));

        // Append_event action: valid
        Assert.Null(validator.Validate(new RecordApplicationCommand(
            PostingId: postingId, Action: "append_event", Event: new ApplicationEventInputDto("interview", "2026-10-18", "recruiter", "Phone screen"))));
    }

    [Fact]
    public void PostingSignal_Constructor_ValidatesInvariants()
    {
        var id = Guid.CreateVersion7();
        var postingId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Id empty
        Assert.Throws<ArgumentException>(() => new PostingSignal(Guid.Empty, "owner-1", postingId, SignalType.Repost, "evidence", now));

        // OwnerId empty
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "", postingId, SignalType.Repost, "evidence", now));
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "   ", postingId, SignalType.Repost, "evidence", now));

        // PostingId empty
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "owner-1", Guid.Empty, SignalType.Repost, "evidence", now));

        // Evidence empty
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "owner-1", postingId, SignalType.Repost, "", now));
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "owner-1", postingId, SignalType.Repost, "   ", now));

        // Evidence too long
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "owner-1", postingId, SignalType.Repost, new string('e', 501), now));

        // Invalid deadline format
        Assert.Throws<ArgumentException>(() => new PostingSignal(id, "owner-1", postingId, SignalType.Deadline, "closing soon", now, "2026/10/18"));

        // Valid signal with deadline
        var signal = new PostingSignal(id, "owner-1", postingId, SignalType.Deadline, "closing soon", now, "2026-10-31");
        Assert.Equal("2026-10-31", signal.DeadlineDate);
        Assert.Equal(SignalType.Deadline, signal.SignalType);
        Assert.Equal("closing soon", signal.Evidence);
    }

    [Fact]
    public void Posting_DomainEntity_ValidatesLimitsAndSupportsUpdates()
    {
        var ownerId = "owner-1";
        var companyId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Title limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, new string('t', 201)));

        // URL limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", originalUrl: new string('u', 1001)));

        // AtsTenant limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", atsTenant: new string('a', 101)));

        // RequisitionId limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", requisitionId: new string('r', 101)));

        // Location limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", location: new string('l', 151)));

        // Compensation limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", compensation: new Compensation(Text: new string('c', 301))));

        // CultureSummary limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", cultureSummary: new string('s', 501)));

        // DispositionReason limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", dispositionReason: new string('d', 501)));

        // Score range limits
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", score: -0.1m));
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", score: 10.1m));

        // DuplicateOfId cannot equal Id
        var assignedId = Guid.CreateVersion7();
        Assert.Throws<ArgumentException>(() => Posting.Create(ownerId, companyId, "Staff Engineer", id: assignedId, duplicateOfId: assignedId));

        // Create valid posting with initial decided status (e.g. ScreenedOut)
        var screenedPosting = Posting.Create(
            ownerId, companyId, "Staff Engineer",
            disposition: PostingDisposition.ScreenedOut,
            now: now);
        Assert.NotNull(screenedPosting.DecidedAt);

        // Posting update and dedup keys
        var posting = Posting.Create(ownerId, companyId, "Senior Engineer", now: now);

        // ApplyExplicitUpdate coverage
        var duplicatePostingId = Guid.CreateVersion7();
        var changes = posting.ApplyExplicitUpdate(
            companyId: Guid.CreateVersion7(),
            title: "Staff Engineer",
            originalUrl: "https://example.com/job/123",
            sourceKind: SourceKind.Aggregator,
            atsTenant: "greenhouse-acme",
            requisitionId: "REQ-999",
            location: "Remote - US",
            remoteType: RemoteType.Remote,
            compensation: new Compensation(150000, 180000, "USD", "150k-180k"),
            postedDate: "2026-10-15",
            score: 8.5m,
            cultureLevel: CultureLevel.HighRisk,
            cultureSummary: "Great engineering culture",
            duplicateOfId: duplicatePostingId,
            screeningNoteId: Guid.CreateVersion7(),
            timestamp: now.AddMinutes(5));

        Assert.Contains("title", changes);
        Assert.Contains("ats_tenant", changes);
        Assert.Contains("requisition_id", changes);
        Assert.Contains("duplicate_of_id", changes);
        Assert.Contains("score", changes);
        Assert.Equal("greenhouse-acme|REQ-999", posting.DedupKeyA);

        // Dedup key C when tenant is removed but req ID stays
        posting.ApplyExplicitUpdate(
            companyId: null,
            title: null,
            originalUrl: null,
            sourceKind: null,
            atsTenant: "",
            requisitionId: "REQ-999",
            location: null,
            remoteType: null,
            compensation: null,
            postedDate: null,
            score: null,
            cultureLevel: null,
            cultureSummary: null,
            duplicateOfId: null,
            screeningNoteId: null,
            timestamp: now.AddMinutes(10));
        Assert.Null(posting.DedupKeyA);
        Assert.NotNull(posting.DedupKeyC);

        // ApplyExplicitUpdate score out of bounds
        Assert.Throws<ArgumentException>(() => posting.ApplyExplicitUpdate(
            null, null, null, null, null, null, null, null, null, null,
            score: 11.0m, null, null, null, null, now));

        // ApplyExplicitUpdate duplicateOfId == posting.Id
        Assert.Throws<ArgumentException>(() => posting.ApplyExplicitUpdate(
            null, null, null, null, null, null, null, null, null, null,
            score: null, null, null, duplicateOfId: posting.Id, null, now));

        // UpgradeIdentifiers with only req id
        posting.UpgradeIdentifiers(null, "REQ-1000", now.AddMinutes(15));
        Assert.Equal($"{posting.CompanyId:D}|REQ-1000", posting.DedupKeyC);

        // SynchronizeFromApplicationEvent long reason truncates
        var veryLongReason = new string('r', 600);
        posting.SynchronizeFromApplicationEvent(PostingDisposition.Rejected, veryLongReason, now.AddMinutes(20));
        Assert.Equal(500, posting.DispositionReason!.Length);

        // UpdateLastVerified
        posting.UpdateLastVerified(now.AddMinutes(25));
        Assert.Equal(now.AddMinutes(25), posting.LastVerifiedAt);

        // Rehydrate
        var rehydrated = Posting.Rehydrate(
            posting.Id, posting.OwnerId, posting.CompanyId, posting.Title, posting.NormalizedTitle,
            posting.OriginalUrl, posting.NormalizedUrl, posting.SourceKind, posting.AtsTenant,
            posting.RequisitionId, posting.DedupKeyA, posting.DedupKeyB, posting.DedupKeyC,
            posting.Location, posting.RemoteType, posting.Compensation, posting.PostedDate,
            posting.FirstSeenAt, posting.LastVerifiedAt, posting.ClosedDate, posting.Score,
            posting.CultureLevel, posting.CultureSummary, posting.Disposition, posting.DispositionReason,
            posting.DispositionCriterionId, posting.ResurfacePolicy, posting.DecidedAt,
            posting.DuplicateOfId, posting.ScreeningNoteId, posting.VersionToken, posting.CreatedAt, posting.UpdatedAt);
        Assert.Equal(posting.Id, rehydrated.Id);
    }

    [Fact]
    public void Company_And_CareerSource_DomainEntities_ValidateLimits()
    {
        var ownerId = "owner-1";
        var now = DateTimeOffset.UtcNow;

        // Company.Create validation
        Assert.Throws<ArgumentException>(() => Company.Create(ownerId, new string('n', 151)));
        Assert.Throws<ArgumentException>(() => Company.Create(ownerId, "Acme", parentCompany: new string('p', 151)));
        Assert.Throws<ArgumentException>(() => Company.Create(ownerId, "Acme", headquarters: new string('h', 151)));

        var company = Company.Create(ownerId, "Acme Corp", "Short desc", now: now);

        // Company.Update validation
        Assert.Throws<ArgumentException>(() => company.Update(name: ""));
        Assert.Throws<ArgumentException>(() => company.Update(name: new string('n', 151)));
        Assert.Throws<ArgumentException>(() => company.Update(description: new string('d', 301)));
        Assert.Throws<ArgumentException>(() => company.Update(parentCompany: new string('p', 151)));
        Assert.Throws<ArgumentException>(() => company.Update(headquarters: new string('h', 151)));
        Assert.Throws<ArgumentException>(() => company.Update(lastCheckResult: new string('r', 301)));

        company.Update(
            name: "Acme Corp International",
            description: "Updated description",
            parentCompany: "Global Holding Corp",
            ownershipType: OwnershipType.Public,
            headquarters: "New York, NY",
            categoryTags: ["enterprise", "saas"],
            status: CompanyStatus.Target,
            lastCheckedAt: now,
            lastCheckResult: "Found 5 jobs",
            now: now);
        Assert.Equal("Acme Corp International", company.Name);
        Assert.Equal(CompanyStatus.Target, company.Status);

        // Company.Rehydrate
        var rehydratedCo = Company.Rehydrate(
            company.Id, company.OwnerId, company.Name, company.NormalizedName,
            company.Description, company.ParentCompany, company.OwnershipType,
            company.Headquarters, company.CategoryTags, company.Status,
            company.LastCheckedAt, company.LastCheckResult, company.VersionToken,
            company.CreatedAt, company.UpdatedAt);
        Assert.Equal(company.Id, rehydratedCo.Id);

        // CareerSource validation
        Assert.Throws<ArgumentException>(() => CareerSource.Create(ownerId, Guid.Empty, "Greenhouse"));
        Assert.Throws<ArgumentException>(() => CareerSource.Create(ownerId, company.Id, "Greenhouse", tenant: new string('t', 101)));
        Assert.Throws<ArgumentException>(() => CareerSource.Create(ownerId, company.Id, "Greenhouse", slug: new string('s', 151)));
        Assert.Throws<ArgumentException>(() => CareerSource.Create(ownerId, company.Id, "Greenhouse", notes: new string('n', 501)));

        var source = CareerSource.Create(ownerId, company.Id, "Greenhouse", "acme", "careers");

        Assert.Throws<ArgumentException>(() => source.Update(tenant: new string('t', 101)));
        Assert.Throws<ArgumentException>(() => source.Update(slug: new string('s', 151)));
        Assert.Throws<ArgumentException>(() => source.Update(notes: new string('n', 501)));

        source.Update(
            atsType: "Lever",
            tenant: "acmelever",
            slug: "jobs",
            fetchMethod: FetchMethod.Browser,
            notes: "Direct career portal",
            isActive: true,
            now: now);
        Assert.Equal("Lever", source.AtsType);
        Assert.Equal(FetchMethod.Browser, source.FetchMethod);

        // CareerSource.Rehydrate
        var rehydratedCs = CareerSource.Rehydrate(
            source.Id, source.OwnerId, source.CompanyId, source.AtsType,
            source.Tenant, source.Slug, source.FetchMethod, source.Notes,
            source.IsActive, source.VersionToken, source.CreatedAt, source.UpdatedAt);
        Assert.Equal(source.Id, rehydratedCs.Id);
    }

    [Fact]
    public async Task TrackerLogCommandHandler_ValidatesAllInputRules()
    {
        var clock = new SystemUtcClock();
        var handler = new TrackerLogCommandHandler(new FakeTrackingContext(), clock);

        // Missing action
        var emptyActionRes = await handler.HandleAsync(new TrackerLogCommand(Action: ""));
        Assert.False(emptyActionRes.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, emptyActionRes.Error!.Value.Code);

        // Unsupported action
        var unsupportedRes = await handler.HandleAsync(new TrackerLogCommand(Action: "unsupported_action"));
        Assert.False(unsupportedRes.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, unsupportedRes.Error!.Value.Code);

        // search_run validation without started_at
        var searchRunRes = await handler.HandleAsync(new TrackerLogCommand(Action: "search_run"));
        Assert.False(searchRunRes.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, searchRunRes.Error!.Value.Code);

        // Criterion revision validations: missing key, category, value, provenance, effectiveFrom
        Assert.False((await handler.HandleAsync(new TrackerLogCommand(Action: "criterion_revision", Key: ""))).IsSuccess);
        Assert.False((await handler.HandleAsync(new TrackerLogCommand(Action: "criterion_revision", Key: "k1", Category: "invalid_cat"))).IsSuccess);
        Assert.False((await handler.HandleAsync(new TrackerLogCommand(Action: "criterion_revision", Key: "k1", Category: "weight", Value: ""))).IsSuccess);
        Assert.False((await handler.HandleAsync(new TrackerLogCommand(Action: "criterion_revision", Key: "k1", Category: "weight", Value: "10", Provenance: "invalid_prov"))).IsSuccess);
        Assert.False((await handler.HandleAsync(new TrackerLogCommand(Action: "criterion_revision", Key: "k1", Category: "weight", Value: "10", Provenance: "stated", EffectiveFrom: "bad_date"))).IsSuccess);

        // Criterion deactivate: missing key
        Assert.False((await handler.HandleAsync(new TrackerLogCommand(Action: "criterion_deactivate", Key: ""))).IsSuccess);
    }

    [Fact]
    public void PositionalDtos_And_AuditSummaries_ExerciseAllPropertiesAndEquality()
    {
        var id = Guid.CreateVersion7();
        var companyId = Guid.CreateVersion7();
        var token = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // PostingCandidate & PostingDifference
        var candidate = new PostingCandidate(id, "Title", PostingDisposition.Lead, "Lead note");
        Assert.Equal(id, candidate.Id);
        Assert.Equal("Title", candidate.Title);
        Assert.Equal(PostingDisposition.Lead, candidate.Disposition);
        Assert.Equal("Lead note", candidate.Reason);
        Assert.Equal(candidate, candidate with { });

        var diff = new PostingDifference("location", "Remote", "Onsite");
        Assert.Equal("location", diff.Field);
        Assert.Equal("Remote", diff.StoredValue);
        Assert.Equal("Onsite", diff.SuppliedValue);
        Assert.Equal(diff, diff with { });

        // PostingSummaryItemDto
        var postingSummary = new PostingSummaryItemDto(
            id, companyId, "Title", "title", "lead", "low", "never", token, "Chicago", "remote", 9.0m, now);
        Assert.Equal(id, postingSummary.Id);
        Assert.Equal(companyId, postingSummary.CompanyId);
        Assert.Equal("Title", postingSummary.Title);
        Assert.Equal("title", postingSummary.NormalizedTitle);
        Assert.Equal("lead", postingSummary.Disposition);
        Assert.Equal("low", postingSummary.GhostLikelihood);
        Assert.Equal("never", postingSummary.ResurfacePolicy);
        Assert.Equal(token, postingSummary.VersionToken);
        Assert.Equal("Chicago", postingSummary.Location);
        Assert.Equal("remote", postingSummary.RemoteType);
        Assert.Equal(9.0m, postingSummary.Score);
        Assert.Equal(now, postingSummary.LastVerifiedAt);
        Assert.Equal(postingSummary, postingSummary with { });

        // DecidedPostingSummaryItemDto
        var decidedSummary = new DecidedPostingSummaryItemDto(id, companyId, "Title", "rejected", "culture fit", "never", now);
        Assert.Equal(id, decidedSummary.Id);
        Assert.Equal("rejected", decidedSummary.Disposition);
        Assert.Equal("culture fit", decidedSummary.DispositionReason);
        Assert.Equal(decidedSummary, decidedSummary with { });

        // CareerSourceSummaryItemDto
        var csSummary = new CareerSourceSummaryItemDto(id, companyId, "Greenhouse", "tenant", "slug", "api", "notes", true, token);
        Assert.Equal(id, csSummary.Id);
        Assert.Equal("Greenhouse", csSummary.AtsType);
        Assert.Equal("tenant", csSummary.Tenant);
        Assert.Equal("slug", csSummary.Slug);
        Assert.Equal("api", csSummary.FetchMethod);
        Assert.Equal("notes", csSummary.Notes);
        Assert.True(csSummary.IsActive);
        Assert.Equal(token, csSummary.VersionToken);
        Assert.Equal(csSummary, csSummary with { });

        // CompanySummaryItemDto
        var companySummary = new CompanySummaryItemDto(
            id, "Acme", "Desc", "Parent", "public", "HQ", "neutral", ["tag1"], [csSummary], now, "Result", token, now, now);
        Assert.Equal(id, companySummary.Id);
        Assert.Equal("Acme", companySummary.Name);
        Assert.Equal("Desc", companySummary.Description);
        Assert.Equal("Parent", companySummary.ParentCompany);
        Assert.Equal("public", companySummary.OwnershipType);
        Assert.Equal("HQ", companySummary.Headquarters);
        Assert.Equal("neutral", companySummary.Status);
        Assert.Single(companySummary.CategoryTags);
        Assert.Single(companySummary.CareerSources);
        Assert.Equal(now, companySummary.LastCheckedAt);
        Assert.Equal("Result", companySummary.LastCheckResult);
        Assert.Equal(token, companySummary.VersionToken);
        Assert.Equal(companySummary, companySummary with { });

        // CriterionSummaryItemDto
        var critSummary = new CriterionSummaryItemDto(
            id, "remote_work", "hard_skip", "must be remote", "stated", now, null, true, token, now);
        Assert.Equal(id, critSummary.Id);
        Assert.Equal("remote_work", critSummary.Key);
        Assert.Equal("hard_skip", critSummary.Category);
        Assert.Equal("must be remote", critSummary.Value);
        Assert.Equal("stated", critSummary.Provenance);
        Assert.Equal(now, critSummary.EffectiveFrom);
        Assert.Null(critSummary.EffectiveTo);
        Assert.True(critSummary.IsActive);
        Assert.Equal(token, critSummary.VersionToken);
        Assert.Equal(critSummary, critSummary with { });

        // TrackerQueryResultDto & TrackerQueryRequest
        var queryDto = new TrackerQueryResultDto("postings", 1, 50, 100, [postingSummary]);
        Assert.Equal("postings", queryDto.Entity);
        Assert.Equal(1, queryDto.Page);
        Assert.Equal(50, queryDto.PageSize);
        Assert.Equal(100, queryDto.TotalCount);
        Assert.Single(queryDto.Items);
        Assert.Equal(queryDto, queryDto with { });

        var queryReq = new TrackerQueryRequest(
            Entity: "postings",
            PostingId: id,
            CompanyId: companyId,
            Disposition: "lead",
            Status: "target",
            Tag: "fintech",
            Name: "Acme",
            IncludeRetired: true,
            IncludeStale: true,
            StaleAfterDays: 14,
            EffectiveDate: "2026-10-18",
            DedupKey: "acme|123",
            Page: 2,
            PageSize: 25,
            OwnerId: "owner-1");
        Assert.Equal("postings", queryReq.Entity);
        Assert.Equal(id, queryReq.PostingId);
        Assert.Equal(companyId, queryReq.CompanyId);
        Assert.Equal(14, queryReq.StaleAfterDays);
        Assert.Equal(queryReq, queryReq with { });

        // TrackerLogCommand & ResultDto
        var logCmd = new TrackerLogCommand(
            Action: "search_run",
            Key: "k1",
            Category: "c1",
            Value: "v1",
            Provenance: "p1",
            EffectiveFrom: "2026-10-18",
            VersionToken: token,
            StartedAt: "2026-10-18T10:00:00Z",
            NewPostingsCount: 5,
            ReportNoteId: id,
            Notes: "test notes",
            OwnerId: "owner-1");
        Assert.Equal("search_run", logCmd.Action);
        Assert.Equal(5, logCmd.NewPostingsCount);
        Assert.Equal(id, logCmd.ReportNoteId);
        Assert.Equal(logCmd, logCmd with { });

        var logResult = new TrackerLogResultDto("criterion_revision", id, "success", now);
        Assert.Equal("criterion_revision", logResult.Action);
        Assert.Equal(id, logResult.RecordId);
        Assert.Equal("success", logResult.Status);
        Assert.Equal(now, logResult.UpdatedAtUtc);
        Assert.Equal(logResult, logResult with { });

        // Audit Summaries
        var postAudit = new PostingAuditSummary(id, ["title", "location"], "lead", "applied", "applied today", now);
        Assert.Equal(id, postAudit.PostingId);
        Assert.Equal("lead", postAudit.PriorDisposition);
        Assert.Equal("applied", postAudit.NewDisposition);
        Assert.Equal(postAudit, postAudit with { });

        var compAudit = new CompanyAuditSummary(id, ["name"], "company_create", now);
        Assert.Equal(id, compAudit.CompanyId);
        Assert.Equal("company_create", compAudit.Action);
        Assert.Equal(compAudit, compAudit with { });

        var critAudit = new CriterionAuditSummary(id, "key1", "criterion_create", now);
        Assert.Equal(id, critAudit.CriterionId);
        Assert.Equal("key1", critAudit.Key);
        Assert.Equal(critAudit, critAudit with { });

        var appAudit = new ApplicationAuditSummary(id, companyId, "create", now);
        Assert.Equal(id, appAudit.ApplicationId);
        Assert.Equal(companyId, appAudit.PostingId);
        Assert.Equal("create", appAudit.Action);
        Assert.Equal(appAudit, appAudit with { });

        var contactAudit = new ContactAuditContentSummary(id, ["role"], false, now);
        Assert.Equal(id, contactAudit.ContactId);
        Assert.False(contactAudit.IsRedacted);
        Assert.Equal(contactAudit, contactAudit with { });

        // ApplicationRecordResultDto & CompanyRecordResultDto & CareerSourceInputDto
        var appRecResult = new ApplicationRecordResultDto(id, companyId, "applied", false, 1, now);
        Assert.Equal(id, appRecResult.ApplicationId);
        Assert.Equal(appRecResult, appRecResult with { });

        var csInput = new CareerSourceInputDto(id, "Greenhouse", "tenant", "slug", "api", "notes", true, token);
        Assert.Equal(id, csInput.Id);
        Assert.Equal("Greenhouse", csInput.AtsType);
        Assert.Equal(csInput, csInput with { });

        var coRecordSummary = new CompanyRecordSummaryDto(id, "Acme", "Desc", "public", "neutral", token, 2);
        Assert.Equal(id, coRecordSummary.Id);
        Assert.Equal(coRecordSummary, coRecordSummary with { });

        var coRecordResult = new CompanyRecordResultDto(coRecordSummary, "created");
        Assert.Equal("created", coRecordResult.Status);
        Assert.Equal(coRecordResult, coRecordResult with { });
    }

    private sealed class FakeTrackingContext : ITrackingTransactionContext
    {
        public ICompanyRepository Companies => throw new NotImplementedException();
        public IPostingRepository Postings => throw new NotImplementedException();
        public ICriterionRepository Criteria => new FakeCriterionRepository();
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();

        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeCriterionRepository : ICriterionRepository
    {
        public Task<Result<Criterion?>> GetActiveByKeyAsync(string ownerId, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Criterion?>.Success(null));

        public Task<Result<Criterion?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<Criterion?>.Success(null));

        public Task<Result<bool>> UpsertCriterionAsync(Criterion criterion, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<bool>.Success(true));

        public Task<Result<IReadOnlyList<Criterion>>> ListActiveAsync(string ownerId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<Criterion>>.Success([]));

        public Task<Result<IReadOnlyList<Criterion>>> GetEffectiveAtAsync(string ownerId, DateTimeOffset effectiveDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<Criterion>>.Success([]));
    }
}
