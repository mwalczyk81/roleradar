using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Queries;

public sealed record PostingSummaryItemDto(
    Guid Id,
    Guid CompanyId,
    string Title,
    string NormalizedTitle,
    string Disposition,
    string GhostLikelihood,
    string ResurfacePolicy,
    Guid VersionToken,
    string? Location = null,
    string? RemoteType = null,
    decimal? Score = null,
    DateTimeOffset? LastVerifiedAt = null);

public sealed record DecidedPostingSummaryItemDto(
    Guid Id,
    Guid CompanyId,
    string Title,
    string Disposition,
    string? DispositionReason,
    string ResurfacePolicy,
    DateTimeOffset? DecidedAtUtc);

public sealed record CareerSourceSummaryItemDto(
    Guid Id,
    Guid CompanyId,
    string AtsType,
    string? Tenant,
    string? Slug,
    string FetchMethod,
    string? Notes,
    bool IsActive,
    Guid VersionToken);

public sealed record CompanySummaryItemDto(
    Guid Id,
    string Name,
    string? Description,
    string? ParentCompany,
    string OwnershipType,
    string? Headquarters,
    string Status,
    IReadOnlyList<string> CategoryTags,
    IReadOnlyList<CareerSourceSummaryItemDto> CareerSources,
    DateTimeOffset? LastCheckedAt,
    string? LastCheckResult,
    Guid VersionToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CriterionSummaryItemDto(
    Guid Id,
    string Key,
    string Category,
    string Value,
    string Provenance,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive,
    Guid VersionToken,
    DateTimeOffset CreatedAt);

public sealed record TrackerQueryResultDto(
    string Entity,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<object> Items);

public sealed record TrackerQueryRequest(
    string Entity,
    Guid? PostingId = null,
    Guid? CompanyId = null,
    string? Disposition = null,
    string? Status = null,
    string? Tag = null,
    string? Name = null,
    bool IncludeRetired = false,
    bool IncludeStale = false,
    int StaleAfterDays = 7,
    string? EffectiveDate = null,
    string? DedupKey = null,
    int Page = 1,
    int PageSize = 50,
    string? OwnerId = null) : IQuery<TrackerQueryResultDto>;

public sealed class TrackerQueryHandler : IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public TrackerQueryHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<TrackerQueryResultDto>> HandleAsync(
        TrackerQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.Entity))
        {
            return Result<TrackerQueryResultDto>.Failure(ErrorCode.ValidationError, "Entity discriminator is required.");
        }

        var entity = query.Entity.Trim().ToLowerInvariant();
        if (entity == "contacts" || entity == "contact")
        {
            return Result<TrackerQueryResultDto>.Failure(
                ErrorCode.ValidationError,
                "Contacts are accessible exclusively via CLI commands (roleradar contact add|get|redact).");
        }

        var ownerId = string.IsNullOrWhiteSpace(query.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(query.OwnerId).Value;
        var now = _clock.UtcNow;

        // Bounded paging: default 50, max 200, min 1
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 50 : query.PageSize, 1, 200);

        try
        {
            return entity switch
            {
                "postings" => await HandlePostingsQueryAsync(query, ownerId, page, pageSize, now, cancellationToken),
                "decided_postings" => await HandleDecidedPostingsQueryAsync(ownerId, page, pageSize, cancellationToken),
                "posting_dedup_lookup" => await HandleDedupLookupQueryAsync(query, ownerId, cancellationToken),
                "companies" => await HandleCompaniesQueryAsync(query, ownerId, page, pageSize, now, cancellationToken),
                "criteria" => await HandleCriteriaQueryAsync(query, ownerId, page, pageSize, cancellationToken),
                _ => Result<TrackerQueryResultDto>.Failure(
                    ErrorCode.ValidationError,
                    $"Entity discriminator '{query.Entity}' is not supported by tracker_query postings handler.")
            };
        }
        catch (TrackingIndexValidationException ex)
        {
            return Result<TrackerQueryResultDto>.Failure(ErrorCode.ValidationError, ex.Message);
        }
    }

    private async Task<Result<TrackerQueryResultDto>> HandlePostingsQueryAsync(
        TrackerQueryRequest query,
        string ownerId,
        int page,
        int pageSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Single posting lookup by ID
        if (query.PostingId.HasValue)
        {
            var singleResult = await _context.Postings.GetByIdAsync(ownerId, query.PostingId.Value, cancellationToken);
            if (!singleResult.IsSuccess || singleResult.Value is null)
            {
                return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("postings", 1, pageSize, 0, []));
            }

            var p = singleResult.Value;
            var signalsResult = await _context.Postings.GetSignalsAsync(ownerId, p.Id, cancellationToken);
            var signals = signalsResult.IsSuccess && signalsResult.Value is not null ? signalsResult.Value : [];
            var ghost = GhostLikelihoodEvaluator.Evaluate(p, signals, now);

            var item = new PostingSummaryItemDto(
                Id: p.Id,
                CompanyId: p.CompanyId,
                Title: p.Title,
                NormalizedTitle: p.NormalizedTitle,
                Disposition: p.Disposition.ToSnakeCase(),
                GhostLikelihood: ghost,
                ResurfacePolicy: p.ResurfacePolicy.ToSnakeCase(),
                VersionToken: p.VersionToken,
                Location: p.Location,
                RemoteType: p.RemoteType.ToSnakeCase(),
                Score: p.Score,
                LastVerifiedAt: p.LastVerifiedAt);

            return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("postings", 1, pageSize, 1, [item]));
        }

        // Paged listing with optional disposition and company filters
        PostingDisposition? dispositionFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Disposition) &&
            TrackingEnumExtensions.TryParseDisposition(query.Disposition, out var parsedDisp))
        {
            dispositionFilter = parsedDisp;
        }

        var postingsResult = await _context.Postings.ListPostingsAsync(
            ownerId, query.CompanyId, dispositionFilter, page, pageSize, cancellationToken);
        if (!postingsResult.IsSuccess || postingsResult.Value is null)
        {
            return Result<TrackerQueryResultDto>.Failure(postingsResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to list postings."));
        }

        var countResult = await _context.Postings.CountPostingsAsync(
            ownerId, query.CompanyId, dispositionFilter, cancellationToken);
        var totalCount = countResult.IsSuccess ? countResult.Value : postingsResult.Value.Count;

        var items = new List<object>(postingsResult.Value.Count);
        foreach (var p in postingsResult.Value)
        {
            var sigsResult = await _context.Postings.GetSignalsAsync(ownerId, p.Id, cancellationToken);
            var sigs = sigsResult.IsSuccess && sigsResult.Value is not null ? sigsResult.Value : [];
            var ghost = GhostLikelihoodEvaluator.Evaluate(p, sigs, now);

            items.Add(new PostingSummaryItemDto(
                Id: p.Id,
                CompanyId: p.CompanyId,
                Title: p.Title,
                NormalizedTitle: p.NormalizedTitle,
                Disposition: p.Disposition.ToSnakeCase(),
                GhostLikelihood: ghost,
                ResurfacePolicy: p.ResurfacePolicy.ToSnakeCase(),
                VersionToken: p.VersionToken,
                Location: p.Location,
                RemoteType: p.RemoteType.ToSnakeCase(),
                Score: p.Score,
                LastVerifiedAt: p.LastVerifiedAt));
        }

        return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("postings", page, pageSize, totalCount, items));
    }

    private async Task<Result<TrackerQueryResultDto>> HandleDecidedPostingsQueryAsync(
        string ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var postingsResult = await _context.Postings.ListDecidedPostingsAsync(ownerId, page, pageSize, cancellationToken);
        if (!postingsResult.IsSuccess || postingsResult.Value is null)
        {
            return Result<TrackerQueryResultDto>.Failure(postingsResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to list decided postings."));
        }

        var countResult = await _context.Postings.CountDecidedPostingsAsync(ownerId, cancellationToken);
        var totalCount = countResult.IsSuccess ? countResult.Value : postingsResult.Value.Count;

        var items = new List<object>(postingsResult.Value.Count);
        foreach (var p in postingsResult.Value)
        {
            items.Add(new DecidedPostingSummaryItemDto(
                Id: p.Id,
                CompanyId: p.CompanyId,
                Title: p.Title,
                Disposition: p.Disposition.ToSnakeCase(),
                DispositionReason: p.DispositionReason,
                ResurfacePolicy: p.ResurfacePolicy.ToSnakeCase(),
                DecidedAtUtc: p.DecidedAt));
        }

        return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("decided_postings", page, pageSize, totalCount, items));
    }

    private async Task<Result<TrackerQueryResultDto>> HandleDedupLookupQueryAsync(
        TrackerQueryRequest query,
        string ownerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.DedupKey))
        {
            return Result<TrackerQueryResultDto>.Failure(ErrorCode.ValidationError, "DedupKey is required for entity 'posting_dedup_lookup'.");
        }

        var key = query.DedupKey.Trim();

        // 1. Try Tier A
        var matchA = await _context.Postings.GetByDedupKeyAAsync(ownerId, key, cancellationToken);
        if (matchA.IsSuccess && matchA.Value is not null)
        {
            return BuildSinglePostingResult(matchA.Value);
        }

        // 2. Try Tier B
        var normalizedKey = UrlNormalizer.Normalize(key) ?? key;
        var matchB = await _context.Postings.GetByDedupKeyBAsync(ownerId, normalizedKey, cancellationToken);
        if (matchB.IsSuccess && matchB.Value is not null)
        {
            return BuildSinglePostingResult(matchB.Value);
        }

        // 3. Try Tier C
        var matchC = await _context.Postings.GetByDedupKeyCAsync(ownerId, key, cancellationToken);
        if (matchC.IsSuccess && matchC.Value is not null)
        {
            return BuildSinglePostingResult(matchC.Value);
        }

        return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("posting_dedup_lookup", 1, 1, 0, []));
    }

    private Result<TrackerQueryResultDto> BuildSinglePostingResult(Posting p)
    {
        var item = new PostingSummaryItemDto(
            Id: p.Id,
            CompanyId: p.CompanyId,
            Title: p.Title,
            NormalizedTitle: p.NormalizedTitle,
            Disposition: p.Disposition.ToSnakeCase(),
            GhostLikelihood: "low",
            ResurfacePolicy: p.ResurfacePolicy.ToSnakeCase(),
            VersionToken: p.VersionToken,
            Location: p.Location,
            RemoteType: p.RemoteType.ToSnakeCase(),
            Score: p.Score,
            LastVerifiedAt: p.LastVerifiedAt);

        return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("posting_dedup_lookup", 1, 1, 1, [item]));
    }

    private async Task<Result<TrackerQueryResultDto>> HandleCompaniesQueryAsync(
        TrackerQueryRequest query,
        string ownerId,
        int page,
        int pageSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        CompanyStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            TrackingEnumExtensions.TryParseCompanyStatus(query.Status, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        var listResult = await _context.Companies.ListAsync(ownerId, statusFilter, cancellationToken);
        if (!listResult.IsSuccess || listResult.Value is null)
        {
            return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto("companies", page, pageSize, 0, []));
        }

        var companies = listResult.Value.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            var tag = query.Tag.Trim();
            companies = companies.Where(c => c.CategoryTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            var name = query.Name.Trim();
            companies = companies.Where(c => c.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IncludeStale)
        {
            var threshold = now.AddDays(-query.StaleAfterDays);
            companies = companies.Where(c => !c.LastCheckedAt.HasValue || c.LastCheckedAt.Value <= threshold);
        }

        var filteredList = companies.ToList();
        var totalCount = filteredList.Count;
        var pagedCompanies = filteredList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var items = new List<object>(pagedCompanies.Count);
        foreach (var c in pagedCompanies)
        {
            var sourcesRes = await _context.Companies.GetCareerSourcesAsync(
                ownerId,
                c.Id,
                includeInactive: query.IncludeRetired,
                cancellationToken);

            var sources = sourcesRes.IsSuccess && sourcesRes.Value is not null
                ? sourcesRes.Value.Select(s => new CareerSourceSummaryItemDto(
                    Id: s.Id,
                    CompanyId: s.CompanyId,
                    AtsType: s.AtsType,
                    Tenant: s.Tenant,
                    Slug: s.Slug,
                    FetchMethod: s.FetchMethod.ToSnakeCase(),
                    Notes: s.Notes,
                    IsActive: s.IsActive,
                    VersionToken: s.VersionToken)).ToArray()
                : [];

            items.Add(new CompanySummaryItemDto(
                Id: c.Id,
                Name: c.Name,
                Description: c.Description,
                ParentCompany: c.ParentCompany,
                OwnershipType: c.OwnershipType.ToSnakeCase(),
                Headquarters: c.Headquarters,
                Status: c.Status.ToSnakeCase(),
                CategoryTags: c.CategoryTags,
                CareerSources: sources,
                LastCheckedAt: c.LastCheckedAt,
                LastCheckResult: c.LastCheckResult,
                VersionToken: c.VersionToken,
                CreatedAt: c.CreatedAt,
                UpdatedAt: c.UpdatedAt));
        }

        return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto(
            Entity: "companies",
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            Items: items));
    }

    private async Task<Result<TrackerQueryResultDto>> HandleCriteriaQueryAsync(
        TrackerQueryRequest query,
        string ownerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Criterion> criteria;

        if (!string.IsNullOrWhiteSpace(query.EffectiveDate))
        {
            if (!DateTimeOffset.TryParse(query.EffectiveDate, System.Globalization.CultureInfo.InvariantCulture, out var parsedEffectiveDate))
            {
                return Result<TrackerQueryResultDto>.Failure(
                    ErrorCode.ValidationError,
                    $"Invalid EffectiveDate format '{query.EffectiveDate}'. Expected YYYY-MM-DD or ISO 8601 string.");
            }

            var effResult = await _context.Criteria.GetEffectiveAtAsync(ownerId, parsedEffectiveDate, cancellationToken);
            if (!effResult.IsSuccess || effResult.Value is null)
            {
                return Result<TrackerQueryResultDto>.Failure(effResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to query effective criteria."));
            }

            criteria = effResult.Value;
        }
        else
        {
            var activeResult = await _context.Criteria.ListActiveAsync(ownerId, cancellationToken);
            if (!activeResult.IsSuccess || activeResult.Value is null)
            {
                return Result<TrackerQueryResultDto>.Failure(activeResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to list active criteria."));
            }

            criteria = activeResult.Value;
        }

        var totalCount = criteria.Count;
        var pagedCriteria = criteria.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var items = pagedCriteria.Select(c => new CriterionSummaryItemDto(
            Id: c.Id,
            Key: c.Key,
            Category: c.Category.ToSnakeCase(),
            Value: c.Value,
            Provenance: c.Provenance.ToSnakeCase(),
            EffectiveFrom: c.EffectiveFrom,
            EffectiveTo: c.EffectiveTo,
            IsActive: c.IsActive,
            VersionToken: c.VersionToken,
            CreatedAt: c.CreatedAt)).ToArray();

        return Result<TrackerQueryResultDto>.Success(new TrackerQueryResultDto(
            Entity: "criteria",
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            Items: items));
    }
}
