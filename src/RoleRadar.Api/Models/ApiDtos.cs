namespace RoleRadar.Api.Models;

// Postings DTOs
public sealed record PostingListItemDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Title,
    string? RequisitionId,
    string? Location,
    string Disposition,
    string ResurfacePolicy,
    decimal? Score,
    Guid VersionToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PostingCompensationDto(
    string Currency,
    string? RawText,
    bool HasSalary,
    bool? CompHighPriority);

public sealed record PostingSignalDto(
    string SignalType,
    string Evidence,
    DateTimeOffset DetectedAt);

public sealed record AssessmentNoteSummaryDto(
    Guid Id,
    string Summary,
    DateTimeOffset UpdatedAt);

public sealed record PostingDetailDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    string Title,
    string? RequisitionId,
    string? Location,
    string Disposition,
    string ResurfacePolicy,
    decimal? Score,
    Guid VersionToken,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Url,
    PostingCompensationDto Compensation,
    IReadOnlyList<PostingSignalDto> Signals,
    AssessmentNoteSummaryDto? AssessmentNote);

public sealed record TransitionPostingRequestDto(
    string ToDisposition,
    string Reason,
    Guid? CriterionId,
    string VersionToken,
    string? ResurfacePolicy = null,
    string? OverrideReason = null);

// Companies DTOs
public sealed record CareerSourceDto(
    string SourceType,
    string AtsProvider,
    string? AtsTenant,
    string? SourceUrl,
    string FetchMethod,
    bool Active);

public sealed record CompanyDetailDto(
    Guid Id,
    string Name,
    string Status,
    string OwnershipType,
    string? Description,
    string? Headquarters,
    IReadOnlyList<string> CategoryTags,
    IReadOnlyList<CareerSourceDto> CareerSources,
    Guid VersionToken);

public sealed record UpdateCareerSourceRequestDto(
    Guid? Id = null,
    string? SourceType = null,
    string? AtsProvider = null,
    string? AtsTenant = null,
    string? SourceUrl = null,
    string? FetchMethod = null,
    bool? Active = null,
    Guid? VersionToken = null);

public sealed record UpdateCompanyRequestDto(
    string? Name = null,
    string? Status = null,
    string? OwnershipType = null,
    string? Description = null,
    string? Headquarters = null,
    IReadOnlyList<string>? CategoryTags = null,
    IReadOnlyList<UpdateCareerSourceRequestDto>? CareerSources = null,
    Guid? VersionToken = null);

// Applications DTOs
public sealed record ApplicationEventDto(
    string EventType,
    string EventDate,
    string? Notes);

public sealed record ApplicationDetailDto(
    Guid Id,
    Guid PostingId,
    Guid CompanyId,
    string Status,
    string AppliedAt,
    IReadOnlyList<ApplicationEventDto> Events);

public sealed record RecordApplicationRequestDto(
    Guid PostingId,
    string SubmittedDate,
    string ResumeVariant = "tailored",
    decimal? DesiredSalaryMin = null,
    decimal? DesiredSalaryMax = null,
    string? DesiredSalaryCurrency = "USD",
    string? TargetStartDate = null,
    string? ResumePointer = null,
    string? ConfirmationCode = null,
    bool OverrideDecided = false,
    string? OverrideReason = null);

public sealed record AppendApplicationEventRequestDto(
    string EventType,
    string EventDate,
    string? Notes = null,
    string? EventSource = null);

// Criteria, Notes, and Search Runs DTOs
public sealed record CriterionDto(
    Guid Id,
    string Name,
    string Weight,
    string RuleText,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    bool IsActive);

public sealed record NoteDto(
    Guid Id,
    string Heading,
    string Content,
    IReadOnlyList<string> Tags,
    string? LinkedEntityType,
    Guid? LinkedEntityId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SearchRunDto(
    Guid Id,
    DateTimeOffset RunDate,
    int CompaniesSearched,
    int PostingsFound,
    int NewPostings,
    int ErrorsCount,
    string? Summary);
