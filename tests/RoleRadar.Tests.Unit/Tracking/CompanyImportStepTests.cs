using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Csv;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class CompanyImportStepTests
{
    private const string TestOwnerId = "test-owner";

    [Fact]
    public async Task CompanyAliasesStep_LoadsAliasesCorrectly()
    {
        var tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "alias,canonical\r\nAcme,Acme Corp\r\nGlobex,Globex Industries");

            var parser = new Rfc4180CsvParser();
            var step = new CompanyAliasesStep(parser);
            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, context.CompanyAliases.Count);
            Assert.Equal("Acme Corp", context.CompanyAliases["Acme"]);
            Assert.Equal("Globex Industries", context.CompanyAliases["Globex"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WatchlistCompanyStep_MapsWatchAndAvoidStatuses_RespectsDecisionSkip()
    {
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                company,tier,source,notes,decision,corrected_notes
                Acme Corp,target,recruiter,Note for Acme,,
                Cyberdyne,avoid,news,Note for Cyberdyne,,
                Soylent,watch,direct,Note for Soylent,,
                Initech,watch,referral,Skip Initech,skip,
                """;
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), csv);

            var inMemoryCtx = new InMemoryCompanyContext();
            var parser = new Rfc4180CsvParser();
            var step = new WatchlistCompaniesStep(parser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(3, context.Summary.CompaniesCreated);
            Assert.Equal(1, context.Summary.CompaniesUnchanged); // Initech skipped

            // Check Acme -> Target
            var acme = inMemoryCtx.SavedCompanies.FirstOrDefault(c => c.Name == "Acme Corp");
            Assert.NotNull(acme);
            Assert.Equal(CompanyStatus.Target, acme.Status);
            Assert.Equal("Note for Acme", acme.Description);

            // Check Cyberdyne -> Avoid
            var cyberdyne = inMemoryCtx.SavedCompanies.FirstOrDefault(c => c.Name == "Cyberdyne");
            Assert.NotNull(cyberdyne);
            Assert.Equal(CompanyStatus.Avoid, cyberdyne.Status);

            // Check Soylent -> Watch
            var soylent = inMemoryCtx.SavedCompanies.FirstOrDefault(c => c.Name == "Soylent");
            Assert.NotNull(soylent);
            Assert.Equal(CompanyStatus.Watch, soylent.Status);

            // Check audit entries: actor cli-import-tracker and operation import_company_create
            Assert.Equal(3, inMemoryCtx.AppendedAuditEntries.Count);
            Assert.All(inMemoryCtx.AppendedAuditEntries, a =>
            {
                Assert.Equal("cli-import-tracker", a.Actor);
                Assert.Equal(AuditOperation.ImportCompanyCreate, a.Operation);
            });
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WatchlistCompanyStep_UsesCorrectedNotesWhenPresent()
    {
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                company,tier,source,notes,decision,corrected_notes
                Wayne Enterprises,target,referral,Original text,,Corrected text
                """;
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), csv);

            var inMemoryCtx = new InMemoryCompanyContext();
            var parser = new Rfc4180CsvParser();
            var step = new WatchlistCompaniesStep(parser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            var company = inMemoryCtx.SavedCompanies.FirstOrDefault(c => c.Name == "Wayne Enterprises");
            Assert.NotNull(company);
            Assert.Equal("Corrected text", company.Description);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr_comp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InMemoryCompanyContext : ITrackingTransactionContext
    {
        public List<Company> SavedCompanies { get; } = new();
        public List<AuditLogEntry> AppendedAuditEntries { get; } = new();

        public ICompanyRepository Companies => new FakeCompanyRepository(SavedCompanies);
        public IPostingRepository Postings => throw new NotImplementedException();
        public ICriterionRepository Criteria => throw new NotImplementedException();
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
    }
}
