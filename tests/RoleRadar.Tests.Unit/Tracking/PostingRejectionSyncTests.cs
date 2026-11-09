using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using Xunit;
using DomainApplication = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Tests.Unit.Tracking;

/// <summary>
/// Feature 008, phase 1: rejecting an applied posting records the rejection on its application in the same
/// handler call. Behaviour tests that compile against today's API live here; tests that need the new members
/// (EventDate, Warnings) are in PostingRejectionSyncTests.NewMembers.cs.
/// All data is fictional.
/// </summary>
public sealed partial class PostingRejectionSyncTests
{
    private const string Owner = "owner-1";
    private static readonly DateTime FixedNow = new(2026, 10, 5, 14, 30, 0, DateTimeKind.Utc);

    // ---------- T001 ----------

    [Fact]
    public async Task AppliedToRejected_AppendsOneRejectedEvent_FromPostingTransition_AndRejectsApplication()
    {
        var world = World.WithAppliedPosting();
        var handler = world.Handler();

        var result = await handler.HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(PostingDisposition.Rejected, world.Posting.Disposition);

        var events = world.Events.Where(e => e.ApplicationId == world.App.Id).ToList();
        var ev = Assert.Single(events);
        Assert.Equal(ApplicationEventType.Rejected, ev.EventType);
        Assert.Equal("posting_transition", ev.EventSource);
        Assert.Equal("Rejected by email", ev.Notes);
        Assert.Equal(ApplicationOutcome.Rejected, world.App.Outcome);
    }

    [Fact]
    public async Task AppliedToRejected_WritesDerivedStateOnlyThroughUpsertAndAppendEvent()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        // The only application writes are the existing Upsert + AppendEventAsync pair (the repository choke point).
        Assert.Equal(
            ["posting.upsert", "app.upsert", "app.appendEvent"],
            world.Calls.Where(c => c.StartsWith("posting.upsert", StringComparison.Ordinal)
                                    || c.StartsWith("app.upsert", StringComparison.Ordinal)
                                    || c.StartsWith("app.appendEvent", StringComparison.Ordinal)).ToArray());
    }

    // ---------- T003 ----------

    [Fact]
    public async Task AlreadyRejectedApplication_AddsNoEvent_AndTransitionSucceeds()
    {
        var world = World.WithAppliedPosting(appOutcome: ApplicationOutcome.Rejected);
        world.AddEvent(ApplicationEventType.Rejected, "2026-09-20", "web");

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(PostingDisposition.Rejected, world.Posting.Disposition);
        Assert.Single(world.Events, e => e.EventType == ApplicationEventType.Rejected);
    }

    [Fact]
    public async Task AppliedOutcome_ThatAlreadyHoldsARejectedEvent_AddsNoSecondEvent()
    {
        var world = World.WithAppliedPosting();
        world.AddEvent(ApplicationEventType.Rejected, "2026-09-20", "web");

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Single(world.Events, e => e.EventType == ApplicationEventType.Rejected);
        Assert.DoesNotContain("app.appendEvent", world.Calls);
    }

    [Fact]
    public async Task PostingWithNoApplication_BehavesAsToday_NoEventNoApplicationWrite()
    {
        var world = World.WithAppliedPosting(withApplication: false);

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(PostingDisposition.Rejected, world.Posting.Disposition);
        Assert.Empty(world.Events);
        Assert.DoesNotContain("app.upsert", world.Calls);
        Assert.DoesNotContain("app.appendEvent", world.Calls);
        // Today's behaviour: exactly one audit entry, the posting one.
        var entry = Assert.Single(world.Audit);
        Assert.Equal(AuditOperation.PostingTransition, entry.Operation);
    }

    [Fact]
    public async Task ApplicationThatEndedAnotherWay_Closed_IsLeftUnchanged_AndTransitionSucceeds()
    {
        // One test only for this case (its warning is asserted in the NewMembers file).
        var world = World.WithAppliedPosting(appOutcome: ApplicationOutcome.Closed);
        world.AddEvent(ApplicationEventType.Withdrawn, "2026-09-25", "web");

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(PostingDisposition.Rejected, world.Posting.Disposition);
        Assert.Equal(ApplicationOutcome.Closed, world.App.Outcome);
        Assert.Single(world.Events);
        Assert.DoesNotContain("app.upsert", world.Calls);
        Assert.DoesNotContain("app.appendEvent", world.Calls);
    }

    // ---------- T004 ----------

    [Fact]
    public async Task Transition_WritesEachRecordOnce_AndNeverRunsTheApplicationToPostingSync()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, world.Calls.Count(c => c == "posting.upsert"));
        Assert.Equal(1, world.Calls.Count(c => c == "app.upsert"));
        Assert.Equal(1, world.Calls.Count(c => c == "app.appendEvent"));
    }

    [Fact]
    public void TransitionHandler_IsStructurallyIncapableOfCallingTheApplicationEventSync()
    {
        // The sync lives in RecordApplicationCommandHandler. The transition handler may depend only on the
        // transaction context and the clock, so it has no way to reach that handler.
        var ctor = Assert.Single(typeof(TransitionPostingCommandHandler).GetConstructors());
        var parameterTypes = ctor.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Equal([typeof(ITrackingTransactionContext), typeof(IUtcClock)], parameterTypes);
    }

    [Fact]
    public async Task ManualRejectedEvent_AfterPostingDrivenRejection_DoesNotChangeThePostingAgain()
    {
        var world = World.WithAppliedPosting();
        var transitioned = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));
        Assert.True(transitioned.IsSuccess, transitioned.Error?.Message);
        var decidedAt = world.Posting.DecidedAt;
        var version = world.Posting.VersionToken;
        world.Calls.Clear();

        var record = new RecordApplicationCommandHandler(world.Context, new FixedClock(FixedNow));
        var manual = await record.HandleAsync(new RecordApplicationCommand(
            PostingId: world.Posting.Id,
            Action: "append_event",
            Event: new ApplicationEventInputDto("rejected", "2026-10-06", "web", "Second email"),
            OwnerId: Owner,
            Actor: "agent"));

        Assert.True(manual.IsSuccess, manual.Error?.Message);
        Assert.Equal(PostingDisposition.Rejected, world.Posting.Disposition);
        // The existing sync leaves an already-rejected posting alone, and nothing loops back into the transition.
        Assert.Equal(decidedAt, world.Posting.DecidedAt);
        Assert.Equal(version, world.Posting.VersionToken);
    }

    // ---------- T005 ----------

    [Fact]
    public async Task AppliedToClosed_IsPostingOnly_ApplicationAndEventsUntouched()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(new TransitionPostingCommand(
            PostingId: world.Posting.Id,
            TargetDisposition: "closed",
            VersionToken: world.Posting.VersionToken,
            Reason: "Listing taken down",
            OwnerId: Owner,
            Actor: "mcp-stdio"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(PostingDisposition.Closed, world.Posting.Disposition);
        Assert.Equal(ApplicationOutcome.Applied, world.App.Outcome);
        Assert.Empty(world.Events);
        Assert.DoesNotContain("app.upsert", world.Calls);
        Assert.DoesNotContain("app.appendEvent", world.Calls);
        Assert.Single(world.Audit);
    }

    [Theory]
    [InlineData(PostingDisposition.Lead)]
    [InlineData(PostingDisposition.Scored)]
    public async Task EarlierDispositionsCannotBeRejected_AndAnyApplicationIsUntouched(PostingDisposition start)
    {
        var world = World.WithAppliedPosting(postingDisposition: start);

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, result.Error!.Value.Code);
        Assert.Equal(start, world.Posting.Disposition);
        Assert.Empty(world.Events);
        Assert.DoesNotContain("app.upsert", world.Calls);
    }

    [Fact]
    public async Task RejectedToRejected_StillNotAllowedByTheMatrix()
    {
        var world = World.WithAppliedPosting(postingDisposition: PostingDisposition.Rejected);

        var result = await world.Handler().HandleAsync(world.RejectCommand("Again"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.InvalidTransition, result.Error!.Value.Code);
        Assert.Empty(world.Events);
    }

    // ---------- T006 (reopen, behaviour part) ----------

    [Fact]
    public async Task ReopenRejectedPosting_WithRejectedApplication_LeavesApplicationAndEventsUnchanged()
    {
        var world = World.WithAppliedPosting(postingDisposition: PostingDisposition.Rejected, appOutcome: ApplicationOutcome.Rejected);
        world.AddEvent(ApplicationEventType.Rejected, "2026-09-20", "web");

        var result = await world.Handler().HandleAsync(world.ReopenCommand());

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(PostingDisposition.Lead, world.Posting.Disposition);
        Assert.Equal(ApplicationOutcome.Rejected, world.App.Outcome);
        Assert.Single(world.Events);
        Assert.DoesNotContain("app.upsert", world.Calls);
        Assert.DoesNotContain("app.appendEvent", world.Calls);
    }

    [Fact]
    public async Task ReopenRejectedPosting_WithoutOverrideReason_StillFailsValidation()
    {
        var world = World.WithAppliedPosting(postingDisposition: PostingDisposition.Rejected, appOutcome: ApplicationOutcome.Rejected);

        var result = await world.Handler().HandleAsync(new TransitionPostingCommand(
            PostingId: world.Posting.Id,
            TargetDisposition: "lead",
            VersionToken: world.Posting.VersionToken,
            OwnerId: Owner,
            Actor: "mcp-stdio"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Equal(PostingDisposition.Rejected, world.Posting.Disposition);
    }

    // ---------- T007 (audit, behaviour part) ----------

    [Fact]
    public async Task SyncingTransition_WritesPostingAndApplicationAuditEntries()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, world.Audit.Count);
        var posting = Assert.Single(world.Audit, a => a.Operation == AuditOperation.PostingTransition);
        var application = Assert.Single(world.Audit, a => a.Operation == AuditOperation.ApplicationEventAppend);
        Assert.Equal("posting", posting.EntityType);
        Assert.Equal(world.Posting.Id, posting.EntityId);
        Assert.Equal("application", application.EntityType);
        Assert.Equal(world.App.Id, application.EntityId);
        Assert.Equal("posting_transition", application.Details!.After!["event_source"]);
    }

    [Fact]
    public async Task SyncingTransition_BothAuditEntriesShareOneNonEmptyCorrelationId()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var posting = Assert.Single(world.Audit, a => a.Operation == AuditOperation.PostingTransition);
        var application = Assert.Single(world.Audit, a => a.Operation == AuditOperation.ApplicationEventAppend);
        var postingId = Assert.IsType<string>(posting.Details!.After!["correlation_id"]);
        var applicationId = Assert.IsType<string>(application.Details!.After!["correlation_id"]);
        Assert.False(string.IsNullOrWhiteSpace(postingId));
        Assert.Equal(postingId, applicationId);
        Assert.True(Guid.TryParse(postingId, out _));
    }

    [Fact]
    public async Task NoEventCase_WritesOnlyThePostingEntry_WithNoCorrelationId()
    {
        var world = World.WithAppliedPosting(appOutcome: ApplicationOutcome.Rejected);
        world.AddEvent(ApplicationEventType.Rejected, "2026-09-20", "web");

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var entry = Assert.Single(world.Audit);
        Assert.Equal(AuditOperation.PostingTransition, entry.Operation);
        Assert.False(entry.Details!.After!.ContainsKey("correlation_id"));
    }

    // ---------- T008 (failure propagation, unit level) ----------

    [Fact]
    public async Task FailedApplicationWrite_ReturnsFailure_SoTheDecoratorRollsBack_AndWritesNoAudit()
    {
        var world = World.WithAppliedPosting();
        world.FailAppendEvent = true;

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        // The in-memory fake cannot roll back; the real rollback is proven against Mongo in Phase 2 (T026).
        // Here we prove the handler surfaces the failure as a Result and appends no audit entry after it.
        Assert.False(result.IsSuccess);
        Assert.Empty(world.Audit);
    }

    // ---------- test world ----------

    private sealed class FixedClock : IUtcClock
    {
        public FixedClock(DateTime utcNow) => UtcNow = utcNow;

        public DateTime UtcNow { get; }
    }

    private sealed class World
    {
        private readonly List<DomainApplication> _applications = [];
        private readonly List<Posting> _postings = [];

        public Posting Posting { get; private set; } = null!;

        public DomainApplication App { get; private set; } = null!;

        public List<ApplicationEvent> Events { get; } = [];

        public List<AuditLogEntry> Audit { get; } = [];

        public List<string> Calls { get; } = [];

        public bool FailAppendEvent { get; set; }

        public ITrackingTransactionContext Context { get; private set; } = null!;

        public static World WithAppliedPosting(
            PostingDisposition postingDisposition = PostingDisposition.Applied,
            ApplicationOutcome appOutcome = ApplicationOutcome.Applied,
            bool withApplication = true)
        {
            var world = new World();
            var companyId = Guid.NewGuid();
            world.Posting = Posting.Create(
                Owner,
                companyId,
                "Staff Platform Engineer",
                disposition: postingDisposition,
                dispositionReason: postingDisposition == PostingDisposition.Rejected ? "Earlier rejection" : null);
            world._postings.Add(world.Posting);

            if (withApplication)
            {
                world.App = DomainApplication.Create(
                    Owner,
                    world.Posting.Id,
                    "2026-09-01",
                    ResumeVariant.Tailored,
                    outcome: appOutcome);
                world._applications.Add(world.App);
                world.AddEvent(ApplicationEventType.Applied, "2026-09-01", "web");
                world.Events.Clear(); // keep the baseline empty so assertions count only what the handler adds
            }

            world.Context = new Ctx(world);
            return world;
        }

        public TransitionPostingCommandHandler Handler() => new(Context, new FixedClock(FixedNow));

        public void AddEvent(ApplicationEventType type, string date, string source) =>
            Events.Add(ApplicationEvent.Create(Owner, App.Id, date, type, source, notes: null));

        public TransitionPostingCommand RejectCommand(string reason) =>
            new(
                PostingId: Posting.Id,
                TargetDisposition: "rejected",
                VersionToken: Posting.VersionToken,
                Reason: reason,
                OwnerId: Owner,
                Actor: "mcp-stdio");

        public TransitionPostingCommand ReopenCommand() =>
            new(
                PostingId: Posting.Id,
                TargetDisposition: "lead",
                VersionToken: Posting.VersionToken,
                OverrideReason: "Recruiter reopened the role",
                OwnerId: Owner,
                Actor: "mcp-stdio");

        private sealed class Ctx : ITrackingTransactionContext
        {
            private readonly World _world;

            public Ctx(World world) => _world = world;

            public ICompanyRepository Companies => new CompanyRepo(this);

            public IPostingRepository Postings => new PostingRepo(_world);

            public ICriterionRepository Criteria => throw new NotImplementedException();

            public IApplicationRepository Applications => new AppRepo(_world);

            public IContactRepository Contacts => throw new NotImplementedException();

            public ISearchRunRepository SearchRuns => throw new NotImplementedException();

            public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
            {
                _world.Audit.Add(entry);
                return Task.CompletedTask;
            }
        }

        private sealed class CompanyRepo : ICompanyRepository
        {
            private readonly ITrackingTransactionContext _ctx;

            public CompanyRepo(ITrackingTransactionContext ctx) => _ctx = ctx;

            // Runs the work inline, as the real repository does when a transaction is already ambient.
            public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
                Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
                CancellationToken cancellationToken = default) =>
                work(_ctx, cancellationToken);

            public Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        }

        private sealed class PostingRepo : IPostingRepository
        {
            private readonly World _world;

            public PostingRepo(World world) => _world = world;

            public Task<Result<Posting?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_world._postings.FirstOrDefault(p => p.Id == id)));

            public Task<Result<bool>> UpsertPostingAsync(Posting posting, CancellationToken cancellationToken = default)
            {
                _world.Calls.Add("posting.upsert");
                var index = _world._postings.FindIndex(p => p.Id == posting.Id);
                if (index >= 0)
                {
                    _world._postings[index] = posting;
                }
                else
                {
                    _world._postings.Add(posting);
                }

                return Task.FromResult(Result<bool>.Success(true));
            }

            public Task<Result<Posting?>> GetByDedupKeyAAsync(string ownerId, string dedupKeyA, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<Posting?>> GetByDedupKeyBAsync(string ownerId, string dedupKeyB, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<Posting?>> GetByDedupKeyCAsync(string ownerId, string dedupKeyC, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<IReadOnlyList<PostingCandidate>>> FindCandidatesByCompanyAndTitleAsync(string ownerId, Guid companyId, string normalizedTitle, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<IReadOnlyList<Posting>>> ListPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<IReadOnlyList<Posting>>> ListDecidedPostingsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<int>> CountPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<int>> CountDecidedPostingsAsync(string ownerId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<bool>> AppendSignalAsync(PostingSignal signal, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<IReadOnlyList<PostingSignal>>> GetSignalsAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<ILookup<Guid, PostingSignal>>> GetSignalsForPostingsAsync(string ownerId, IEnumerable<Guid> postingIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        }

        private sealed class AppRepo : IApplicationRepository
        {
            private readonly World _world;

            public AppRepo(World world) => _world = world;

            public Task<Result<DomainApplication?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<DomainApplication?>.Success(_world._applications.FirstOrDefault(a => a.Id == id)));

            public Task<Result<DomainApplication?>> GetByPostingIdAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<DomainApplication?>.Success(_world._applications.FirstOrDefault(a => a.PostingId == postingId)));

            public Task<Result<bool>> UpsertApplicationAsync(DomainApplication application, CancellationToken cancellationToken = default)
            {
                _world.Calls.Add("app.upsert");
                var index = _world._applications.FindIndex(a => a.Id == application.Id);
                if (index >= 0)
                {
                    _world._applications[index] = application;
                }
                else
                {
                    _world._applications.Add(application);
                }

                return Task.FromResult(Result<bool>.Success(true));
            }

            public Task<Result<bool>> AppendEventAsync(ApplicationEvent appEvent, CancellationToken cancellationToken = default)
            {
                _world.Calls.Add("app.appendEvent");
                if (_world.FailAppendEvent)
                {
                    return Task.FromResult(Result<bool>.Failure(ErrorCode.DatastoreOffline, "Injected failure."));
                }

                _world.Events.Add(appEvent);
                return Task.FromResult(Result<bool>.Success(true));
            }

            public Task<Result<IReadOnlyList<ApplicationEvent>>> GetEventsAsync(string ownerId, Guid applicationId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<ApplicationEvent>>.Success(
                    _world.Events.Where(e => e.ApplicationId == applicationId).ToList()));

            public Task<Result<IReadOnlyList<DomainApplication>>> ListApplicationsAsync(string ownerId, string? outcome = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) => throw new NotImplementedException();

            public Task<Result<int>> CountApplicationsAsync(string ownerId, string? outcome = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        }
    }
}
