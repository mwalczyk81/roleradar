using RoleRadar.Domain.Tracking;

namespace RoleRadar.Cli.Demo;

/// <summary>A fictional company. Names are owner-reviewed (spec 012).</summary>
public sealed record DemoCompany(string Name, OwnershipType Ownership, CompanyStatus Status, string Headquarters, string Description)
{
    /// <summary>Category tags shown on the company page.</summary>
    public IReadOnlyList<string> CategoryTags { get; init; } = [];

    /// <summary>One fictional career page, stored the way the company editor stores it (the source URL).</summary>
    public string? CareerSourceUrl { get; init; }
}

/// <summary>An application event, <c>DaysBeforeAsOf</c> days before the seed's as-of date.</summary>
public sealed record DemoEvent(ApplicationEventType Type, int DaysBeforeAsOf);

/// <summary>
/// An opportunity signal observed <c>DaysBeforeAsOf</c> days before the as-of date; a deadline signal also carries its
/// deadline, <c>DeadlineDaysBeforeAsOf</c> days before the as-of date.
/// </summary>
public sealed record DemoSignal(SignalType Type, string Evidence, int DaysBeforeAsOf, int? DeadlineDaysBeforeAsOf = null);

/// <summary>A fictional posting, optionally with one application and its timeline.</summary>
public sealed record DemoPosting(
    string Slug,
    string Company,
    string Title,
    PostingDisposition Disposition,
    int FirstSeenDaysBeforeAsOf,
    RemoteType Remote,
    decimal? Score,
    string? Reason,
    int? AppliedDaysBeforeAsOf,
    IReadOnlyList<DemoEvent> Events)
{
    public string Url => "https://jobs.example.com/" + Slug;

    /// <summary>Posted pay; the detail page shows the text, the range is stored beside it.</summary>
    public Compensation? Compensation { get; init; }

    /// <summary>Opportunity signals, written only when the posting has none yet.</summary>
    public IReadOnlyList<DemoSignal> Signals { get; init; } = [];

    /// <summary>Title of the demo note that is this posting's assessment note (its screening note).</summary>
    public string? AssessmentNoteTitle { get; init; }
}

/// <summary>A fictional Markdown note. Only elements the note renderer allows (ADR 0032): no raw HTML, no images.</summary>
public sealed record DemoNote(string Title, string Area, string Kind, int ProvenanceDaysBeforeAsOf, IReadOnlyList<string> Tags, string Body);

/// <summary>A fictional search run, <c>DaysBeforeAsOf</c> days before the seed's as-of date.</summary>
public sealed record DemoSearchRun(int DaysBeforeAsOf, int NewPostings);

/// <summary>
/// The fictional demo dataset: pure content, relative to an as-of date, with no database access. Every name is invented
/// and every URL uses example.com. Shaped so the Insights funnels, lenses and trends look real (data-model.md).
/// </summary>
public static class DemoDataset
{
    /// <summary>The company the screenshots open (company page, posting, application).</summary>
    public const string HeroCompany = "Harborlight Systems";

    /// <summary>The posting the screenshots open: Harborlight's Head of Software Development, applied to an accepted offer.</summary>
    public const string HeroPostingSlug = "harborlight-head-software";

    /// <summary>The hero posting's assessment note, one of the six notes.</summary>
    public const string HeroAssessmentNoteTitle = "Strong fit: scope and comp match, confirm the on-call load";

    public static IReadOnlyList<DemoCompany> Companies { get; } =
    [
        new(HeroCompany, OwnershipType.Private, CompanyStatus.Target, "Portland, OR", "Maritime logistics software for small ports.")
        {
            CategoryTags = ["logistics", "maritime", "b2b-saas", "remote-friendly"],
            CareerSourceUrl = "https://jobs.example.com/harborlight-systems"
        },
        new("Quillfeather Labs", OwnershipType.VcBacked, CompanyStatus.Watch, "Austin, TX", "Writing assistants for technical teams."),
        new("Brindle & Moss Analytics", OwnershipType.Private, CompanyStatus.Neutral, "Chicago, IL", "Retail demand forecasting."),
        new("Copperfield Robotics", OwnershipType.PeBacked, CompanyStatus.Target, "Pittsburgh, PA", "Warehouse picking robots."),
        new("Tamarack Data Works", OwnershipType.Public, CompanyStatus.Neutral, "Minneapolis, MN", "Data pipelines for agriculture."),
        new("Velvet Anvil Studios", OwnershipType.Private, CompanyStatus.Avoid, "Seattle, WA", "Narrative games studio."),
        new("Larkspur Freight Software", OwnershipType.Mutual, CompanyStatus.Watch, "Columbus, OH", "Freight scheduling for regional carriers."),
        new("Ostrander Cloud", OwnershipType.Public, CompanyStatus.Target, "Denver, CO", "Managed cloud hosting."),
        new("Fernhollow Payments", OwnershipType.VcBacked, CompanyStatus.Neutral, "Atlanta, GA", "Payment orchestration for marketplaces."),
        new("Saltmarsh Insurance Tech", OwnershipType.Subsidiary, CompanyStatus.Watch, "Hartford, CT", "Claims automation."),
        new("Kestrel Ridge Energy", OwnershipType.Public, CompanyStatus.Neutral, "Houston, TX", "Grid monitoring software."),
        new("Mossgate Learning", OwnershipType.Nonprofit, CompanyStatus.Target, "Boston, MA", "Open learning platform."),
        new("Thistledown Security", OwnershipType.PeBacked, CompanyStatus.Avoid, "Raleigh, NC", "Endpoint security."),
        new("Wrenfield Marketplace", OwnershipType.VcBacked, CompanyStatus.Neutral, "Remote", "Local services marketplace.")
    ];

    public static IReadOnlyList<DemoPosting> Postings { get; } = BuildPostings();

    public static IReadOnlyList<DemoSearchRun> SearchRuns { get; } =
    [
        new(96, 7), new(82, 4), new(68, 0), new(54, 12), new(41, 3), new(27, 14), new(15, 9), new(3, 6)
    ];

    public static IReadOnlyList<DemoNote> Notes { get; } = BuildNotes();

    private static DemoEvent E(ApplicationEventType type, int daysBefore) => new(type, daysBefore);

    private static Compensation Pay(decimal min, decimal max, string text) => new(min, max, "USD", text);

    private static DemoPosting Lead(string slug, string company, string title, int seen, RemoteType remote = RemoteType.Remote) =>
        new(slug, company, title, PostingDisposition.Lead, seen, remote, null, null, null, []);

    private static DemoPosting Unscored(string slug, string company, string title, int seen) =>
        new(slug, company, title, PostingDisposition.Unscored, seen, RemoteType.Unknown, null, null, null, []);

    private static DemoPosting Scored(string slug, string company, string title, int seen, decimal score, RemoteType remote = RemoteType.Hybrid) =>
        new(slug, company, title, PostingDisposition.Scored, seen, remote, score, null, null, []);

    private static DemoPosting Screened(string slug, string company, string title, int seen, string reason) =>
        new(slug, company, title, PostingDisposition.ScreenedOut, seen, RemoteType.Onsite, 4.5m, reason, null, []);

    private static DemoPosting Closed(string slug, string company, string title, int seen) =>
        new(slug, company, title, PostingDisposition.Closed, seen, RemoteType.Remote, 6.5m, "Posting removed by the employer", null, []);

    private static DemoPosting App(
        string slug, string company, string title, PostingDisposition disposition, int seen, int applied, decimal score, params DemoEvent[] events) =>
        new(slug, company, title, disposition, seen, RemoteType.Hybrid, score,
            disposition == PostingDisposition.Closed ? "Posting removed by the employer" : null, applied, events);

    private static List<DemoPosting> BuildPostings()
    {
        const PostingDisposition Applied = PostingDisposition.Applied;
        const PostingDisposition Rejected = PostingDisposition.Rejected;

        return
        [
            // ---- leads (8) ----
            Lead("harborlight-em-platform", "Harborlight Systems", "Engineering Manager, Platform", 4),
            Lead("quillfeather-director-engineering", "Quillfeather Labs", "Director of Engineering", 9),
            Lead("brindle-director-devex", "Brindle & Moss Analytics", "Director, Developer Experience", 14),
            Lead("copperfield-head-software", "Copperfield Robotics", "Head of Software Development", 21),
            Lead("tamarack-director-data", "Tamarack Data Works", "Director, Data Engineering", 33),
            Lead("ostrander-director-platform", "Ostrander Cloud", "Director of Platform Engineering", 47),
            Lead("fernhollow-em-payments", "Fernhollow Payments", "Engineering Manager, Payments", 66),
            Lead("mossgate-director-engineering", "Mossgate Learning", "Director of Engineering", 112),

            // ---- unscored (3) ----
            Unscored("kestrel-senior-em", "Kestrel Ridge Energy", "Senior Engineering Manager", 3),
            Unscored("wrenfield-group-em", "Wrenfield Marketplace", "Group Engineering Manager", 28),
            Unscored("larkspur-vp-engineering", "Larkspur Freight Software", "VP of Engineering", 75),

            // ---- scored (7) ----
            Scored("harborlight-director-engineering", "Harborlight Systems", "Director of Engineering", 12, 8.2m) with
                { Compensation = Pay(195_000m, 225_000m, "$195,000 to $225,000 base, 10% target bonus") },
            Scored("saltmarsh-head-software", "Saltmarsh Insurance Tech", "Head of Software Development", 26, 7.1m) with
                { Compensation = Pay(185_000m, 210_000m, "$185,000 to $210,000 base") },
            Scored("thistledown-principal-architecture", "Thistledown Security", "Principal Engineer, Architecture", 38, 6.8m),
            Scored("ostrander-vp-engineering", "Ostrander Cloud", "VP of Engineering", 55, 7.9m) with
                { Compensation = Pay(230_000m, 270_000m, "$230,000 to $270,000 base, RSUs over four years") },
            Scored("tamarack-senior-director", "Tamarack Data Works", "Senior Director of Engineering", 82, 7.4m),
            Scored("mossgate-director-devex", "Mossgate Learning", "Director, Developer Experience", 101, 8.0m, RemoteType.Remote) with
                { Compensation = Pay(170_000m, 195_000m, "$170,000 to $195,000 base, nonprofit benefits package") },
            Scored("wrenfield-em-platform", "Wrenfield Marketplace", "Engineering Manager, Platform", 138, 6.6m, RemoteType.Remote),

            // ---- screened out (7) ----
            Screened("velvet-vp-engineering", "Velvet Anvil Studios", "VP of Engineering", 8, "Studio layoffs reported"),
            Screened("thistledown-director-engineering", "Thistledown Security", "Director of Engineering", 19, "Onsite five days a week, below the compensation floor"),
            Screened("kestrel-head-operations", "Kestrel Ridge Energy", "Head of Engineering Operations", 31, "Scope is operations, not engineering"),
            Screened("saltmarsh-staff-reliability", "Saltmarsh Insurance Tech", "Staff Engineer, Reliability", 49, "Staff track, not management"),
            Screened("velvet-em-mobile", "Velvet Anvil Studios", "Engineering Manager, Mobile", 74, "Studio layoffs reported"),
            Screened("larkspur-director-platform", "Larkspur Freight Software", "Director of Platform Engineering", 96, "Relocation required"),
            Screened("brindle-staff-infrastructure", "Brindle & Moss Analytics", "Staff Software Engineer, Infrastructure", 128, "Staff track, not management"),

            // ---- closed without an application (3) ----
            Closed("quillfeather-senior-em", "Quillfeather Labs", "Senior Engineering Manager", 16),
            Closed("copperfield-director-engineering", "Copperfield Robotics", "Director of Engineering", 59),
            Closed("fernhollow-director-platform", "Fernhollow Payments", "Director of Platform Engineering", 120),

            // ---- applied (12) ----
            App(HeroPostingSlug, HeroCompany, "Head of Software Development", Applied, 112, 100, 8.6m,
                E(ApplicationEventType.Interview, 90), E(ApplicationEventType.Interview, 80), E(ApplicationEventType.Offer, 70), E(ApplicationEventType.OfferAccepted, 65)) with
            {
                Compensation = Pay(215_000m, 245_000m, "$215,000 to $245,000 base, 15% target bonus, equity refreshed yearly"),
                AssessmentNoteTitle = HeroAssessmentNoteTitle,
                Signals =
                [
                    new(SignalType.ParallelPosting, "Same role listed on the Harborlight careers page and on two job boards with matching text.", 110),
                    new(SignalType.Repost, "Reposted after the first round closed without a hire; the posted range rose by $10,000.", 106),
                    new(SignalType.Deadline, "The recruiter confirmed that applications close at the end of the month.", 104, DeadlineDaysBeforeAsOf: 98)
                ]
            },
            App("copperfield-group-em", "Copperfield Robotics", "Group Engineering Manager", Applied, 95, 85, 8.1m,
                E(ApplicationEventType.Interview, 75), E(ApplicationEventType.Offer, 60), E(ApplicationEventType.OfferDeclined, 55)),
            App("ostrander-director-data", "Ostrander Cloud", "Director, Data Engineering", Applied, 80, 70, 7.8m,
                E(ApplicationEventType.Interview, 60), E(ApplicationEventType.Interview, 52), E(ApplicationEventType.Note, 50)),
            App("tamarack-em-platform", "Tamarack Data Works", "Engineering Manager, Platform", Applied, 70, 62, 7.2m,
                E(ApplicationEventType.FollowUp, 50), E(ApplicationEventType.Interview, 44), E(ApplicationEventType.Note, 40)),
            App("mossgate-head-software", "Mossgate Learning", "Head of Software Development", Applied, 55, 48, 8.3m,
                E(ApplicationEventType.Interview, 38)),
            App("saltmarsh-senior-em", "Saltmarsh Insurance Tech", "Senior Engineering Manager", Applied, 46, 40, 7.0m,
                E(ApplicationEventType.FollowUp, 30)),
            App("wrenfield-director-engineering", "Wrenfield Marketplace", "Director of Engineering", Applied, 40, 35, 7.5m,
                E(ApplicationEventType.FollowUp, 31), E(ApplicationEventType.Withdrawn, 28)),
            App("fernhollow-vp-engineering", "Fernhollow Payments", "VP of Engineering", Applied, 130, 120, 7.6m),
            App("kestrel-director-engineering", "Kestrel Ridge Energy", "Director of Engineering", Applied, 83, 75, 7.3m),
            App("larkspur-senior-em", "Larkspur Freight Software", "Senior Engineering Manager", Applied, 60, 55, 6.9m,
                E(ApplicationEventType.Note, 52)),
            App("brindle-director-engineering", "Brindle & Moss Analytics", "Director of Engineering", Applied, 20, 12, 7.7m),
            App("thistledown-em-payments", "Thistledown Security", "Engineering Manager, Payments", Applied, 10, 6, 6.7m,
                E(ApplicationEventType.FollowUp, 2)),

            // ---- rejected (6) ----
            App("velvet-director-engineering", "Velvet Anvil Studios", "Director of Engineering", Rejected, 105, 95, 6.4m,
                E(ApplicationEventType.Rejected, 88)),
            App("quillfeather-head-software", "Quillfeather Labs", "Head of Software Development", Rejected, 90, 80, 7.0m,
                E(ApplicationEventType.Interview, 70), E(ApplicationEventType.Rejected, 62)),
            App("kestrel-senior-director", "Kestrel Ridge Energy", "Senior Director of Engineering", Rejected, 76, 66, 6.8m,
                E(ApplicationEventType.Rejected, 58), E(ApplicationEventType.Note, 56)),
            App("copperfield-vp-engineering", "Copperfield Robotics", "VP of Engineering", Rejected, 60, 50, 7.9m,
                E(ApplicationEventType.Interview, 42), E(ApplicationEventType.Interview, 36), E(ApplicationEventType.Rejected, 30)),
            App("ostrander-em-payments", "Ostrander Cloud", "Engineering Manager, Payments", Rejected, 38, 30, 7.1m,
                E(ApplicationEventType.Rejected, 21)),
            App("tamarack-director-platform", "Tamarack Data Works", "Director of Platform Engineering", Rejected, 24, 18, 7.0m,
                E(ApplicationEventType.Rejected, 9)),

            // ---- closed after an application (2) ----
            App("saltmarsh-director-engineering", "Saltmarsh Insurance Tech", "Director of Engineering", PostingDisposition.Closed, 70, 58, 7.2m,
                E(ApplicationEventType.FollowUp, 45)),
            App("wrenfield-senior-em", "Wrenfield Marketplace", "Senior Engineering Manager", PostingDisposition.Closed, 34, 26, 6.6m)
        ];
    }

    private static List<DemoNote> BuildNotes() =>
    [
        new("Interview prep: platform team", "interview-prep", "stated", 40, ["interview", "platform"],
            """
            ## What to ask

            - How is on-call shared between the platform team and product teams?
            - Which service has the worst deploy time, and who owns fixing it?
            - What does a good first 90 days look like for a new director?

            ## Checklist

            - [x] Read the engineering blog posts
            - [x] Draft three stories about migrations
            - [ ] Prepare a one-page view of the team structure
            - [ ] Send thank-you notes within a day

            > Keep answers to two minutes and end with a question.
            """),
        new("Compensation research method", "research", "researched", 55, ["compensation", "method"],
            """
            ## How I compare offers

            I weight cash, equity and growth differently for each stage of company.

            | Factor | Weight | Notes |
            | --- | --- | --- |
            | Base salary | 40% | Compare against the posted range |
            | Equity | 20% | Discount private equity heavily |
            | Growth | 25% | Scope of the role in two years |
            | Team | 15% | Who I would report to |

            ```text
            score = 0.40 * base + 0.20 * equity + 0.25 * growth + 0.15 * team
            ```

            See [the method write-up](https://example.com/notes/compensation-method) for the long version.
            """),
        new("Weekly search retro", "search-method", "stated", 8, ["retro", "weekly"],
            """
            ## This week

            - Reviewed **14** new leads and moved **3** to scored.
            - Two applications are past 30 days with no reply.
            - One interview loop finished; waiting on feedback.

            ## Next week

            1. Follow up on the two quiet applications.
            2. Re-score the leads that mention remote-first.
            3. Add two more companies to the watch list.

            More context lives in [the weekly template](https://example.com/notes/weekly-retro).
            """),
        new("Harborlight Systems research", "company", "researched", 100, ["company", "target"],
            """
            ## Snapshot

            Harborlight builds scheduling software for small ports. The team is about *sixty* engineers.

            ## Signals

            - Stable leadership for the last four years.
            - Public roadmap, and a remote-friendly engineering handbook.
            - Hiring a director for the platform group.

            ## Open questions

            - How are priorities set between customer requests and platform work?
            - What is the on-call load for the platform team?
            """),
        new(HeroAssessmentNoteTitle, "assessment", "inferred", 104, ["assessment", "harborlight"],
            """
            ## Verdict

            Strong fit, **8.6 out of 10**. Apply this week with the tailored resume.

            ## How it scores

            | Criterion | Score | Evidence |
            | --- | --- | --- |
            | Scope | 9 | Owns platform and product engineering, about sixty engineers |
            | Compensation | 8 | Posted range of $215,000 to $245,000 clears the floor |
            | Remote policy | 8 | Hybrid, two days a month in Portland |
            | Stability | 9 | Same leadership team for four years |

            ## Risks

            - Platform and customer work compete for the same people; ask how priorities are set.
            - The on-call load for the platform team is not public.

            ## Next steps

            - [x] Tailor the resume around the port-scheduling migration story
            - [ ] Ask the recruiter for the outline of the interview loop
            """),
        new("Red flags checklist", "search-method", "stated", 70, ["checklist", "screening"],
            """
            ## Screen before applying

            - [ ] Role scope is clear and not three jobs in one
            - [ ] Compensation range is posted or confirmed early
            - [ ] No recent mass layoffs reported
            - [ ] The hiring manager can explain how the team is measured

            ## Reminder

            A vague answer to "why is this role open?" is worth writing down.
            """)
    ];
}
