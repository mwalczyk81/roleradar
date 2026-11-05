using RoleRadar.Domain.Tracking;
using Xunit;
using DomainApplication = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Tests.Unit.Tracking;

/// <summary>Feature 008: the note event type is non-terminal, never changes the outcome, and round trips as text.</summary>
public sealed class ApplicationNoteEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Note_IsTheLastOfNineEventTypes()
    {
        var values = Enum.GetValues<ApplicationEventType>();

        Assert.Equal(9, values.Length);
        Assert.Equal(ApplicationEventType.Note, values[^1]);
    }

    [Fact]
    public void ApplyEvent_Note_LeavesAnAppliedApplicationCompletelyUnchanged()
    {
        var app = DomainApplication.Create("owner-1", Guid.NewGuid(), "2026-09-01", ResumeVariant.Tailored);
        var version = app.VersionToken;
        var updated = app.UpdatedAt;

        app.ApplyEvent(ApplicationEventType.Note, "A remark about the recruiter call", Now);

        Assert.Equal(ApplicationOutcome.Applied, app.Outcome);
        Assert.Null(app.OutcomeReason);
        Assert.Equal(version, app.VersionToken);
        Assert.Equal(updated, app.UpdatedAt);
    }

    [Theory]
    [InlineData(ApplicationOutcome.Rejected)]
    [InlineData(ApplicationOutcome.Closed)]
    public void ApplyEvent_Note_NeverReopensOrChangesAFinalOutcome(ApplicationOutcome outcome)
    {
        var app = DomainApplication.Create(
            "owner-1", Guid.NewGuid(), "2026-09-01", ResumeVariant.Tailored, outcome: outcome, outcomeReason: "Original reason");
        var version = app.VersionToken;

        app.ApplyEvent(ApplicationEventType.Note, "Correction: the date was a day later", Now);

        Assert.Equal(outcome, app.Outcome);
        Assert.Equal("Original reason", app.OutcomeReason);
        Assert.Equal(version, app.VersionToken);
    }

    [Fact]
    public void EveryEventType_RoundTripsThroughItsSnakeCaseText()
    {
        foreach (var value in Enum.GetValues<ApplicationEventType>())
        {
            var text = value.ToSnakeCase();

            Assert.True(TrackingEnumExtensions.TryParseApplicationEventType(text, out var parsed), $"'{text}' does not parse.");
            Assert.Equal(value, parsed);
        }
    }

    [Theory]
    [InlineData("note")]
    [InlineData("Note")]
    [InlineData(" NOTE ")]
    public void Note_ParsesCaseInsensitively_AndWritesAsLowerCase(string text)
    {
        Assert.True(TrackingEnumExtensions.TryParseApplicationEventType(text, out var parsed));
        Assert.Equal(ApplicationEventType.Note, parsed);
        Assert.Equal("note", parsed.ToSnakeCase());
    }

    [Fact]
    public void ApplicationEvent_CanBeCreatedWithTheNoteType()
    {
        var ev = ApplicationEvent.Create("owner-1", Guid.NewGuid(), "2026-10-05", ApplicationEventType.Note, "web", "A remark");

        Assert.Equal(ApplicationEventType.Note, ev.EventType);
        Assert.Equal("A remark", ev.Notes);
    }
}
