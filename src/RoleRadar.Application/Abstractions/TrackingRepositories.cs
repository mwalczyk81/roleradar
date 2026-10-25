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
    Task<Result<bool>> UpsertNoteAsync(RoleRadar.Domain.Notes.Note note, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(true));
    Task<Result<bool>> NoteExistsAsync(string ownerId, string title, string area, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<bool>.Success(false));
    Task<Result<Guid?>> GetNoteIdByTitleAndAreaAsync(string ownerId, string title, string area, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result<Guid?>.Success(null));
    Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
        Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
        CancellationToken cancellationToken = default) =>
        work(this, cancellationToken);
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
    Task<Result<ILookup<Guid, PostingSignal>>> GetSignalsForPostingsAsync(string ownerId, IEnumerable<Guid> postingIds, CancellationToken cancellationToken = default);
}

public interface ICriterionRepository
{
    Task<Result<Criterion?>> GetActiveByKeyAsync(string ownerId, string key, CancellationToken cancellationToken = default);
    Task<Result<Criterion?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Criterion>>> GetEffectiveAtAsync(string ownerId, DateTimeOffset effectiveDate, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Criterion>>> ListActiveAsync(string ownerId, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertCriterionAsync(Criterion criterion, CancellationToken cancellationToken = default);
}

public interface IApplicationRepository
{
    Task<Result<Domain.Tracking.Application?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<Domain.Tracking.Application?>> GetByPostingIdAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertApplicationAsync(Domain.Tracking.Application application, CancellationToken cancellationToken = default);
    Task<Result<bool>> AppendEventAsync(ApplicationEvent appEvent, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<ApplicationEvent>>> GetEventsAsync(string ownerId, Guid applicationId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Domain.Tracking.Application>>> ListApplicationsAsync(string ownerId, string? outcome = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<Result<int>> CountApplicationsAsync(string ownerId, string? outcome = null, CancellationToken cancellationToken = default);
}

public interface IContactRepository
{
    Task<Result<Domain.Tracking.Contact?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertContactAsync(Domain.Tracking.Contact contact, CancellationToken cancellationToken = default);
    Task<Result<int>> CountContactsAsync(string ownerId, bool? isRedacted = null, CancellationToken cancellationToken = default);
}

public interface ISearchRunRepository
{
    Task<Result<Domain.Tracking.SearchRun?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default);
    Task<Result<Domain.Tracking.SearchRun?>> GetByStartedAtAsync(string ownerId, DateTimeOffset startedAt, CancellationToken cancellationToken = default);
    Task<Result<bool>> UpsertSearchRunAsync(Domain.Tracking.SearchRun searchRun, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<Domain.Tracking.SearchRun>>> ListSearchRunsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default);
    Task<Result<int>> CountSearchRunsAsync(string ownerId, CancellationToken cancellationToken = default);
}
