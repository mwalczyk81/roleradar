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
    Task<Result<Posting?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<Posting?>> GetByDedupKeyAAsync(string ownerId, string dedupKeyA, CancellationToken cancellationToken = default);
    Task<Result<Posting?>> GetByDedupKeyBAsync(string ownerId, string dedupKeyB, CancellationToken cancellationToken = default);
    Task<Result<Posting?>> GetByDedupKeyCAsync(string ownerId, string dedupKeyC, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PostingCandidate>>> FindCandidatesByCompanyAndTitleAsync(string ownerId, Guid companyId, string normalizedTitle, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Posting>>> ListPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Posting>>> ListDecidedPostingsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<Result<int>> CountPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, CancellationToken cancellationToken = default);
    Task<Result<int>> CountDecidedPostingsAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertPostingAsync(Posting posting, CancellationToken cancellationToken = default);
    Task<Result<bool>> AppendSignalAsync(PostingSignal signal, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<PostingSignal>>> GetSignalsAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default);
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
