using RoleRadar.Domain.Notes;
using RoleRadar.Domain.Tracking;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Application.Tracking.Import;

/// <summary>
/// Enforces domain-level length constraints on free-text inputs across all tracker import steps.
/// Truncates inputs to their domain limits, records each truncation in the summary and report,
/// and prevents unbounded strings from triggering validation exceptions in domain factories.
/// </summary>
public static class TrackerImportFieldLimiter
{
    public const int CompanyDescriptionLimit = Company.MaxDescriptionLength; // 300
    public const int CompanyNameLimit = Company.MaxNameLength; // 150
    public const int PostingTitleLimit = Posting.MaxTitleLength; // 200
    public const int PostingLocationLimit = Posting.MaxLocationLength; // 150
    public const int CompensationTextLimit = Posting.MaxCompensationTextLength; // 300
    public const int CultureSummaryLimit = Posting.MaxCultureSummaryLength; // 500
    public const int DispositionReasonLimit = Posting.MaxDispositionReasonLength; // 500
    public const int OutcomeReasonLimit = ApplicationEntity.MaxOutcomeReasonLength; // 500
    public const int UrlLimit = Posting.MaxUrlLength; // 1000
    public const int RequisitionIdLimit = Posting.MaxRequisitionIdLength; // 100
    public const int NoteTitleLimit = Note.MaxTitleChars; // 300
    public const int NoteBodyLimit = Note.MaxBodyBytes; // 8 * 1024 * 1024 (8 MiB)
    public const int EventNotesLimit = ApplicationEvent.MaxNotesLength; // 500
    public const int ResumePointerLimit = ApplicationEntity.MaxResumePointerLength; // 300
    public const int ReferralLimit = 100;
    public const int FitLimit = 100;
    public const int ResumeVariantLimit = 100;
    public const int CriteriaKeyLimit = Criterion.MaxKeyLength; // 100
    public const int CriteriaValueLimit = Criterion.MaxValueLength; // 500

    public static string? Limit(string? value, int maxLength, string fieldName, TrackerImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        if (value is null)
        {
            return null;
        }

        if (value.Length > maxLength)
        {
            summary.RecordFieldTruncation(fieldName);
            return value[..maxLength];
        }

        return value;
    }

    public static string LimitRequired(string value, int maxLength, string fieldName, TrackerImportSummary summary)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        if (value.Length > maxLength)
        {
            summary.RecordFieldTruncation(fieldName);
            return value[..maxLength];
        }

        return value;
    }

    public static string? LimitCompanyDescription(string? value, TrackerImportSummary summary) =>
        Limit(value, CompanyDescriptionLimit, "company_description", summary);

    public static string? LimitCompanyName(string? value, TrackerImportSummary summary) =>
        Limit(value, CompanyNameLimit, "company_name", summary);

    public static string? LimitTitle(string? value, TrackerImportSummary summary) =>
        Limit(value, PostingTitleLimit, "title", summary);

    public static string? LimitLocation(string? value, TrackerImportSummary summary) =>
        Limit(value, PostingLocationLimit, "location", summary);

    public static string? LimitCompensationText(string? value, TrackerImportSummary summary) =>
        Limit(value, CompensationTextLimit, "compensation", summary);

    public static string? LimitCultureSummary(string? value, TrackerImportSummary summary) =>
        Limit(value, CultureSummaryLimit, "culture_summary", summary);

    public static string? LimitDispositionReason(string? value, TrackerImportSummary summary) =>
        Limit(value, DispositionReasonLimit, "disposition_reason", summary);

    public static string? LimitOutcomeReason(string? value, TrackerImportSummary summary) =>
        Limit(value, OutcomeReasonLimit, "outcome_reason", summary);

    public static string? LimitUrl(string? value, TrackerImportSummary summary) =>
        Limit(value, UrlLimit, "url", summary);

    public static string? LimitRequisitionId(string? value, TrackerImportSummary summary)
    {
        var limited = Limit(value, RequisitionIdLimit, "requisition_id", summary);
        if (string.IsNullOrWhiteSpace(limited))
        {
            return null;
        }

        if (!limited.Any(char.IsDigit))
        {
            summary.RecordUnmappedValue("req (no digit)", limited);
            return null;
        }

        return limited;
    }

    public static string? LimitNoteTitle(string? value, TrackerImportSummary summary) =>
        Limit(value, NoteTitleLimit, "note_title", summary);

    public static string? LimitNoteBody(string? value, TrackerImportSummary summary) =>
        Limit(value, NoteBodyLimit, "notes", summary);

    public static string? LimitEventNotes(string? value, TrackerImportSummary summary) =>
        Limit(value, EventNotesLimit, "event_notes", summary);

    public static string? LimitReferral(string? value, TrackerImportSummary summary) =>
        Limit(value, ReferralLimit, "referral", summary);

    public static string? LimitFit(string? value, TrackerImportSummary summary) =>
        Limit(value, FitLimit, "fit", summary);

    public static string? LimitResumeVariant(string? value, TrackerImportSummary summary) =>
        Limit(value, ResumeVariantLimit, "resume_variant", summary);

    public static string? LimitCriteriaKey(string? value, TrackerImportSummary summary) =>
        Limit(value, CriteriaKeyLimit, "criteria_key", summary);

    public static string? LimitCriteriaValue(string? value, TrackerImportSummary summary) =>
        Limit(value, CriteriaValueLimit, "criteria_value", summary);
}
