using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Abstractions;

public interface ITrackingTransactionContext
{
    ICompanyRepository Companies { get; }
    IPostingRepository Postings { get; }
    ICriterionRepository Criteria { get; }
    IApplicationRepository Applications { get; }
    IContactRepository Contacts { get; }
    ISearchRunRepository SearchRuns { get; }
    Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}

public interface ICompanyRepository
{
    Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default);
    Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default);
    Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
        Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
        CancellationToken cancellationToken = default);
}

public interface IPostingRepository
{
    // Placeholders for User Story 1
}

public interface ICriterionRepository
{
    // Placeholders for User Story 3
}

public interface IApplicationRepository
{
    // Placeholders for User Story 4
}

public interface IContactRepository
{
    // Placeholders for User Story 6
}

public interface ISearchRunRepository
{
    // Placeholders for User Story 7
}
