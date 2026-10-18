using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class PostingDomainAndValidatorUnitTests
{
    [Fact]
    public void Posting_ValidationInvariants_ThrowArgumentExceptionWhenViolated()
    {
        var companyId = Guid.NewGuid();

        // Title empty or whitespace
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, ""));
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "   "));

        // Title > 200 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, new string('a', 201)));

        // URL > 1000 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", originalUrl: new string('a', 1001)));

        // ATS Tenant > 100 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", atsTenant: new string('a', 101)));

        // Requisition ID > 100 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", requisitionId: new string('a', 101)));

        // Location > 150 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", location: new string('a', 151)));

        // Compensation text > 300 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", compensation: new Compensation(1, 2, "USD", new string('a', 301))));

        // Culture summary > 500 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", cultureSummary: new string('a', 501)));

        // Disposition reason > 500 chars
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", dispositionReason: new string('a', 501)));

        // Score < 0.0 or > 10.0
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", score: -0.1m));
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", score: 10.1m));

        // Duplicate of self
        var selfId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Posting.Create("owner-1", companyId, "Title", id: selfId, duplicateOfId: selfId));
    }

    [Fact]
    public void Posting_UpgradeIdentifiers_UpdatesKeysCorrectly()
    {
        var companyId = Guid.NewGuid();
        var posting = Posting.Create("owner-1", companyId, "Software Engineer", atsTenant: null, requisitionId: "REQ-1");
        Assert.Equal($"{companyId:D}|REQ-1", posting.DedupKeyC);
        Assert.Null(posting.DedupKeyA);

        var now = DateTimeOffset.UtcNow;
        posting.UpgradeIdentifiers("acme", "REQ-2", now);
        Assert.Equal("acme|REQ-2", posting.DedupKeyA);
        Assert.Null(posting.DedupKeyC);

        posting.UpgradeIdentifiers(null, "REQ-3", now);
        Assert.Equal("acme|REQ-3", posting.DedupKeyA);
    }

    [Fact]
    public void GhostLikelihoodEvaluator_EvaluatesAllSignalTypesAndBrackets()
    {
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Fresh posting with no signals => low (0 - 15 => 0)
        var pFresh = Posting.Create("owner-1", companyId, "Title", now: now);
        var scoreFresh = GhostLikelihoodEvaluator.Evaluate(pFresh, [], now);
        Assert.Equal("low", scoreFresh);

        // Posting older than 60 days (+20) with verified > 7 days ago
        var p60 = Posting.Create("owner-1", companyId, "Title", now: now.AddDays(-65));
        var score60 = GhostLikelihoodEvaluator.Evaluate(p60, [], now);
        Assert.Equal("low", score60); // 20 pts => low (0..24)

        // Posting older than 90 days (+35)
        var p90 = Posting.Create("owner-1", companyId, "Title", now: now.AddDays(-95));
        var score90 = GhostLikelihoodEvaluator.Evaluate(p90, [], now);
        Assert.Equal("moderate", score90); // 35 pts => moderate (25..49)

        // Signals test:
        // Gone (+50) on non-fresh posting => high (50 pts)
        var sigGone = new PostingSignal(Guid.NewGuid(), "owner-1", p90.Id, SignalType.Gone, "404", now);
        Assert.Equal("high", GhostLikelihoodEvaluator.Evaluate(p60, [sigGone], now)); // 20 + 50 = 70 => high

        // Phantom (+40) + Gone (+50) => critical (90 pts)
        var sigPhantom = new PostingSignal(Guid.NewGuid(), "owner-1", p60.Id, SignalType.Phantom, "phantom role", now);
        Assert.Equal("critical", GhostLikelihoodEvaluator.Evaluate(p60, [sigGone, sigPhantom], now));

        // Deadline passed (+30) vs future (0)
        var pastDeadline = now.AddDays(-2).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var futureDeadline = now.AddDays(5).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var sigDeadPassed = new PostingSignal(Guid.NewGuid(), "owner-1", pFresh.Id, SignalType.Deadline, "expired", now, pastDeadline);
        var sigDeadFuture = new PostingSignal(Guid.NewGuid(), "owner-1", pFresh.Id, SignalType.Deadline, "open", now, futureDeadline);

        // 30 - 15 = 15 => low
        Assert.Equal("low", GhostLikelihoodEvaluator.Evaluate(pFresh, [sigDeadPassed], now));
        // 0 - 15 = 0 => low
        Assert.Equal("low", GhostLikelihoodEvaluator.Evaluate(pFresh, [sigDeadFuture], now));

        // Repost (+25), ParallelPosting (+20), NoSalary (+15)
        var sigRepost = new PostingSignal(Guid.NewGuid(), "owner-1", p60.Id, SignalType.Repost, "reposted", now);
        var sigParallel = new PostingSignal(Guid.NewGuid(), "owner-1", p60.Id, SignalType.ParallelPosting, "multi-city", now);
        var sigNoSal = new PostingSignal(Guid.NewGuid(), "owner-1", p60.Id, SignalType.NoSalary, "no compensation", now);

        // 20 (age) + 25 + 20 + 15 = 80 => critical
        Assert.Equal("critical", GhostLikelihoodEvaluator.Evaluate(p60, [sigRepost, sigParallel, sigNoSal], now));
    }

    [Fact]
    public void TrackingEnumExtensions_ConvertsBidirectionally()
    {
        // PostingDisposition
        Assert.Equal("lead", PostingDisposition.Lead.ToSnakeCase());
        Assert.Equal("unscored", PostingDisposition.Unscored.ToSnakeCase());
        Assert.Equal("scored", PostingDisposition.Scored.ToSnakeCase());
        Assert.Equal("applied", PostingDisposition.Applied.ToSnakeCase());
        Assert.Equal("screened_out", PostingDisposition.ScreenedOut.ToSnakeCase());
        Assert.Equal("closed", PostingDisposition.Closed.ToSnakeCase());
        Assert.Equal("rejected", PostingDisposition.Rejected.ToSnakeCase());

        Assert.True(TrackingEnumExtensions.TryParseDisposition("screened_out", out var d1) && d1 == PostingDisposition.ScreenedOut);
        Assert.True(TrackingEnumExtensions.TryParseDisposition("screenedout", out var d2) && d2 == PostingDisposition.ScreenedOut);
        Assert.True(TrackingEnumExtensions.TryParseDisposition("lead", out var d3) && d3 == PostingDisposition.Lead);
        Assert.False(TrackingEnumExtensions.TryParseDisposition("invalid_disp", out _));

        // ResurfacePolicy
        Assert.Equal("never", ResurfacePolicy.Never.ToSnakeCase());
        Assert.Equal("if_reopened", ResurfacePolicy.IfReopened.ToSnakeCase());
        Assert.True(TrackingEnumExtensions.TryParseResurfacePolicy("if_reopened", out var r1) && r1 == ResurfacePolicy.IfReopened);
        Assert.True(TrackingEnumExtensions.TryParseResurfacePolicy("ifreopened", out var r2) && r2 == ResurfacePolicy.IfReopened);
        Assert.False(TrackingEnumExtensions.TryParseResurfacePolicy("invalid_resurface", out _));

        // SourceKind
        Assert.Equal("ats", SourceKind.Ats.ToSnakeCase());
        Assert.Equal("aggregator", SourceKind.Aggregator.ToSnakeCase());
        Assert.True(TrackingEnumExtensions.TryParseSourceKind("ats", out var s1) && s1 == SourceKind.Ats);
        Assert.True(TrackingEnumExtensions.TryParseSourceKind("aggregator", out var s2) && s2 == SourceKind.Aggregator);
        Assert.False(TrackingEnumExtensions.TryParseSourceKind("invalid_source", out _));

        // RemoteType
        Assert.Equal("remote", RemoteType.Remote.ToSnakeCase());
        Assert.Equal("hybrid", RemoteType.Hybrid.ToSnakeCase());
        Assert.Equal("onsite", RemoteType.Onsite.ToSnakeCase());
        Assert.Equal("unknown", RemoteType.Unknown.ToSnakeCase());
        Assert.True(TrackingEnumExtensions.TryParseRemoteType("remote", out var rt1) && rt1 == RemoteType.Remote);
        Assert.True(TrackingEnumExtensions.TryParseRemoteType("hybrid", out var rt2) && rt2 == RemoteType.Hybrid);
        Assert.False(TrackingEnumExtensions.TryParseRemoteType("invalid_rt", out _));

        // CultureLevel
        Assert.Equal("stable", CultureLevel.Stable.ToSnakeCase());
        Assert.Equal("moderate", CultureLevel.Moderate.ToSnakeCase());
        Assert.Equal("high_risk", CultureLevel.HighRisk.ToSnakeCase());
        Assert.Equal("unknown", CultureLevel.Unknown.ToSnakeCase());
        Assert.True(TrackingEnumExtensions.TryParseCultureLevel("high_risk", out var cl1) && cl1 == CultureLevel.HighRisk);
        Assert.True(TrackingEnumExtensions.TryParseCultureLevel("highrisk", out var cl2) && cl2 == CultureLevel.HighRisk);
        Assert.False(TrackingEnumExtensions.TryParseCultureLevel("invalid_cl", out _));

        // SignalType
        Assert.Equal("repost", SignalType.Repost.ToSnakeCase());
        Assert.Equal("no_salary", SignalType.NoSalary.ToSnakeCase());
        Assert.Equal("parallel_posting", SignalType.ParallelPosting.ToSnakeCase());
        Assert.Equal("gone", SignalType.Gone.ToSnakeCase());
        Assert.Equal("phantom", SignalType.Phantom.ToSnakeCase());
        Assert.Equal("deadline", SignalType.Deadline.ToSnakeCase());
        Assert.True(TrackingEnumExtensions.TryParseSignalType("no_salary", out var st1) && st1 == SignalType.NoSalary);
        Assert.True(TrackingEnumExtensions.TryParseSignalType("nosalary", out var st2) && st2 == SignalType.NoSalary);
        Assert.True(TrackingEnumExtensions.TryParseSignalType("parallel_posting", out var st3) && st3 == SignalType.ParallelPosting);
        Assert.False(TrackingEnumExtensions.TryParseSignalType("invalid_sig", out _));
    }

    [Fact]
    public void RecordPostingCommandValidator_ValidatesCreateAndExplicitUpdate()
    {
        var validator = new RecordPostingCommandValidator();

        // Null command
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        // Explicit update without version token
        var errUpdateNoToken = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid()));
        Assert.NotNull(errUpdateNoToken);
        Assert.Equal(ErrorCode.StaleVersionToken, errUpdateNoToken.Value.Code);

        // Explicit update title validation
        var errUpdateTitleEmpty = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid(), VersionToken: Guid.NewGuid(), Title: "  "));
        Assert.NotNull(errUpdateTitleEmpty);
        Assert.Equal(ErrorCode.ValidationError, errUpdateTitleEmpty.Value.Code);

        var errUpdateTitleTooLong = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid(), VersionToken: Guid.NewGuid(), Title: new string('a', 201)));
        Assert.NotNull(errUpdateTitleTooLong);

        // Explicit update url validation
        var errUpdateUrlTooLong = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid(), VersionToken: Guid.NewGuid(), Url: new string('a', 1001)));
        Assert.NotNull(errUpdateUrlTooLong);

        // Explicit update score validation
        var errUpdateScoreLow = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid(), VersionToken: Guid.NewGuid(), Score: -0.5m));
        Assert.NotNull(errUpdateScoreLow);

        var errUpdateScoreHigh = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid(), VersionToken: Guid.NewGuid(), Score: 10.5m));
        Assert.NotNull(errUpdateScoreHigh);

        // Valid explicit update
        var okUpdate = validator.Validate(new RecordPostingCommand(PostingId: Guid.NewGuid(), VersionToken: Guid.NewGuid(), Title: "Valid Title", Score: 8.5m));
        Assert.Null(okUpdate);

        // Create mode: missing companyId
        var errCreateNoCo = validator.Validate(new RecordPostingCommand(Title: "Title"));
        Assert.NotNull(errCreateNoCo);
        Assert.Equal(ErrorCode.ValidationError, errCreateNoCo.Value.Code);

        // Create mode: empty title
        var errCreateNoTitle = validator.Validate(new RecordPostingCommand(CompanyId: Guid.NewGuid(), Title: "  "));
        Assert.NotNull(errCreateNoTitle);

        // Create mode: title too long
        var errCreateTitleTooLong = validator.Validate(new RecordPostingCommand(CompanyId: Guid.NewGuid(), Title: new string('a', 201)));
        Assert.NotNull(errCreateTitleTooLong);

        // Create mode: url too long
        var errCreateUrlTooLong = validator.Validate(new RecordPostingCommand(CompanyId: Guid.NewGuid(), Title: "Valid Title", Url: new string('a', 1001)));
        Assert.NotNull(errCreateUrlTooLong);

        // Create mode: score out of bounds
        var errCreateScore = validator.Validate(new RecordPostingCommand(CompanyId: Guid.NewGuid(), Title: "Valid Title", Score: -1.0m));
        Assert.NotNull(errCreateScore);

        // Valid create
        var okCreate = validator.Validate(new RecordPostingCommand(CompanyId: Guid.NewGuid(), Title: "Valid Title", Score: 7.0m));
        Assert.Null(okCreate);
    }

    [Fact]
    public void TransitionPostingCommandValidator_ValidatesTransitions()
    {
        var validator = new TransitionPostingCommandValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        // Missing PostingId
        var errNoId = validator.Validate(new TransitionPostingCommand(Guid.Empty, "scored", Guid.NewGuid()));
        Assert.NotNull(errNoId);

        // Missing TargetDisposition
        var errNoDisp = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "", Guid.NewGuid()));
        Assert.NotNull(errNoDisp);

        // Missing VersionToken
        var errNoToken = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "scored", Guid.Empty));
        Assert.NotNull(errNoToken);
        Assert.Equal(ErrorCode.StaleVersionToken, errNoToken.Value.Code);

        // Target 'applied' disallowed
        var errApplied = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "applied", Guid.NewGuid()));
        Assert.NotNull(errApplied);
        Assert.Equal(ErrorCode.InvalidTransition, errApplied.Value.Code);

        // Unknown target disposition
        var errUnknown = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "not_a_disp", Guid.NewGuid()));
        Assert.NotNull(errUnknown);

        // Missing reason when transitioning to screened_out
        var errNoReason = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "screened_out", Guid.NewGuid(), Reason: null));
        Assert.NotNull(errNoReason);

        // Reason too long (> 500)
        var errReasonTooLong = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "screened_out", Guid.NewGuid(), Reason: new string('a', 501)));
        Assert.NotNull(errReasonTooLong);

        // Valid transition
        var ok = validator.Validate(new TransitionPostingCommand(Guid.NewGuid(), "screened_out", Guid.NewGuid(), Reason: "Too junior"));
        Assert.Null(ok);
    }

    [Fact]
    public async Task TrackerQueryHandler_ValidatesUnsupportedEntityAndContacts()
    {
        var context = new NoOpTrackingTransactionContext();
        var clock = new SystemUtcClock();
        var handler = new TrackerQueryHandler(context, clock);

        // Null request
        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!));

        // Empty entity
        var emptyRes = await handler.HandleAsync(new TrackerQueryRequest(Entity: ""));
        Assert.False(emptyRes.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, emptyRes.Error!.Value.Code);

        // Contacts entity disallowed
        var contactRes = await handler.HandleAsync(new TrackerQueryRequest(Entity: "contacts"));
        Assert.False(contactRes.IsSuccess);
        Assert.Contains("Contacts are accessible exclusively via CLI commands", contactRes.Error!.Value.Message);

        // Unknown entity
        var unknownRes = await handler.HandleAsync(new TrackerQueryRequest(Entity: "unknown_entity"));
        Assert.False(unknownRes.IsSuccess);
        Assert.Contains("not supported", unknownRes.Error!.Value.Message);
    }

    [Fact]
    public void Company_ValidationAndUpdates_OperatesCorrectly()
    {
        Assert.Throws<ArgumentException>(() => Company.Create("owner-1", ""));
        Assert.Throws<ArgumentException>(() => Company.Create("owner-1", new string('a', 151)));
        Assert.Throws<ArgumentException>(() => Company.Create("owner-1", "Name", description: new string('a', 301)));
        Assert.Throws<ArgumentException>(() => Company.Create("owner-1", "Name", parentCompany: new string('a', 151)));
        Assert.Throws<ArgumentException>(() => Company.Create("owner-1", "Name", headquarters: new string('a', 151)));

        var c = Company.Create("owner-1", "Original Co", "Desc", "Parent", OwnershipType.Private, "HQ", ["tag1", "tag2"], CompanyStatus.Target);
        Assert.Equal("Original Co", c.Name);
        Assert.Equal("original co", c.NormalizedName);
        Assert.Equal("Desc", c.Description);
        Assert.Equal("Parent", c.ParentCompany);
        Assert.Equal(OwnershipType.Private, c.OwnershipType);
        Assert.Equal("HQ", c.Headquarters);
        Assert.Equal(2, c.CategoryTags.Count);
        Assert.Equal(CompanyStatus.Target, c.Status);

        c.Update(name: "New Co", description: "New Desc", status: CompanyStatus.Watch, lastCheckResult: "OK");
        Assert.Equal("New Co", c.Name);
        Assert.Equal("New Desc", c.Description);
        Assert.Equal(CompanyStatus.Watch, c.Status);
        Assert.Equal("OK", c.LastCheckResult);

        Assert.Throws<ArgumentException>(() => c.Update(name: ""));
        Assert.Throws<ArgumentException>(() => c.Update(description: new string('a', 301)));
        Assert.Throws<ArgumentException>(() => c.Update(parentCompany: new string('a', 151)));
        Assert.Throws<ArgumentException>(() => c.Update(headquarters: new string('a', 151)));
    }

    [Fact]
    public void CareerSource_ValidationAndLifecycle_OperatesCorrectly()
    {
        var companyId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => CareerSource.Create("owner-1", Guid.Empty, "greenhouse"));
        Assert.Throws<ArgumentException>(() => CareerSource.Create("owner-1", companyId, ""));
        Assert.Throws<ArgumentException>(() => CareerSource.Create("owner-1", companyId, "greenhouse", new string('a', 101)));
        Assert.Throws<ArgumentException>(() => CareerSource.Create("owner-1", companyId, "greenhouse", null, new string('a', 151)));
        Assert.Throws<ArgumentException>(() => CareerSource.Create("owner-1", companyId, "greenhouse", "tenant", null, notes: new string('a', 501)));

        var cs = CareerSource.Create("owner-1", companyId, "greenhouse", "tenant1", "slug1", FetchMethod.Api, "notes");
        Assert.True(cs.IsActive);
        cs.Retire(DateTimeOffset.UtcNow);
        Assert.False(cs.IsActive);
        cs.Reactivate(DateTimeOffset.UtcNow);
        Assert.True(cs.IsActive);
    }

    [Fact]
    public void DomainValueObjectsAndSummaries_InitializeCorrectly()
    {
        var salary = new DesiredSalary(150_000, 200_000, "USD");
        Assert.Equal(150_000, salary.Min);
        Assert.Equal(200_000, salary.Max);
        Assert.Equal("USD", salary.Currency);

        var diff = new PostingDifference("location", "Dallas, TX", "Remote");
        Assert.Equal("location", diff.Field);
        Assert.Equal("Dallas, TX", diff.StoredValue);
        Assert.Equal("Remote", diff.SuppliedValue);

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var coAudit = new CompanyAuditSummary(id, ["name", "status"], "update", now);
        Assert.Equal(id, coAudit.CompanyId);
        Assert.Equal(2, coAudit.ChangedFieldNames.Count);

        var critAudit = new CriterionAuditSummary(id, "hard_skip", "create", now);
        Assert.Equal(id, critAudit.CriterionId);

        var appAudit = new ApplicationAuditSummary(id, Guid.NewGuid(), "applied", now);
        Assert.Equal(id, appAudit.ApplicationId);
    }

    private sealed class NoOpTrackingTransactionContext : ITrackingTransactionContext
    {
        public ICompanyRepository Companies => throw new NotSupportedException();
        public IPostingRepository Postings => throw new NotSupportedException();
        public ICriterionRepository Criteria => throw new NotSupportedException();
        public IApplicationRepository Applications => throw new NotSupportedException();
        public IContactRepository Contacts => throw new NotSupportedException();
        public ISearchRunRepository SearchRuns => throw new NotSupportedException();
        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
