namespace RoleRadar.Tests.Unit;

using MongoDB.Bson;
using RoleRadar.Infrastructure.Mongo.Repositories;
using Xunit;

/// <summary>
/// Covers the single shared milestone / derived-field implementation used by both
/// <see cref="MongoApplicationRepository"/> and the read-model reconcile service.
/// All data is fictional.
/// </summary>
public sealed class ApplicationMilestoneTests
{
    // Used only when an event has neither a usable event_date nor created_at.
    private static readonly DateTime DefaultCreated = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BsonDocument Event(string type, string? eventDate, DateTime? createdAt = null)
    {
        var doc = new BsonDocument { ["event_type"] = type };
        if (eventDate is not null)
        {
            doc["event_date"] = eventDate;
        }

        if (createdAt.HasValue)
        {
            doc["created_at"] = BsonDateTime.Create(createdAt.Value);
        }

        return doc;
    }

    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoEvents_YieldsNoMilestones()
    {
        // lastEventAt is null only when an application has no events at all.
        var (hasInterview, first, last) = MongoApplicationRepository.ComputeApplicationMilestones([], DefaultCreated);

        Assert.False(hasInterview);
        Assert.Null(first);
        Assert.Null(last);
    }

    [Fact]
    public void FollowUpOnly_DoesNotCountAsFirstResponse_ButSetsLastEvent()
    {
        // follow_up is the candidate's own action, so it is not a response from the employer.
        var events = new[] { Event("follow_up", "2026-02-10") };

        var (hasInterview, first, last) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.False(hasInterview);
        Assert.Null(first);
        Assert.Equal(Utc(2026, 2, 10), last);
    }

    [Fact]
    public void Withdrawn_DoesNotCountAsFirstResponse()
    {
        // withdrawn is also a candidate action.
        var events = new[] { Event("withdrawn", "2026-02-12") };

        var (_, first, last) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Null(first);
        Assert.Equal(Utc(2026, 2, 12), last);
    }

    [Theory]
    [InlineData("interview")]
    [InlineData("offer")]
    [InlineData("offer_accepted")]
    [InlineData("offer_declined")]
    [InlineData("rejected")]
    public void EmployerResponseTypes_CountAsFirstResponse(string eventType)
    {
        var events = new[] { Event(eventType, "2026-03-03") };

        var (_, first, last) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Equal(Utc(2026, 3, 3), first);
        Assert.Equal(Utc(2026, 3, 3), last);
    }

    [Fact]
    public void OfferAccepted_AfterFollowUp_FirstResponseIsTheOfferAcceptedDate()
    {
        var events = new[]
        {
            Event("follow_up", "2026-03-01"),
            Event("offer_accepted", "2026-03-20"),
        };

        var (_, first, last) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Equal(Utc(2026, 3, 20), first);
        Assert.Equal(Utc(2026, 3, 20), last);
    }

    [Fact]
    public void HasInterview_IsTrueOnlyForInterviewEvents()
    {
        var withoutInterview = new[] { Event("offer", "2026-04-01"), Event("rejected", "2026-04-02") };
        var withInterview = new[] { Event("follow_up", "2026-04-01"), Event("interview", "2026-04-05") };

        Assert.False(MongoApplicationRepository.ComputeApplicationMilestones(withoutInterview, DefaultCreated).HasInterview);
        Assert.True(MongoApplicationRepository.ComputeApplicationMilestones(withInterview, DefaultCreated).HasInterview);
    }

    [Fact]
    public void BackfilledEvents_AreOrderedByEventDate_NotCreatedAt()
    {
        // Both events were backfilled "today" (created_at), but with reversed insertion order.
        // The earlier-dated interview was inserted LAST; the later-dated offer was inserted FIRST.
        var backfillTime = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            Event("offer", "2026-01-20", backfillTime),
            Event("interview", "2026-01-05", backfillTime.AddMinutes(5)),
        };

        var (_, first, last) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Equal(Utc(2026, 1, 5), first);
        Assert.Equal(Utc(2026, 1, 20), last);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-date")]
    [InlineData("2026-13-45")]
    public void MissingOrUnparseableEventDate_FallsBackToCreatedAt(string? eventDate)
    {
        var createdAt = new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var events = new[] { Event("interview", eventDate, createdAt) };

        var (_, first, last) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Equal(createdAt, first);
        Assert.Equal(createdAt, last);
    }

    [Fact]
    public void ValidEventDate_WinsOverCreatedAt()
    {
        var events = new[] { Event("interview", "2026-01-05", new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc)) };

        var (_, first, _) = MongoApplicationRepository.ComputeApplicationMilestones(events, DefaultCreated);

        Assert.Equal(Utc(2026, 1, 5), first);
    }

    [Theory]
    [InlineData("applied", "applied")]
    [InlineData("rejected", "rejected")]
    [InlineData("closed", "closed")]
    [InlineData("Rejected", "rejected")]
    [InlineData(null, "applied")]
    [InlineData("", "applied")]
    public void DeriveStatus_MapsLegacyOutcome_ToSnakeCaseStatus(string? outcome, string expected)
    {
        Assert.Equal(expected, MongoApplicationRepository.DeriveStatus(outcome));
    }

    [Fact]
    public void DeriveAppliedAt_ParsesSubmittedDate_AsUtcMidnight()
    {
        var created = new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc);

        var appliedAt = MongoApplicationRepository.DeriveAppliedAt("2026-10-01", created);

        Assert.Equal(Utc(2026, 10, 1), appliedAt);
        Assert.Equal(DateTimeKind.Utc, appliedAt.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("10/01/2026")]
    public void DeriveAppliedAt_FallsBackToCreatedAt_WhenSubmittedDateMissingOrInvalid(string? submittedDate)
    {
        var created = new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc);

        Assert.Equal(created, MongoApplicationRepository.DeriveAppliedAt(submittedDate, created));
    }
}
