namespace RoleRadar.Domain.Tracking;

public sealed class Application
{
    public const int MaxResumePointerLength = 300;
    public const int MaxConfirmationCodeLength = 100;
    public const int MaxOutcomeReasonLength = 500;

    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public Guid PostingId { get; init; }
    public string SubmittedDate { get; private set; } = string.Empty;
    public DesiredSalary DesiredSalary { get; private set; } = new();
    public string? TargetStartDate { get; private set; }
    public ResumeVariant ResumeVariant { get; private set; }
    public string? ResumePointer { get; private set; }
    public Guid? ReferralContactId { get; private set; }
    public string? ConfirmationCode { get; private set; }
    public ApplicationOutcome Outcome { get; private set; } = ApplicationOutcome.Applied;
    public string? OutcomeReason { get; private set; }
    public Guid VersionToken { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Application Create(
        string ownerId,
        Guid postingId,
        string submittedDate,
        ResumeVariant resumeVariant,
        DesiredSalary? desiredSalary = null,
        string? targetStartDate = null,
        string? resumePointer = null,
        Guid? referralContactId = null,
        string? confirmationCode = null,
        ApplicationOutcome outcome = ApplicationOutcome.Applied,
        string? outcomeReason = null,
        Guid? id = null,
        Guid? versionToken = null,
        DateTimeOffset? now = null)
    {
        // Stub implementation for test-first phase - intentionally does not validate invariants
        var ts = now ?? DateTimeOffset.UtcNow;
        return new Application
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId ?? string.Empty,
            PostingId = postingId,
            SubmittedDate = submittedDate ?? string.Empty,
            DesiredSalary = desiredSalary ?? new(),
            TargetStartDate = targetStartDate,
            ResumeVariant = resumeVariant,
            ResumePointer = resumePointer,
            ReferralContactId = referralContactId,
            ConfirmationCode = confirmationCode,
            Outcome = outcome,
            OutcomeReason = outcomeReason,
            VersionToken = versionToken ?? Guid.CreateVersion7(),
            CreatedAt = ts,
            UpdatedAt = ts
        };
    }

    public static Application Rehydrate(
        Guid id,
        string ownerId,
        Guid postingId,
        string submittedDate,
        DesiredSalary desiredSalary,
        string? targetStartDate,
        ResumeVariant resumeVariant,
        string? resumePointer,
        Guid? referralContactId,
        string? confirmationCode,
        ApplicationOutcome outcome,
        string? outcomeReason,
        Guid versionToken,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new Application
        {
            Id = id,
            OwnerId = ownerId,
            PostingId = postingId,
            SubmittedDate = submittedDate,
            DesiredSalary = desiredSalary,
            TargetStartDate = targetStartDate,
            ResumeVariant = resumeVariant,
            ResumePointer = resumePointer,
            ReferralContactId = referralContactId,
            ConfirmationCode = confirmationCode,
            Outcome = outcome,
            OutcomeReason = outcomeReason,
            VersionToken = versionToken,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void ApplyEvent(ApplicationEventType eventType, string? notes, DateTimeOffset timestamp)
    {
        // Stub - does not mutate outcome
    }
}
