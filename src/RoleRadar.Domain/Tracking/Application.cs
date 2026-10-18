using System.Globalization;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (postingId == Guid.Empty)
        {
            throw new ArgumentException("Posting ID cannot be empty.", nameof(postingId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(submittedDate);
        if (!DateOnly.TryParseExact(submittedDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException($"Submitted date must be in YYYY-MM-DD format: '{submittedDate}'.", nameof(submittedDate));
        }

        string? validatedTargetStartDate = null;
        if (!string.IsNullOrWhiteSpace(targetStartDate))
        {
            if (!DateOnly.TryParseExact(targetStartDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                throw new ArgumentException($"Target start date must be in YYYY-MM-DD format: '{targetStartDate}'.", nameof(targetStartDate));
            }
            validatedTargetStartDate = targetStartDate.Trim();
        }

        var salary = desiredSalary ?? new DesiredSalary();
        if (salary.Min.HasValue && salary.Min.Value < 0)
        {
            throw new ArgumentException("Desired salary minimum must be >= 0.", nameof(desiredSalary));
        }

        if (salary.Min.HasValue && salary.Max.HasValue && salary.Max.Value < salary.Min.Value)
        {
            throw new ArgumentException("Desired salary maximum must be greater than or equal to minimum.", nameof(desiredSalary));
        }

        if (string.IsNullOrWhiteSpace(salary.Currency) || salary.Currency.Trim().Length != 3)
        {
            throw new ArgumentException("Desired salary currency must be a 3-character currency code.", nameof(desiredSalary));
        }

        string? validatedResumePointer = null;
        if (resumePointer is not null)
        {
            if (resumePointer.Length > MaxResumePointerLength)
            {
                throw new ArgumentException($"Resume pointer cannot exceed {MaxResumePointerLength} characters.", nameof(resumePointer));
            }

            if (resumePointer.Contains('\r') || resumePointer.Contains('\n'))
            {
                throw new ArgumentException("Resume pointer cannot contain newline characters or raw resume document content.", nameof(resumePointer));
            }

            validatedResumePointer = resumePointer.Trim();
        }

        string? validatedConfirmationCode = null;
        if (confirmationCode is not null)
        {
            if (confirmationCode.Length > MaxConfirmationCodeLength)
            {
                throw new ArgumentException($"Confirmation code cannot exceed {MaxConfirmationCodeLength} characters.", nameof(confirmationCode));
            }
            validatedConfirmationCode = confirmationCode.Trim();
        }

        string? validatedOutcomeReason = null;
        if (outcomeReason is not null)
        {
            if (outcomeReason.Length > MaxOutcomeReasonLength)
            {
                throw new ArgumentException($"Outcome reason cannot exceed {MaxOutcomeReasonLength} characters.", nameof(outcomeReason));
            }
            validatedOutcomeReason = outcomeReason.Trim();
        }

        var ts = now ?? DateTimeOffset.UtcNow;
        return new Application
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId.Trim(),
            PostingId = postingId,
            SubmittedDate = submittedDate.Trim(),
            DesiredSalary = salary,
            TargetStartDate = validatedTargetStartDate,
            ResumeVariant = resumeVariant,
            ResumePointer = validatedResumePointer,
            ReferralContactId = referralContactId,
            ConfirmationCode = validatedConfirmationCode,
            Outcome = outcome,
            OutcomeReason = validatedOutcomeReason,
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
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (trimmedNotes is not null && trimmedNotes.Length > MaxOutcomeReasonLength)
        {
            trimmedNotes = trimmedNotes[..MaxOutcomeReasonLength];
        }

        switch (eventType)
        {
            case ApplicationEventType.Offer:
            case ApplicationEventType.Applied:
            case ApplicationEventType.FollowUp:
            case ApplicationEventType.Interview:
                // Non-terminal events: outcome remains unchanged
                break;

            case ApplicationEventType.Rejected:
                Outcome = ApplicationOutcome.Rejected;
                OutcomeReason = trimmedNotes;
                UpdatedAt = timestamp;
                VersionToken = Guid.CreateVersion7();
                break;

            case ApplicationEventType.OfferAccepted:
            case ApplicationEventType.OfferDeclined:
            case ApplicationEventType.Withdrawn:
                Outcome = ApplicationOutcome.Closed;
                OutcomeReason = trimmedNotes;
                UpdatedAt = timestamp;
                VersionToken = Guid.CreateVersion7();
                break;
        }
    }
}
