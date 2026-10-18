using RoleRadar.Domain.Tracking;
using Xunit;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class ApplicationDomainTests
{
    [Fact]
    public void Create_ValidInputs_SucceedsAndSetsProperties()
    {
        var ownerId = "owner-test-1";
        var postingId = Guid.NewGuid();
        var submittedDate = "2026-10-18";
        var desiredSalary = new DesiredSalary(180000m, 210000m, "USD");
        var resumeVariant = ResumeVariant.Tailored;
        var resumePointer = "local://resumes/staff-engineer-2026.pdf";
        var referralContactId = Guid.NewGuid();
        var confirmationCode = "APP-CONF-12345";

        var application = ApplicationEntity.Create(
            ownerId: ownerId,
            postingId: postingId,
            submittedDate: submittedDate,
            resumeVariant: resumeVariant,
            desiredSalary: desiredSalary,
            targetStartDate: "2026-12-01",
            resumePointer: resumePointer,
            referralContactId: referralContactId,
            confirmationCode: confirmationCode);

        Assert.NotEqual(Guid.Empty, application.Id);
        Assert.Equal(ownerId, application.OwnerId);
        Assert.Equal(postingId, application.PostingId);
        Assert.Equal(submittedDate, application.SubmittedDate);
        Assert.Equal(resumeVariant, application.ResumeVariant);
        Assert.Equal(180000m, application.DesiredSalary.Min);
        Assert.Equal(210000m, application.DesiredSalary.Max);
        Assert.Equal("USD", application.DesiredSalary.Currency);
        Assert.Equal("2026-12-01", application.TargetStartDate);
        Assert.Equal(resumePointer, application.ResumePointer);
        Assert.Equal(referralContactId, application.ReferralContactId);
        Assert.Equal(confirmationCode, application.ConfirmationCode);
        Assert.Equal(ApplicationOutcome.Applied, application.Outcome);
        Assert.NotEqual(Guid.Empty, application.VersionToken);
    }

    [Theory]
    [InlineData(-1000.0, 200000.0, "USD")]
    [InlineData(220000.0, 200000.0, "USD")]
    [InlineData(180000.0, 200000.0, "US")]
    [InlineData(180000.0, 200000.0, "USDT")]
    public void Create_InvalidDesiredSalary_ThrowsArgumentException(double min, double max, string currency)
    {
        var salary = new DesiredSalary((decimal)min, (decimal)max, currency);
        Assert.Throws<ArgumentException>(() => ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored,
            desiredSalary: salary));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-date")]
    [InlineData("2026/10/18")]
    [InlineData("2026-13-01")]
    [InlineData("2026-10-32")]
    public void Create_InvalidSubmittedDate_ThrowsArgumentException(string invalidDate)
    {
        Assert.Throws<ArgumentException>(() => ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: invalidDate,
            resumeVariant: ResumeVariant.Tailored));
    }

    [Theory]
    [InlineData("not-a-date")]
    [InlineData("2026/12/01")]
    [InlineData("2026-02-30")]
    public void Create_InvalidTargetStartDate_ThrowsArgumentException(string invalidDate)
    {
        Assert.Throws<ArgumentException>(() => ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored,
            targetStartDate: invalidDate));
    }

    [Fact]
    public void Create_ResumePointerExceeds300Chars_ThrowsArgumentException()
    {
        var longPointer = new string('a', 301);
        Assert.Throws<ArgumentException>(() => ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored,
            resumePointer: longPointer));
    }

    [Fact]
    public void Create_ResumePointerContainsRawDocumentTextWithNewlines_ThrowsArgumentException()
    {
        var rawResumeContent = "John Doe\nSummary: Senior Engineer with 10 years experience\nSkills: C#, MongoDB";
        Assert.Throws<ArgumentException>(() => ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored,
            resumePointer: rawResumeContent));
    }

    [Fact]
    public void Create_ConfirmationCodeExceeds100Chars_ThrowsArgumentException()
    {
        var longCode = new string('c', 101);
        Assert.Throws<ArgumentException>(() => ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored,
            confirmationCode: longCode));
    }

    [Fact]
    public void ApplicationEvent_Create_ValidInputs_Succeeds()
    {
        var ownerId = "owner-1";
        var appId = Guid.NewGuid();
        var ev = ApplicationEvent.Create(
            ownerId: ownerId,
            applicationId: appId,
            eventDate: "2026-10-18",
            eventType: ApplicationEventType.Interview,
            eventSource: "email",
            notes: "Technical phone screen scheduled for next week.");

        Assert.NotEqual(Guid.Empty, ev.Id);
        Assert.Equal(ownerId, ev.OwnerId);
        Assert.Equal(appId, ev.ApplicationId);
        Assert.Equal("2026-10-18", ev.EventDate);
        Assert.Equal(ApplicationEventType.Interview, ev.EventType);
        Assert.Equal("email", ev.EventSource);
        Assert.Equal("Technical phone screen scheduled for next week.", ev.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid-date")]
    [InlineData("2026-00-10")]
    public void ApplicationEvent_Create_InvalidDate_ThrowsArgumentException(string invalidDate)
    {
        Assert.Throws<ArgumentException>(() => ApplicationEvent.Create(
            ownerId: "owner-1",
            applicationId: Guid.NewGuid(),
            eventDate: invalidDate,
            eventType: ApplicationEventType.FollowUp));
    }

    [Fact]
    public void ApplicationEvent_Create_EventSourceExceeds100Chars_ThrowsArgumentException()
    {
        var longSource = new string('s', 101);
        Assert.Throws<ArgumentException>(() => ApplicationEvent.Create(
            ownerId: "owner-1",
            applicationId: Guid.NewGuid(),
            eventDate: "2026-10-18",
            eventType: ApplicationEventType.Applied,
            eventSource: longSource));
    }

    [Fact]
    public void ApplicationEvent_Create_NotesExceeds500Chars_ThrowsArgumentException()
    {
        var longNotes = new string('n', 501);
        Assert.Throws<ArgumentException>(() => ApplicationEvent.Create(
            ownerId: "owner-1",
            applicationId: Guid.NewGuid(),
            eventDate: "2026-10-18",
            eventType: ApplicationEventType.Applied,
            notes: longNotes));
    }

    [Fact]
    public void ApplyEvent_Offer_IsNonTerminal_OutcomeRemainsApplied()
    {
        var app = ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored);

        var originalToken = app.VersionToken;
        app.ApplyEvent(ApplicationEventType.Offer, "Received formal offer", DateTimeOffset.UtcNow);

        Assert.Equal(ApplicationOutcome.Applied, app.Outcome);
    }

    [Fact]
    public void ApplyEvent_Rejected_OutcomeTransitionsToRejectedAndUpdatesReason()
    {
        var app = ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored);

        var originalToken = app.VersionToken;
        var now = DateTimeOffset.UtcNow;
        app.ApplyEvent(ApplicationEventType.Rejected, "Position closed to internal candidate", now);

        Assert.Equal(ApplicationOutcome.Rejected, app.Outcome);
        Assert.Equal("Position closed to internal candidate", app.OutcomeReason);
        Assert.NotEqual(originalToken, app.VersionToken);
        Assert.Equal(now, app.UpdatedAt);
    }

    [Theory]
    [InlineData(ApplicationEventType.OfferAccepted)]
    [InlineData(ApplicationEventType.OfferDeclined)]
    [InlineData(ApplicationEventType.Withdrawn)]
    public void ApplyEvent_ClosingEvents_OutcomeTransitionsToClosed(ApplicationEventType eventType)
    {
        var app = ApplicationEntity.Create(
            ownerId: "owner-1",
            postingId: Guid.NewGuid(),
            submittedDate: "2026-10-18",
            resumeVariant: ResumeVariant.Tailored);

        var originalToken = app.VersionToken;
        var now = DateTimeOffset.UtcNow;
        app.ApplyEvent(eventType, "Closing timeline event", now);

        Assert.Equal(ApplicationOutcome.Closed, app.Outcome);
        Assert.Equal("Closing timeline event", app.OutcomeReason);
        Assert.NotEqual(originalToken, app.VersionToken);
        Assert.Equal(now, app.UpdatedAt);
    }
}
