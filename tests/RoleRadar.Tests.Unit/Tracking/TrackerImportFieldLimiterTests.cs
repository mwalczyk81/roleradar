using RoleRadar.Application.Tracking.Import;
using RoleRadar.Domain.Notes;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class TrackerImportFieldLimiterTests
{
    private readonly TrackerImportSummary _summary = new();

    [Fact]
    public void CompanyDescription_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('A', Company.MaxDescriptionLength + 50);

        var result = TrackerImportFieldLimiter.LimitCompanyDescription(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Company.MaxDescriptionLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["company_description"]);
    }

    [Fact]
    public void CompanyName_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('B', Company.MaxNameLength + 30);

        var result = TrackerImportFieldLimiter.LimitCompanyName(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Company.MaxNameLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["company_name"]);
    }

    [Fact]
    public void PostingTitle_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('C', Posting.MaxTitleLength + 50);

        var result = TrackerImportFieldLimiter.LimitTitle(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxTitleLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["title"]);
    }

    [Fact]
    public void PostingLocation_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('D', Posting.MaxLocationLength + 40);

        var result = TrackerImportFieldLimiter.LimitLocation(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxLocationLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["location"]);
    }

    [Fact]
    public void CompensationText_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('E', Posting.MaxCompensationTextLength + 60);

        var result = TrackerImportFieldLimiter.LimitCompensationText(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxCompensationTextLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["compensation"]);
    }

    [Fact]
    public void CultureSummary_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('F', Posting.MaxCultureSummaryLength + 100);

        var result = TrackerImportFieldLimiter.LimitCultureSummary(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxCultureSummaryLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["culture_summary"]);
        Assert.Equal(1, _summary.CultureTruncations);
    }

    [Fact]
    public void DispositionReason_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('G', Posting.MaxDispositionReasonLength + 80);

        var result = TrackerImportFieldLimiter.LimitDispositionReason(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxDispositionReasonLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["disposition_reason"]);
    }

    [Fact]
    public void OutcomeReason_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('H', RoleRadar.Domain.Tracking.Application.MaxOutcomeReasonLength + 70);

        var result = TrackerImportFieldLimiter.LimitOutcomeReason(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(RoleRadar.Domain.Tracking.Application.MaxOutcomeReasonLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["outcome_reason"]);
    }

    [Fact]
    public void Url_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = "https://example.com/" + new string('u', Posting.MaxUrlLength + 50);

        var result = TrackerImportFieldLimiter.LimitUrl(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxUrlLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["url"]);
    }

    [Fact]
    public void RequisitionId_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('R', Posting.MaxRequisitionIdLength + 25);

        var result = TrackerImportFieldLimiter.LimitRequisitionId(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Posting.MaxRequisitionIdLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["requisition_id"]);
    }

    [Fact]
    public void NoteTitle_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('N', Note.MaxTitleChars + 40);

        var result = TrackerImportFieldLimiter.LimitNoteTitle(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Note.MaxTitleChars, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["note_title"]);
    }

    [Fact]
    public void NoteBody_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('B', 100);

        // Limit with small custom limit for test verification
        var result = TrackerImportFieldLimiter.Limit(input, 50, "notes", _summary);

        Assert.NotNull(result);
        Assert.Equal(50, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["notes"]);
    }

    [Fact]
    public void EventNotes_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('M', ApplicationEvent.MaxNotesLength + 60);

        var result = TrackerImportFieldLimiter.LimitEventNotes(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(ApplicationEvent.MaxNotesLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["event_notes"]);
    }

    [Fact]
    public void Referral_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('X', TrackerImportFieldLimiter.ReferralLimit + 30);

        var result = TrackerImportFieldLimiter.LimitReferral(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(TrackerImportFieldLimiter.ReferralLimit, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["referral"]);
    }

    [Fact]
    public void Fit_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('Y', TrackerImportFieldLimiter.FitLimit + 40);

        var result = TrackerImportFieldLimiter.LimitFit(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(TrackerImportFieldLimiter.FitLimit, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["fit"]);
    }

    [Fact]
    public void ResumeVariant_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('Z', TrackerImportFieldLimiter.ResumeVariantLimit + 20);

        var result = TrackerImportFieldLimiter.LimitResumeVariant(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(TrackerImportFieldLimiter.ResumeVariantLimit, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["resume_variant"]);
    }

    [Fact]
    public void CriteriaKey_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('K', Criterion.MaxKeyLength + 25);

        var result = TrackerImportFieldLimiter.LimitCriteriaKey(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Criterion.MaxKeyLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["criteria_key"]);
    }

    [Fact]
    public void CriteriaValue_OverLimit_TruncatedToDomainLimitAndCounted()
    {
        var input = new string('V', Criterion.MaxValueLength + 50);

        var result = TrackerImportFieldLimiter.LimitCriteriaValue(input, _summary);

        Assert.NotNull(result);
        Assert.Equal(Criterion.MaxValueLength, result.Length);
        Assert.Equal(1, _summary.FieldTruncations["criteria_value"]);
    }

    [Fact]
    public void WithinLimits_NeverTruncatedOrCounted()
    {
        const string title = "Software Engineer";
        var result = TrackerImportFieldLimiter.LimitTitle(title, _summary);

        Assert.Equal(title, result);
        Assert.Empty(_summary.FieldTruncations);
    }
}
