using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class CriteriaImportStepTests
{
    private const string TestOwnerId = "test-user-tracker";
    private static readonly DateOnly TestDefaultDate = new(2026, 10, 24);

    [Fact]
    public async Task SeedPredefinedCriteriaAsync_SeedsBothCriteriaIdempotently()
    {
        var context = new InMemoryTrackingTransactionContext();
        var step = new CriteriaSeedingStep(context);

        // 1. Initial run seeds both
        var result = await step.SeedPredefinedCriteriaAsync(TestOwnerId, TestDefaultDate);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value!.CreatedCount);

        var floor = await context.Criteria.GetActiveByKeyAsync(TestOwnerId, "comp_preferred_floor");
        Assert.NotNull(floor.Value);
        Assert.Equal("200000", floor.Value!.Value);
        Assert.Equal(ProvenanceKind.Stated, floor.Value.Provenance);
        Assert.Equal(CriterionCategory.Preference, floor.Value.Category);

        var bar = await context.Criteria.GetActiveByKeyAsync(TestOwnerId, "comp_high_priority_bar");
        Assert.NotNull(bar.Value);
        Assert.Equal("220000", bar.Value!.Value);
        Assert.Equal(ProvenanceKind.Stated, bar.Value.Provenance);
        Assert.Equal(CriterionCategory.Preference, bar.Value.Category);

        Assert.Equal(2, context.AppendedAuditEntries.Count);
        Assert.All(context.AppendedAuditEntries, entry =>
        {
            Assert.Equal("cli-import-tracker", entry.Actor);
            Assert.Equal(AuditOperation.ImportCriterionRevision, entry.Operation);
        });

        // 2. Second run is a no-op (idempotent)
        var secondRun = await step.SeedPredefinedCriteriaAsync(TestOwnerId, TestDefaultDate);
        Assert.True(secondRun.IsSuccess);
        Assert.NotNull(secondRun.Value);
        Assert.Equal(0, secondRun.Value!.CreatedCount);
        Assert.Equal(2, context.AppendedAuditEntries.Count); // No new audit entries
    }

    [Fact]
    public async Task GetOrCreateReasonCriterionAsync_RegistersOnFirstUseAndReusesOnSubsequent()
    {
        var context = new InMemoryTrackingTransactionContext();
        var step = new CriteriaSeedingStep(context);
        var effective = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        // 1. First call registers new criterion
        var firstResult = await step.GetOrCreateReasonCriterionAsync(TestOwnerId, "No remote work", effective);
        Assert.True(firstResult.IsSuccess);
        Assert.NotNull(firstResult.Value);
        var firstCriterion = firstResult.Value!;
        Assert.Equal("No remote work", firstCriterion.Key);
        Assert.Equal(ProvenanceKind.Inferred, firstCriterion.Provenance);
        Assert.Equal(CriterionCategory.HardSkip, firstCriterion.Category);

        Assert.Single(context.AppendedAuditEntries);
        var audit = context.AppendedAuditEntries[0];
        Assert.Equal("cli-import-tracker", audit.Actor);
        Assert.Equal(AuditOperation.ImportCriterionRevision, audit.Operation);

        // 2. Second call reuses existing criterion without appending extra audit entry
        var secondResult = await step.GetOrCreateReasonCriterionAsync(TestOwnerId, "No remote work", effective);
        Assert.True(secondResult.IsSuccess);
        Assert.NotNull(secondResult.Value);
        Assert.Equal(firstCriterion.Id, secondResult.Value!.Id);
        Assert.Single(context.AppendedAuditEntries);
    }

    private sealed class InMemoryTrackingTransactionContext : ITrackingTransactionContext
    {
        public InMemoryCriterionRepository CriteriaRepo { get; } = new();
        public List<AuditLogEntry> AppendedAuditEntries { get; } = new();

        public ICompanyRepository Companies => throw new NotImplementedException();
        public IPostingRepository Postings => throw new NotImplementedException();
        public ICriterionRepository Criteria => CriteriaRepo;
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();

        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            AppendedAuditEntries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCriterionRepository : ICriterionRepository
    {
        private readonly Dictionary<(string OwnerId, string Key), Criterion> _store = new();
        private readonly Dictionary<Guid, Criterion> _byId = new();

        public Task<Result<Criterion?>> GetActiveByKeyAsync(string ownerId, string key, CancellationToken cancellationToken = default)
        {
            _store.TryGetValue((ownerId, key), out var c);
            return Task.FromResult(Result<Criterion?>.Success(c));
        }

        public Task<Result<Criterion?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
        {
            _byId.TryGetValue(id, out var c);
            return Task.FromResult(Result<Criterion?>.Success(c));
        }

        public Task<Result<IReadOnlyList<Criterion>>> GetEffectiveAtAsync(string ownerId, DateTimeOffset effectiveDate, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<IReadOnlyList<Criterion>>>(Result<IReadOnlyList<Criterion>>.Success(_store.Values.ToList()));

        public Task<Result<IReadOnlyList<Criterion>>> ListActiveAsync(string ownerId, CancellationToken cancellationToken = default)
            => Task.FromResult<Result<IReadOnlyList<Criterion>>>(Result<IReadOnlyList<Criterion>>.Success(_store.Values.ToList()));

        public Task<Result<bool>> UpsertCriterionAsync(Criterion criterion, CancellationToken cancellationToken = default)
        {
            _store[(criterion.OwnerId, criterion.Key)] = criterion;
            _byId[criterion.Id] = criterion;
            return Task.FromResult(Result<bool>.Success(true));
        }
    }
}
