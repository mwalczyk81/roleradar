using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

// Feature 008, phase 1: tests that need members that do not exist yet (EventDate, Warnings,
// TransitionWarningDto, TransitionWarnings). Kept in a second file so the behaviour tests in
// PostingRejectionSyncTests.cs can be shown failing for behavioural reasons first.
public sealed partial class PostingRejectionSyncTests
{
    // ---------- T002 ----------

    [Fact]
    public async Task EventDate_DefaultsToTodayUtc_FromTheClock()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("2026-10-05", Assert.Single(world.Events).EventDate);
    }

    [Fact]
    public async Task EventDate_CallerValue_IsUsed()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email") with { EventDate = "2026-09-01" });

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("2026-09-01", Assert.Single(world.Events).EventDate);
    }

    [Theory]
    [InlineData("2026-13-40")]
    [InlineData("09/01/2026")]
    [InlineData("2026-9-1")]
    public async Task EventDate_Malformed_IsValidationError_AndNothingIsWritten(string badDate)
    {
        var world = World.WithAppliedPosting();
        var versionBefore = world.Posting.VersionToken;

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email") with { EventDate = badDate });

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Equal(PostingDisposition.Applied, world.Posting.Disposition);
        Assert.Equal(versionBefore, world.Posting.VersionToken);
        Assert.Empty(world.Events);
        Assert.Empty(world.Audit);
        Assert.Empty(world.Calls);
    }

    [Fact]
    public async Task EventDate_IsNotValidatedOrUsed_WhenTheTransitionCreatesNoEvent()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(new TransitionPostingCommand(
            PostingId: world.Posting.Id,
            TargetDisposition: "closed",
            VersionToken: world.Posting.VersionToken,
            Reason: "Listing taken down",
            EventDate: "not-a-date",
            OwnerId: Owner,
            Actor: "mcp-stdio"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(world.Events);
    }

    // ---------- T003 / T006 (warnings) ----------

    [Fact]
    public async Task ClosedApplication_RejectingThePosting_WarnsApplicationClosed()
    {
        var world = World.WithAppliedPosting(appOutcome: ApplicationOutcome.Closed);

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var warning = Assert.Single(result.Value!.Warnings);
        Assert.Equal(TransitionWarnings.ApplicationClosed, warning.Code);
        var postingEntry = Assert.Single(world.Audit, a => a.Operation == AuditOperation.PostingTransition);
        Assert.Contains(TransitionWarnings.ApplicationClosed, Assert.IsAssignableFrom<IEnumerable<string>>(postingEntry.Details!.After!["warnings"]));
    }

    [Theory]
    [InlineData(ApplicationOutcome.Applied)]
    [InlineData(ApplicationOutcome.Rejected)]
    public async Task OtherRejections_HaveNoWarnings(ApplicationOutcome outcome)
    {
        var world = World.WithAppliedPosting(appOutcome: outcome);
        if (outcome == ApplicationOutcome.Rejected)
        {
            world.AddEvent(ApplicationEventType.Rejected, "2026-09-20", "web");
        }

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value!.Warnings);
    }

    [Fact]
    public async Task ReopenRejectedPosting_WithRejectedApplication_WarnsApplicationStillRejected()
    {
        var world = World.WithAppliedPosting(postingDisposition: PostingDisposition.Rejected, appOutcome: ApplicationOutcome.Rejected);
        world.AddEvent(ApplicationEventType.Rejected, "2026-09-20", "web");

        var result = await world.Handler().HandleAsync(world.ReopenCommand());

        Assert.True(result.IsSuccess, result.Error?.Message);
        var warning = Assert.Single(result.Value!.Warnings);
        Assert.Equal(TransitionWarnings.ApplicationStillRejected, warning.Code);
        var postingEntry = Assert.Single(world.Audit, a => a.Operation == AuditOperation.PostingTransition);
        Assert.Contains(TransitionWarnings.ApplicationStillRejected, Assert.IsAssignableFrom<IEnumerable<string>>(postingEntry.Details!.After!["warnings"]));
    }

    [Theory]
    [InlineData(false, ApplicationOutcome.Applied)]
    [InlineData(true, ApplicationOutcome.Applied)]
    [InlineData(true, ApplicationOutcome.Closed)]
    public async Task ReopenRejectedPosting_WithoutARejectedApplication_HasNoWarning(bool withApplication, ApplicationOutcome outcome)
    {
        var world = World.WithAppliedPosting(
            postingDisposition: PostingDisposition.Rejected,
            appOutcome: outcome,
            withApplication: withApplication);

        var result = await world.Handler().HandleAsync(world.ReopenCommand());

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Empty(result.Value!.Warnings);
    }

    // ---------- T007 (no warnings key when there are none, so audit output is unchanged for plain transitions) ----------

    [Fact]
    public async Task PostingAuditEntry_HasNoWarningsKey_WhenThereAreNone()
    {
        var world = World.WithAppliedPosting();

        var result = await world.Handler().HandleAsync(world.RejectCommand("Rejected by email"));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var postingEntry = Assert.Single(world.Audit, a => a.Operation == AuditOperation.PostingTransition);
        Assert.False(postingEntry.Details!.After!.ContainsKey("warnings"));
    }
}
