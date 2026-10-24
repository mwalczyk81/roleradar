using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Csv;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class PostingImportStepTests
{
    private const string TestOwnerId = "test-owner";
    private static readonly DateOnly TestDefaultDate = new(2026, 10, 24);

    [Fact]
    public async Task PostingsJsonStep_DropsTier_MapsCompHigh_MapsCultureLevel_AndFlagsOutOfRangeScore()
    {
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                [
                    {
                        "title": "Principal Architect",
                        "company": "Acme Corp",
                        "tier": "A",
                        "comp": "250k base",
                        "compHigh": true,
                        "cultureLevel": "stable",
                        "culture": "Healthy remote culture",
                        "score": 9.5,
                        "status": "applied",
                        "url": "https://example.com/job/301"
                    },
                    {
                        "title": "Staff Engineer",
                        "company": "Globex",
                        "tier": "B",
                        "compHigh": false,
                        "cultureLevel": "non_standard_value",
                        "score": 12.0,
                        "status": "lead",
                        "url": "https://example.com/job/302"
                    }
                ]
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, context.Summary.PostingsCreated);

            // 1. First posting
            var p1 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Principal Architect");
            Assert.NotNull(p1);
            Assert.True(p1.CompHighPriority);
            Assert.Equal(CultureLevel.Stable, p1.CultureLevel);
            Assert.Equal(9.5m, p1.Score);
            Assert.Equal(PostingDisposition.Scored, p1.Disposition); // "applied" stages as "scored"

            // 2. Second posting (out of range score -> stored as empty without clamping; cultureLevel fallback -> unknown)
            var p2 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Staff Engineer");
            Assert.NotNull(p2);
            Assert.False(p2.CompHighPriority);
            Assert.Equal(CultureLevel.Unknown, p2.CultureLevel);
            Assert.Null(p2.Score); // Out of range score stored as null
            Assert.True(context.Summary.DatesFlagged > 0); // Flagged count incremented
            Assert.Equal(1, context.Summary.CultureUnmapped);

            // Verify unclaimed applied roles tracked
            Assert.Equal(1, context.Summary.UnclaimedAppliedPostings);
            Assert.Contains("Principal Architect", context.Summary.UnclaimedAppliedPostingTitles);

            // Verify audit operation is import_posting_create and actor cli-import-tracker
            Assert.All(inMemoryCtx.AppendedAuditEntries.Where(a => a.EntityType == "posting"), a =>
            {
                Assert.Equal("cli-import-tracker", a.Actor);
                Assert.Equal(AuditOperation.ImportPostingCreate, a.Operation);
            });
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_ProcessesNonDuplicatesBeforeDuplicates_AndLinksDuplicateOfId()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Two rows in appendix: duplicate appears first in CSV, target appears second in CSV
            // The step must process non-duplicate first, so duplicate resolves target ID!
            var csv = """
                req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes
                REQ-DUP,Acme Corp,Software Engineer,true,Details,2026-05-01,duplicate,duplicate,never,false,https://example.com/job/dup,,
                REQ-ORIG,Acme Corp,Software Engineer,true,Details,2026-05-01,no-remote,no-remote,never,false,https://example.com/job/orig,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, context.Summary.PostingsCreated);

            var orig = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "REQ-ORIG");
            var dup = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "REQ-DUP");

            Assert.NotNull(orig);
            Assert.NotNull(dup);
            Assert.Equal(orig.Id, dup.DuplicateOfId); // Resolved target posting ID!
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr_post_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InMemoryPostingContext : ITrackingTransactionContext
    {
        public List<Company> SavedCompanies { get; } = new();
        public List<Posting> SavedPostings { get; } = new();
        public List<Criterion> SavedCriteria { get; } = new();
        public List<AuditLogEntry> AppendedAuditEntries { get; } = new();

        public ICompanyRepository Companies => new FakeCompanyRepository(SavedCompanies);
        public IPostingRepository Postings => new FakePostingRepository(SavedPostings);
        public ICriterionRepository Criteria => new FakeCriterionRepository(SavedCriteria);
        public IApplicationRepository Applications => throw new NotImplementedException();
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();

        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            AppendedAuditEntries.Add(entry);
            return Task.CompletedTask;
        }

        private sealed class FakeCompanyRepository : ICompanyRepository
        {
            private readonly List<Company> _companies;
            public FakeCompanyRepository(List<Company> companies) => _companies = companies;
            public Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Company?>.Success(_companies.FirstOrDefault(c => c.Id == id)));
            public Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Company?>.Success(_companies.FirstOrDefault(c => c.NormalizedName == normalizedName)));
            public Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Company>>.Success(_companies));
            public Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<CareerSource>>.Success(Array.Empty<CareerSource>()));
            public Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<CareerSource?>.Success(null));
            public Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default)
            {
                var index = _companies.FindIndex(c => c.Id == company.Id);
                if (index >= 0) _companies[index] = company;
                else _companies.Add(company);
                return Task.FromResult(Result<bool>.Success(true));
            }
            public Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<bool>.Success(true));
            public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();
        }

        private sealed class FakePostingRepository : IPostingRepository
        {
            private readonly List<Posting> _postings;
            public FakePostingRepository(List<Posting> postings) => _postings = postings;
            public Task<Result<Posting?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.Id == id)));
            public Task<Result<Posting?>> GetByDedupKeyAAsync(string ownerId, string dedupKeyA, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.DedupKeyA == dedupKeyA)));
            public Task<Result<Posting?>> GetByDedupKeyBAsync(string ownerId, string dedupKeyB, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.DedupKeyB == dedupKeyB)));
            public Task<Result<Posting?>> GetByDedupKeyCAsync(string ownerId, string dedupKeyC, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.DedupKeyC == dedupKeyC)));
            public Task<Result<IReadOnlyList<PostingCandidate>>> FindCandidatesByCompanyAndTitleAsync(string ownerId, Guid companyId, string normalizedTitle, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<PostingCandidate>>.Success(_postings
                    .Where(p => p.CompanyId == companyId && p.NormalizedTitle == normalizedTitle)
                    .Select(p => new PostingCandidate(p.Id, p.Title, p.Disposition, p.DispositionReason))
                    .ToList()));
            public Task<Result<IReadOnlyList<Posting>>> ListPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Posting>>.Success(_postings));
            public Task<Result<IReadOnlyList<Posting>>> ListDecidedPostingsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Posting>>.Success(_postings));
            public Task<Result<int>> CountPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<int>.Success(_postings.Count));
            public Task<Result<int>> CountDecidedPostingsAsync(string ownerId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<int>.Success(_postings.Count));
            public Task<Result<bool>> UpsertPostingAsync(Posting posting, CancellationToken cancellationToken = default)
            {
                var index = _postings.FindIndex(p => p.Id == posting.Id);
                if (index >= 0) _postings[index] = posting;
                else _postings.Add(posting);
                return Task.FromResult(Result<bool>.Success(true));
            }
            public Task<Result<bool>> AppendSignalAsync(PostingSignal signal, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<bool>.Success(true));
            public Task<Result<IReadOnlyList<PostingSignal>>> GetSignalsAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<PostingSignal>>.Success(Array.Empty<PostingSignal>()));
            public Task<Result<ILookup<Guid, PostingSignal>>> GetSignalsForPostingsAsync(string ownerId, IEnumerable<Guid> postingIds, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();
        }

        private sealed class FakeCriterionRepository : ICriterionRepository
        {
            private readonly List<Criterion> _criteria;
            public FakeCriterionRepository(List<Criterion> criteria) => _criteria = criteria;
            public Task<Result<Criterion?>> GetActiveByKeyAsync(string ownerId, string key, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Criterion?>.Success(_criteria.FirstOrDefault(c => c.Key == key)));
            public Task<Result<Criterion?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Criterion?>.Success(_criteria.FirstOrDefault(c => c.Id == id)));
            public Task<Result<IReadOnlyList<Criterion>>> GetEffectiveAtAsync(string ownerId, DateTimeOffset effectiveDate, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Criterion>>.Success(_criteria));
            public Task<Result<IReadOnlyList<Criterion>>> ListActiveAsync(string ownerId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Criterion>>.Success(_criteria));
            public Task<Result<bool>> UpsertCriterionAsync(Criterion criterion, CancellationToken cancellationToken = default)
            {
                var index = _criteria.FindIndex(c => c.Id == criterion.Id);
                if (index >= 0) _criteria[index] = criterion;
                else _criteria.Add(criterion);
                return Task.FromResult(Result<bool>.Success(true));
            }
        }
    }
}
