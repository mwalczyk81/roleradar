namespace RoleRadar.Tests.Integration.Insights;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

/// <summary>One application event, <c>DaysBeforeAsOf</c> days before the seed's as-of date.</summary>
internal sealed record SeedEvent(ApplicationEventType Type, int DaysBeforeAsOf);

/// <summary>A fictional posting, optionally with one application and its events.</summary>
internal sealed record SeedPosting(
    string Key,
    string Owner,
    string Company,
    PostingDisposition Disposition,
    int FirstSeenDaysBeforeAsOf,
    int? AppliedDaysBeforeAsOf = null,
    IReadOnlyList<SeedEvent>? Events = null)
{
    public bool HasApplication => AppliedDaysBeforeAsOf.HasValue;

    public IReadOnlyList<SeedEvent> EventList => Events ?? [];
}

/// <summary>
/// Deterministic, entirely fictional seed for the Insights tests. Dates are expressed as days before a fixed
/// as-of date, so the same shape can be built relative to a fixed date (snapshots) or to today (tests that
/// compare with the live <c>ageInDays</c>). Rows are written through the real repositories, so every derived
/// flag comes from the repository choke point.
/// </summary>
internal static class InsightsSeed
{
    public const string OwnerA = "insights-owner-aaa";
    public const string OwnerB = "insights-owner-bbb";
    public const string OwnerC = "insights-owner-ccc"; // owns nothing: the zero-denominator owner
    public const string OwnerD = "insights-owner-ddd"; // fewer than 10 of everything: the small-sample owner

    /// <summary>Fixed fictional as-of date used by the committed snapshots.</summary>
    public static readonly DateOnly FixedAsOf = new(2026, 9, 30);

    public static readonly string[] AllOwners = [OwnerA, OwnerB, OwnerC, OwnerD];

    private static readonly string[] Companies =
    [
        "Northwind Examples", "Contoso Samples", "Fabrikam Mocks", "Tailspin Demo", "Wingtip Fixtures", "Litware Placeholders"
    ];

    // string.GetHashCode is randomized per process; the seed must be identical on every run.
    private static string CompanyFor(string key) => Companies[key.Sum(c => (int)c) % Companies.Length];

    private static SeedEvent E(ApplicationEventType type, int daysBefore) => new(type, daysBefore);

    private static SeedPosting P(string key, PostingDisposition d, int firstSeen) =>
        new(key, OwnerA, CompanyFor(key), d, firstSeen);

    private static SeedPosting A(
        string key,
        PostingDisposition d,
        int firstSeen,
        int applied,
        params SeedEvent[] events) =>
        new(key, OwnerA, CompanyFor(key), d, firstSeen, applied, events);

    /// <summary>Owner A has the full matrix; B must never leak into A; C is empty; D is tiny.</summary>
    public static IReadOnlyList<SeedPosting> Postings { get; } = BuildPostings();

    private static List<SeedPosting> BuildPostings()
    {
        const PostingDisposition Applied = PostingDisposition.Applied;
        const PostingDisposition Rejected = PostingDisposition.Rejected;
        const PostingDisposition Closed = PostingDisposition.Closed;

        var list = new List<SeedPosting>
        {
            // ---- postings with an application (owner A) ----
            // Ages around the 14 and 30 day cutoffs, no events.
            A("age-00-edge", Applied, 12, 0),
            A("age-05", Applied, 12, 5),
            A("age-13-interview", Applied, 20, 13, E(ApplicationEventType.Interview, 8)),
            A("age-14-interview", Applied, 20, 14, E(ApplicationEventType.Interview, 8)),
            A("age-29-silent", Applied, 40, 29),
            A("age-30-silent", Applied, 40, 30),
            A("age-31-silent", Applied, 40, 31),
            A("age-44-follow-up-only", Applied, 60, 44, E(ApplicationEventType.FollowUp, 40)),
            A("age-50-silent", Applied, 60, 50),
            // Window edges (90 day window is asOf-89 .. asOf).
            A("window-first-instant", Applied, 100, 89),
            A("window-just-outside", Applied, 100, 90),
            A("window-future-dated", Applied, 3, -1),
            // First seen before the window, applied inside it.
            A("seen-before-window", Applied, 150, 20),
            // Rejections.
            A("rej-no-interview", Rejected, 70, 60, E(ApplicationEventType.Rejected, 55)),
            A("rej-after-interview", Rejected, 70, 60, E(ApplicationEventType.Interview, 50), E(ApplicationEventType.Rejected, 40)),
            A("rej-same-day-as-interview", Rejected, 70, 60, E(ApplicationEventType.Interview, 45), E(ApplicationEventType.Rejected, 45)),
            A("rej-then-interview-dated-later", Rejected, 80, 70, E(ApplicationEventType.Rejected, 65), E(ApplicationEventType.Interview, 60)),
            A("rej-twice-earliest-before", Rejected, 70, 50, E(ApplicationEventType.Rejected, 45), E(ApplicationEventType.Interview, 43), E(ApplicationEventType.Rejected, 40)),
            A("rej-recent", Rejected, 30, 20, E(ApplicationEventType.Rejected, 10)),
            // Offers.
            A("offer-after-interview", Applied, 60, 50, E(ApplicationEventType.Interview, 40), E(ApplicationEventType.Offer, 30)),
            A("offer-no-interview", Applied, 60, 50, E(ApplicationEventType.Offer, 30)),
            A("offer-accepted", Applied, 90, 80, E(ApplicationEventType.Interview, 70), E(ApplicationEventType.Offer, 60), E(ApplicationEventType.OfferAccepted, 55)),
            A("offer-declined", Applied, 90, 80, E(ApplicationEventType.Offer, 60), E(ApplicationEventType.OfferDeclined, 50)),
            // Candidate-side terminal event.
            A("withdrawn", Applied, 60, 45, E(ApplicationEventType.FollowUp, 40), E(ApplicationEventType.Withdrawn, 35)),
            // Interviews at various stages.
            A("interview-mid", Applied, 50, 40, E(ApplicationEventType.Interview, 30)),
            A("interview-two-rounds", Applied, 50, 35, E(ApplicationEventType.Interview, 28), E(ApplicationEventType.Interview, 20)),
            A("interview-then-silent-old", Applied, 120, 100, E(ApplicationEventType.Interview, 90)),
            // Applied to a posting whose disposition later became closed (still has an application).
            A("closed-after-apply", Closed, 40, 30),

            // ---- postings without an application (owner A) ----
            P("lead-5", PostingDisposition.Lead, 5),
            P("lead-20", PostingDisposition.Lead, 20),
            P("lead-50", PostingDisposition.Lead, 50),
            P("lead-100", PostingDisposition.Lead, 100),
            P("lead-150", PostingDisposition.Lead, 150),
            P("unscored-3", PostingDisposition.Unscored, 3),
            P("unscored-60", PostingDisposition.Unscored, 60),
            P("unscored-95", PostingDisposition.Unscored, 95),
            P("scored-10", PostingDisposition.Scored, 10),
            P("scored-30", PostingDisposition.Scored, 30),
            P("scored-70", PostingDisposition.Scored, 70),
            P("scored-120", PostingDisposition.Scored, 120),
            P("screened-out-8", PostingDisposition.ScreenedOut, 8),
            P("screened-out-40", PostingDisposition.ScreenedOut, 40),
            P("screened-out-89", PostingDisposition.ScreenedOut, 89),
            P("screened-out-130", PostingDisposition.ScreenedOut, 130),
            P("closed-15", PostingDisposition.Closed, 15),
            P("closed-65", PostingDisposition.Closed, 65),
            P("closed-110", PostingDisposition.Closed, 110),
            P("posting-rejected-25", PostingDisposition.Rejected, 25),
            P("window-first-seen-edge-89", PostingDisposition.Scored, 89),
            P("window-first-seen-edge-90", PostingDisposition.Scored, 90),
            P("window-first-seen-future", PostingDisposition.Lead, -1),

            // ---- owner B: must never appear in owner A's counts ----
            new("b-lead", OwnerB, "Owner B Fixture Co", PostingDisposition.Lead, 10),
            new("b-scored", OwnerB, "Owner B Fixture Co", PostingDisposition.Scored, 20),
            new("b-screened", OwnerB, "Owner B Fixture Co", PostingDisposition.ScreenedOut, 30),
            new("b-applied-offer", OwnerB, "Owner B Fixture Co", Applied, 40, 30,
                [E(ApplicationEventType.Interview, 25), E(ApplicationEventType.Offer, 20)]),
            new("b-applied-rejected", OwnerB, "Owner B Fixture Co", Rejected, 40, 35, [E(ApplicationEventType.Rejected, 30)]),
            new("b-applied-silent", OwnerB, "Owner B Fixture Co", Applied, 80, 70),

            // ---- owner D: small sample (fewer than 10 of everything) ----
            new("d-lead", OwnerD, "Small Sample Examples", PostingDisposition.Lead, 5),
            new("d-scored", OwnerD, "Small Sample Examples", PostingDisposition.Scored, 15),
            new("d-applied-1", OwnerD, "Small Sample Examples", Applied, 30, 20, [E(ApplicationEventType.Interview, 12)]),
            new("d-applied-2", OwnerD, "Small Sample Examples", Applied, 50, 40)
        };

        return list;
    }

    /// <summary>The instant the application row and its events use for a day offset (noon UTC, so no edge flips).</summary>
    public static DateTimeOffset Instant(DateOnly asOf, int daysBefore) =>
        new(asOf.AddDays(-daysBefore).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), TimeSpan.Zero);

    public static string DateText(DateOnly asOf, int daysBefore) =>
        asOf.AddDays(-daysBefore).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static Guid PostingId(string key) => DeterministicGuid("posting:" + key);

    public static Guid ApplicationId(string key) => DeterministicGuid("application:" + key);

    private static Guid DeterministicGuid(string text)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    /// <summary>
    /// Writes the seed through the real repositories (company, posting, application, events), exactly in the order
    /// the import and the handlers do: application row, "applied" event, further events, outcome via the domain.
    /// </summary>
    public static async Task WriteAsync(IMongoClient client, IMongoDatabase database, DateOnly asOf)
    {
        var companies = new MongoCompanyRepository(client, database);
        var postings = new MongoPostingRepository(client, database);
        var applications = new MongoApplicationRepository(client, database);

        var companyIds = new Dictionary<(string Owner, string Name), Guid>();
        foreach (var seed in Postings)
        {
            var key = (seed.Owner, seed.Company);
            if (companyIds.ContainsKey(key))
            {
                continue;
            }

            var company = Company.Create(seed.Owner, seed.Company, now: Instant(asOf, 400));
            companyIds[key] = company.Id;
            Ok(await companies.UpsertCompanyAsync(company));
        }

        foreach (var seed in Postings)
        {
            var firstSeen = Instant(asOf, seed.FirstSeenDaysBeforeAsOf);
            var posting = Posting.Create(
                ownerId: seed.Owner,
                companyId: companyIds[(seed.Owner, seed.Company)],
                title: "Fictional " + seed.Key,
                disposition: seed.Disposition,
                score: seed.Disposition == PostingDisposition.Unscored ? null : 7.5m,
                id: PostingId(seed.Key),
                now: firstSeen);
            Ok(await postings.UpsertPostingAsync(posting));

            if (!seed.HasApplication)
            {
                continue;
            }

            var appliedAt = Instant(asOf, seed.AppliedDaysBeforeAsOf!.Value);
            var application = ApplicationEntity.Create(
                ownerId: seed.Owner,
                postingId: posting.Id,
                submittedDate: DateText(asOf, seed.AppliedDaysBeforeAsOf.Value),
                resumeVariant: ResumeVariant.Tailored,
                id: ApplicationId(seed.Key),
                now: appliedAt);
            Ok(await applications.UpsertApplicationAsync(application));

            Ok(await applications.AppendEventAsync(ApplicationEvent.Create(
                seed.Owner, application.Id, DateText(asOf, seed.AppliedDaysBeforeAsOf.Value), ApplicationEventType.Applied, now: appliedAt)));

            foreach (var ev in seed.EventList)
            {
                var when = Instant(asOf, ev.DaysBeforeAsOf);
                Ok(await applications.AppendEventAsync(ApplicationEvent.Create(
                    seed.Owner, application.Id, DateText(asOf, ev.DaysBeforeAsOf), ev.Type, now: when)));
                application.ApplyEvent(ev.Type, null, when);
            }

            // Persist the outcome the events produced; the replace recomputes every milestone from the events.
            Ok(await applications.UpsertApplicationAsync(application));
        }
    }

    /// <summary>
    /// Turns the written data into pre-007 shape: removes the five fields this feature added, exactly as a
    /// database written before the feature would lack them. Used to exercise <c>reconcile-read-models</c>.
    /// </summary>
    public static async Task StripDerivedFieldsAsync(IMongoDatabase database)
    {
        await database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).UpdateManyAsync(
            FilterDefinition<BsonDocument>.Empty,
            Builders<BsonDocument>.Update.Unset("has_application"));

        await database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).UpdateManyAsync(
            FilterDefinition<BsonDocument>.Empty,
            Builders<BsonDocument>.Update.Unset("has_offer").Unset("has_rejection").Unset("rejected_before_interview"));
    }

    private static void Ok<T>(RoleRadar.Application.Abstractions.Result<T> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Seed write failed: " + result.Error?.Message);
        }
    }
}
