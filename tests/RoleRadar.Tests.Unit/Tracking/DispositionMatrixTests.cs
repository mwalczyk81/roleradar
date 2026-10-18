using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class DispositionMatrixTests
{
    [Theory]
    // Lead transitions
    [InlineData(PostingDisposition.Lead, PostingDisposition.Scored, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Lead, PostingDisposition.Unscored, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Lead, PostingDisposition.ScreenedOut, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Lead, PostingDisposition.Closed, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Lead, PostingDisposition.Applied, ResurfacePolicy.Never, false)] // applied forbidden via transition
    [InlineData(PostingDisposition.Lead, PostingDisposition.Rejected, ResurfacePolicy.Never, false)]

    // Unscored transitions
    [InlineData(PostingDisposition.Unscored, PostingDisposition.Scored, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Unscored, PostingDisposition.ScreenedOut, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Unscored, PostingDisposition.Closed, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Unscored, PostingDisposition.Applied, ResurfacePolicy.Never, false)] // applied forbidden via transition

    // Scored transitions
    [InlineData(PostingDisposition.Scored, PostingDisposition.ScreenedOut, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Scored, PostingDisposition.Closed, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Scored, PostingDisposition.Applied, ResurfacePolicy.Never, false)] // applied forbidden via transition
    [InlineData(PostingDisposition.Scored, PostingDisposition.Lead, ResurfacePolicy.Never, false)]

    // Applied transitions
    [InlineData(PostingDisposition.Applied, PostingDisposition.Rejected, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Applied, PostingDisposition.Closed, ResurfacePolicy.Never, true)]
    [InlineData(PostingDisposition.Applied, PostingDisposition.Lead, ResurfacePolicy.Never, false)]
    [InlineData(PostingDisposition.Applied, PostingDisposition.Scored, ResurfacePolicy.Never, false)]

    // ScreenedOut transitions (stored policy governs return to lead)
    [InlineData(PostingDisposition.ScreenedOut, PostingDisposition.Lead, ResurfacePolicy.IfReopened, true)]
    [InlineData(PostingDisposition.ScreenedOut, PostingDisposition.Lead, ResurfacePolicy.Never, false)]
    [InlineData(PostingDisposition.ScreenedOut, PostingDisposition.Scored, ResurfacePolicy.IfReopened, false)]
    [InlineData(PostingDisposition.ScreenedOut, PostingDisposition.Applied, ResurfacePolicy.IfReopened, false)] // only via app override

    // Closed transitions
    [InlineData(PostingDisposition.Closed, PostingDisposition.Lead, ResurfacePolicy.IfReopened, true)]
    [InlineData(PostingDisposition.Closed, PostingDisposition.Lead, ResurfacePolicy.Never, false)]
    [InlineData(PostingDisposition.Closed, PostingDisposition.Applied, ResurfacePolicy.IfReopened, false)] // only via app override

    // Rejected transitions
    [InlineData(PostingDisposition.Rejected, PostingDisposition.Lead, ResurfacePolicy.Never, true)] // reopen with override reason
    [InlineData(PostingDisposition.Rejected, PostingDisposition.Applied, ResurfacePolicy.Never, false)]
    public void DispositionMatrix_CanTransition_ValidatesAllowedAndForbiddenTransitions(
        PostingDisposition current,
        PostingDisposition target,
        ResurfacePolicy storedPolicy,
        bool expected)
    {
        var actual = DispositionMatrix.CanTransition(current, target, storedPolicy);
        Assert.Equal(expected, actual);
    }
}
