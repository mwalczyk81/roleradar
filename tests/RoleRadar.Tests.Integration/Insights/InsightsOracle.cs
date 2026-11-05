namespace RoleRadar.Tests.Integration.Insights;

using RoleRadar.Domain.Tracking;

/// <summary>
/// An independent, in-memory statement of the spec's metric definitions over the <see cref="InsightsSeed"/> rows.
/// It deliberately shares no code with the repositories, the OData pipeline or the web lens module: it works on
/// day offsets and event types only, so a bug in the production derivation cannot also hide in the oracle.
/// </summary>
internal static class InsightsOracle
{
    public static readonly string[] LensKeys =
    [
        "leads", "reviewed", "applied-to", "screened-out", "expired",
        "applied", "responded", "interviewed", "offers", "offers-interviewed", "offers-without-interview",
        "mature", "mature-interviewed", "rejected", "rejected-before-interview", "rejected-after-interview",
        "ghost-eligible", "ghosted", "open"
    ];

    public sealed record ApplicationFacts(
        SeedPosting Seed,
        bool HasInterview,
        bool Responded,
        bool HasOffer,
        bool HasRejection,
        bool RejectedBeforeInterview,
        bool Open)
    {
        public int AgeDays => Seed.AppliedDaysBeforeAsOf!.Value;
    }

    public static ApplicationFacts Facts(SeedPosting seed)
    {
        if (!seed.HasApplication)
        {
            throw new ArgumentException("Posting has no application.", nameof(seed));
        }

        var events = seed.EventList;
        var interviews = events.Where(e => e.Type == ApplicationEventType.Interview).Select(e => e.DaysBeforeAsOf).ToList();
        var rejections = events.Where(e => e.Type == ApplicationEventType.Rejected).Select(e => e.DaysBeforeAsOf).ToList();

        // A larger "days before" is an earlier date. The earliest rejection decides before / after: it is "after"
        // when some interview is dated on or before it (same day counts as after, dates carry no time).
        var rejectedBefore = false;
        if (rejections.Count > 0)
        {
            var earliestRejection = rejections.Max();
            rejectedBefore = !interviews.Any(i => i >= earliestRejection);
        }

        var responseTypes = new[]
        {
            ApplicationEventType.Interview, ApplicationEventType.Offer, ApplicationEventType.OfferAccepted,
            ApplicationEventType.OfferDeclined, ApplicationEventType.Rejected
        };
        var terminalTypes = new[]
        {
            ApplicationEventType.Rejected, ApplicationEventType.OfferAccepted, ApplicationEventType.OfferDeclined,
            ApplicationEventType.Withdrawn
        };

        return new ApplicationFacts(
            seed,
            HasInterview: interviews.Count > 0,
            Responded: events.Any(e => responseTypes.Contains(e.Type)),
            HasOffer: events.Any(e => e.Type == ApplicationEventType.Offer),
            HasRejection: rejections.Count > 0,
            RejectedBeforeInterview: rejectedBefore,
            Open: !events.Any(e => terminalTypes.Contains(e.Type)));
    }

    public static IEnumerable<SeedPosting> ForOwner(string owner) => InsightsSeed.Postings.Where(p => p.Owner == owner);

    /// <summary>Whether a day offset falls inside the window (offset 0 is the as-of date itself).</summary>
    public static bool InWindow(int daysBeforeAsOf, bool last90Days) =>
        daysBeforeAsOf >= 0 && (!last90Days || daysBeforeAsOf <= 89);

    public static bool IsReviewed(SeedPosting p) =>
        (p.Disposition != PostingDisposition.Lead && p.Disposition != PostingDisposition.Unscored) || p.HasApplication;

    public static int Count(string owner, string lens, bool last90Days)
    {
        var postings = ForOwner(owner).ToList();
        var postingsInWindow = postings.Where(p => InWindow(p.FirstSeenDaysBeforeAsOf, last90Days)).ToList();
        var apps = postings.Where(p => p.HasApplication && InWindow(p.AppliedDaysBeforeAsOf!.Value, last90Days))
            .Select(Facts).ToList();

        return lens switch
        {
            "leads" => postingsInWindow.Count,
            "reviewed" => postingsInWindow.Count(IsReviewed),
            "applied-to" => postingsInWindow.Count(p => IsReviewed(p) && p.HasApplication),
            "screened-out" => postingsInWindow.Count(p => IsReviewed(p) && p.Disposition == PostingDisposition.ScreenedOut),
            "expired" => postingsInWindow.Count(p => p.Disposition == PostingDisposition.Closed && !p.HasApplication),
            "applied" => apps.Count,
            "responded" => apps.Count(a => a.Responded),
            "interviewed" => apps.Count(a => a.HasInterview),
            "offers" => apps.Count(a => a.HasOffer),
            "offers-interviewed" => apps.Count(a => a.HasOffer && a.HasInterview),
            "offers-without-interview" => apps.Count(a => a.HasOffer && !a.HasInterview),
            // At least 14 days old: a day offset of 14 or more.
            "mature" => apps.Count(a => a.AgeDays >= 14),
            "mature-interviewed" => apps.Count(a => a.AgeDays >= 14 && a.HasInterview),
            "rejected" => apps.Count(a => a.HasRejection),
            "rejected-before-interview" => apps.Count(a => a.HasRejection && a.RejectedBeforeInterview),
            "rejected-after-interview" => apps.Count(a => a.HasRejection && !a.RejectedBeforeInterview),
            // At least 30 days old.
            "ghost-eligible" => apps.Count(a => a.AgeDays >= 30),
            // Still applied (no terminal event), no response event, strictly older than 30 days.
            "ghosted" => apps.Count(a => a.Open && !a.Responded && a.AgeDays > 30),
            "open" => apps.Count(a => a.Open),
            _ => throw new ArgumentOutOfRangeException(nameof(lens), lens, "Unknown lens.")
        };
    }

    public static readonly string[][] Funnels =
    [
        ["leads", "reviewed", "applied-to"],
        ["applied", "responded", "interviewed", "offers-interviewed"]
    ];
}
