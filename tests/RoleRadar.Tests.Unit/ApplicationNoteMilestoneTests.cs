namespace RoleRadar.Tests.Unit;

using MongoDB.Bson;
using RoleRadar.Infrastructure.Mongo.Repositories;
using Xunit;

/// <summary>
/// Feature 008: a note event is inert for every derived application field, including the last-event date, and
/// so is any event type the milestone code does not name. The milestone code counts only the named types.
/// All data is fictional.
/// </summary>
public sealed class ApplicationNoteMilestoneTests
{
    private static readonly DateTime DefaultCreated = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BsonDocument Event(string type, string eventDate) =>
        new() { ["event_type"] = type, ["event_date"] = eventDate };

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static readonly (string Name, (string Type, string Date)[] Events)[] BaseEventSets =
    [
        ("applied only", [("applied", "2026-02-01")]),
        ("silent follow-up", [("applied", "2026-02-01"), ("follow_up", "2026-02-10")]),
        ("interview", [("applied", "2026-02-01"), ("interview", "2026-02-12")]),
        ("offer after interview", [("applied", "2026-02-01"), ("interview", "2026-02-12"), ("offer", "2026-02-20")]),
        ("rejected before any interview", [("applied", "2026-02-01"), ("rejected", "2026-02-09")]),
        ("rejected after interview", [("applied", "2026-02-01"), ("interview", "2026-02-05"), ("rejected", "2026-02-09")]),
        ("withdrawn", [("applied", "2026-02-01"), ("withdrawn", "2026-02-15")]),
        ("offer declined", [("applied", "2026-02-01"), ("offer", "2026-02-10"), ("offer_declined", "2026-02-14")]),
    ];

    private static readonly string[] NamedTypes = ["applied", "follow_up", "interview", "offer", "offer_accepted", "offer_declined", "rejected", "withdrawn"];

    private static readonly string[] NoteDates = ["2026-01-15", "2026-02-06", "2026-03-30"];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void AddingNotes_ChangesNoMilestone_AtAnyDate(int setIndex)
    {
        var (name, baseEvents) = BaseEventSets[setIndex];
        var without = baseEvents.Select(e => Event(e.Type, e.Date)).ToList();
        var baseline = MongoApplicationRepository.ComputeApplicationMilestones(without, DefaultCreated);

        // A note before everything, between events, and after everything.
        foreach (var noteDate in NoteDates)
        {
            var with = new List<BsonDocument>(without) { Event("note", noteDate) };
            var withNote = MongoApplicationRepository.ComputeApplicationMilestones(with, DefaultCreated);

            Assert.True(baseline == withNote, $"{name}: a note dated {noteDate} changed the milestones.");
        }
    }

    [Fact]
    public void ApplicationWithOnlyNotes_HasNoMilestonesAtAll()
    {
        var events = new[] { Event("note", "2026-02-10"), Event("note", "2026-02-20") };

        var milestones = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.False(milestones.HasInterview);
        Assert.False(milestones.HasOffer);
        Assert.False(milestones.HasRejection);
        Assert.False(milestones.RejectedBeforeInterview);
        Assert.Null(milestones.FirstResponseAt);
        Assert.Null(milestones.LastEventAt);
    }

    [Fact]
    public void NoteAfterAnOldSilentApplication_KeepsItNoResponse_AndKeepsTheLastEventDate()
    {
        var events = new[] { Event("applied", "2026-01-05"), Event("note", "2026-03-01") };

        var milestones = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Null(milestones.FirstResponseAt);
        Assert.Equal(Utc(2026, 1, 5), milestones.LastEventAt);
    }

    [Fact]
    public void CorrectionNoteAfterARejection_IsNotASecondRejection_AndMovesNothing()
    {
        var events = new[]
        {
            Event("applied", "2026-02-01"), Event("rejected", "2026-02-09"), Event("note", "2026-02-11")
        };

        var milestones = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.True(milestones.HasRejection);
        Assert.True(milestones.RejectedBeforeInterview);
        Assert.Equal(Utc(2026, 2, 9), milestones.FirstResponseAt);
        Assert.Equal(Utc(2026, 2, 9), milestones.LastEventAt);
    }

    [Theory]
    [InlineData("note")]
    [InlineData("NOTE")]
    [InlineData("something_unknown")]
    [InlineData("")]
    public void OnlyNamedTypesCount_AnyOtherTypeIsIgnoredByConstruction(string type)
    {
        var events = new[] { Event("applied", "2026-02-01"), Event(type, "2026-04-01") };

        var milestones = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Null(milestones.FirstResponseAt);
        Assert.False(milestones.HasInterview);
        Assert.False(milestones.HasOffer);
        Assert.False(milestones.HasRejection);
        Assert.Equal(Utc(2026, 2, 1), milestones.LastEventAt);
    }

    [Fact]
    public void NamedTypes_StillCountAsBefore()
    {
        // Guard for the allowlist: every named type keeps contributing to the last-event date.
        foreach (var type in NamedTypes)
        {
            var milestones = MongoApplicationRepository.ComputeApplicationMilestones([Event(type, "2026-02-10")], DefaultCreated);

            Assert.Equal(Utc(2026, 2, 10), milestones.LastEventAt);
        }
    }
}
