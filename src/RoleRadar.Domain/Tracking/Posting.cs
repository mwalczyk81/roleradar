namespace RoleRadar.Domain.Tracking;

public sealed record PostingDifference(
    string Field,
    object? StoredValue,
    object? SuppliedValue);

public sealed record PostingCandidate(
    Guid Id,
    string Title,
    PostingDisposition Disposition,
    string? Reason);

public sealed record PostingSignal(
    Guid Id,
    string OwnerId,
    Guid PostingId,
    SignalType SignalType,
    string Evidence,
    DateTimeOffset ObservedAt,
    string? DeadlineDate = null);

public sealed class Posting
{
    public const int MaxTitleLength = 200;
    public const int MaxUrlLength = 1000;
    public const int MaxAtsTenantLength = 100;
    public const int MaxRequisitionIdLength = 100;
    public const int MaxLocationLength = 150;
    public const int MaxCompensationTextLength = 300;
    public const int MaxCultureSummaryLength = 500;
    public const int MaxDispositionReasonLength = 500;

    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string NormalizedTitle { get; private set; } = string.Empty;
    public string? OriginalUrl { get; private set; }
    public string? NormalizedUrl { get; private set; }
    public SourceKind SourceKind { get; private set; } = SourceKind.Ats;
    public string? AtsTenant { get; private set; }
    public string? RequisitionId { get; private set; }
    public string? DedupKeyA { get; private set; }
    public string? DedupKeyB { get; private set; }
    public string? DedupKeyC { get; private set; }
    public string? Location { get; private set; }
    public RemoteType RemoteType { get; private set; } = RemoteType.Unknown;
    public Compensation Compensation { get; private set; } = new();
    public string? PostedDate { get; private set; }
    public DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset LastVerifiedAt { get; private set; }
    public string? ClosedDate { get; private set; }
    public decimal? Score { get; private set; }
    public CultureLevel CultureLevel { get; private set; } = CultureLevel.Unknown;
    public string? CultureSummary { get; private set; }
    public PostingDisposition Disposition { get; private set; } = PostingDisposition.Lead;
    public string? DispositionReason { get; private set; }
    public Guid? DispositionCriterionId { get; private set; }
    public ResurfacePolicy ResurfacePolicy { get; private set; } = ResurfacePolicy.Never;
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DuplicateOfId { get; private set; }
    public Guid? ScreeningNoteId { get; private set; }
    public Guid VersionToken { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Posting Create(
        string ownerId,
        Guid companyId,
        string title,
        SourceKind sourceKind = SourceKind.Ats,
        string? originalUrl = null,
        string? atsTenant = null,
        string? requisitionId = null,
        string? location = null,
        RemoteType remoteType = RemoteType.Unknown,
        Compensation? compensation = null,
        string? postedDate = null,
        decimal? score = null,
        CultureLevel cultureLevel = CultureLevel.Unknown,
        string? cultureSummary = null,
        PostingDisposition disposition = PostingDisposition.Lead,
        string? dispositionReason = null,
        Guid? dispositionCriterionId = null,
        ResurfacePolicy resurfacePolicy = ResurfacePolicy.Never,
        DateTimeOffset? decidedAt = null,
        Guid? duplicateOfId = null,
        Guid? screeningNoteId = null,
        Guid? id = null,
        DateTimeOffset? now = null)
    {
        // Stub implementation for test-first phase
        var actualId = id ?? Guid.CreateVersion7();
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var normalizedTitle = TitleNormalizer.Normalize(title);
        var normalizedUrl = UrlNormalizer.Normalize(originalUrl);

        return new Posting
        {
            Id = actualId,
            OwnerId = ownerId ?? string.Empty,
            CompanyId = companyId,
            Title = title ?? string.Empty,
            NormalizedTitle = normalizedTitle,
            OriginalUrl = originalUrl,
            NormalizedUrl = normalizedUrl,
            SourceKind = sourceKind,
            AtsTenant = atsTenant,
            RequisitionId = requisitionId,
            DedupKeyA = !string.IsNullOrWhiteSpace(atsTenant) && !string.IsNullOrWhiteSpace(requisitionId) ? $"{atsTenant.Trim()}|{requisitionId.Trim()}" : null,
            DedupKeyB = normalizedUrl,
            DedupKeyC = string.IsNullOrWhiteSpace(atsTenant) && !string.IsNullOrWhiteSpace(requisitionId) ? $"{companyId:D}|{requisitionId.Trim()}" : null,
            Location = location,
            RemoteType = remoteType,
            Compensation = compensation ?? new(),
            PostedDate = postedDate,
            FirstSeenAt = timestamp,
            LastVerifiedAt = timestamp,
            Score = score,
            CultureLevel = cultureLevel,
            CultureSummary = cultureSummary,
            Disposition = disposition,
            DispositionReason = dispositionReason,
            DispositionCriterionId = dispositionCriterionId,
            ResurfacePolicy = resurfacePolicy,
            DecidedAt = decidedAt,
            DuplicateOfId = duplicateOfId,
            ScreeningNoteId = screeningNoteId,
            VersionToken = Guid.CreateVersion7(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }
}
