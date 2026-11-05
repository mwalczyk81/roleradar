using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

// Feature 008, T036: appending a note event never reaches the posting. Uses the same in-memory world.
public sealed partial class PostingRejectionSyncTests
{
    [Theory]
    [InlineData(PostingDisposition.Applied, ApplicationOutcome.Applied)]
    [InlineData(PostingDisposition.Rejected, ApplicationOutcome.Rejected)]
    public async Task AppendingANoteEvent_DoesNotChangeThePostingOrTheApplicationOutcome(PostingDisposition posting, ApplicationOutcome outcome)
    {
        var world = World.WithAppliedPosting(postingDisposition: posting, appOutcome: outcome);
        var decidedAt = world.Posting.DecidedAt;
        var version = world.Posting.VersionToken;
        var updatedAt = world.Posting.UpdatedAt;
        var appVersion = world.App.VersionToken;

        var handler = new RecordApplicationCommandHandler(world.Context, new FixedClock(FixedNow));
        var result = await handler.HandleAsync(new RecordApplicationCommand(
            PostingId: world.Posting.Id,
            Action: "append_event",
            Event: new ApplicationEventInputDto("note", "2026-10-05", "web", "A remark about the call"),
            OwnerId: Owner));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var ev = Assert.Single(world.Events);
        Assert.Equal(ApplicationEventType.Note, ev.EventType);
        Assert.Equal(posting, world.Posting.Disposition);
        Assert.Equal(decidedAt, world.Posting.DecidedAt);
        Assert.Equal(version, world.Posting.VersionToken);
        Assert.Equal(updatedAt, world.Posting.UpdatedAt);
        Assert.Equal(outcome, world.App.Outcome);
        Assert.Equal(appVersion, world.App.VersionToken);
    }
}
