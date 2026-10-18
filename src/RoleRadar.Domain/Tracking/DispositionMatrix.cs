using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Domain.Tracking;

public static class DispositionMatrix
{
    public static bool CanTransition(PostingDisposition current, PostingDisposition target, ResurfacePolicy storedPolicy = ResurfacePolicy.Never)
    {
        // Target applied is NEVER reachable through disposition matrix / posting_transition
        if (target == PostingDisposition.Applied)
        {
            return false;
        }

        return (current, target) switch
        {
            (PostingDisposition.Lead, PostingDisposition.Scored) => true,
            (PostingDisposition.Lead, PostingDisposition.Unscored) => true,
            (PostingDisposition.Lead, PostingDisposition.ScreenedOut) => true,
            (PostingDisposition.Lead, PostingDisposition.Closed) => true,

            (PostingDisposition.Unscored, PostingDisposition.Scored) => true,
            (PostingDisposition.Unscored, PostingDisposition.ScreenedOut) => true,
            (PostingDisposition.Unscored, PostingDisposition.Closed) => true,

            (PostingDisposition.Scored, PostingDisposition.ScreenedOut) => true,
            (PostingDisposition.Scored, PostingDisposition.Closed) => true,

            (PostingDisposition.Applied, PostingDisposition.Rejected) => true,
            (PostingDisposition.Applied, PostingDisposition.Closed) => true,

            (PostingDisposition.ScreenedOut, PostingDisposition.Lead) => storedPolicy == ResurfacePolicy.IfReopened,
            (PostingDisposition.Closed, PostingDisposition.Lead) => storedPolicy == ResurfacePolicy.IfReopened,

            (PostingDisposition.Rejected, PostingDisposition.Lead) => true, // with reason

            _ => false
        };
    }
}
