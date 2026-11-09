using System.Text.RegularExpressions;
using RoleRadar.Cli.Demo;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>Shape and hygiene of the fictional demo dataset (data-model.md, FR-010). Pure data: no database.</summary>
public sealed partial class DemoDatasetTests
{
    // The owner-reviewed company names from the spec. Every company in the dataset must be one of these.
    private static readonly string[] ApprovedCompanies =
    [
        "Harborlight Systems", "Quillfeather Labs", "Brindle & Moss Analytics", "Copperfield Robotics",
        "Tamarack Data Works", "Velvet Anvil Studios", "Larkspur Freight Software", "Ostrander Cloud",
        "Fernhollow Payments", "Saltmarsh Insurance Tech", "Kestrel Ridge Energy", "Mossgate Learning",
        "Thistledown Security", "Wrenfield Marketplace"
    ];

    private static DemoPosting[] WithApplication => [.. DemoDataset.Postings.Where(p => p.AppliedDaysBeforeAsOf.HasValue)];

    [Fact]
    public void Companies_AreExactlyTheFourteenApprovedNames()
    {
        Assert.Equal(14, DemoDataset.Companies.Count);
        Assert.Equal(ApprovedCompanies.OrderBy(n => n, StringComparer.Ordinal), DemoDataset.Companies.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(DemoDataset.Postings, p => Assert.Contains(p.Company, ApprovedCompanies));
    }

    [Fact]
    public void Postings_HaveTheSpecifiedCountAndDispositionMix()
    {
        Assert.Equal(48, DemoDataset.Postings.Count);
        var byDisposition = DemoDataset.Postings.GroupBy(p => p.Disposition).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(8, byDisposition[PostingDisposition.Lead]);
        Assert.Equal(3, byDisposition[PostingDisposition.Unscored]);
        Assert.Equal(7, byDisposition[PostingDisposition.Scored]);
        Assert.Equal(7, byDisposition[PostingDisposition.ScreenedOut]);
        Assert.Equal(12, byDisposition[PostingDisposition.Applied]);
        Assert.Equal(6, byDisposition[PostingDisposition.Rejected]);
        Assert.Equal(5, byDisposition[PostingDisposition.Closed]);
        Assert.Equal(Enum.GetValues<PostingDisposition>().Length, byDisposition.Count);
    }

    [Fact]
    public void Applications_AreTwelveAppliedSixRejectedAndTwoClosed()
    {
        Assert.Equal(20, WithApplication.Length);
        Assert.Equal(12, WithApplication.Count(p => p.Disposition == PostingDisposition.Applied));
        Assert.Equal(6, WithApplication.Count(p => p.Disposition == PostingDisposition.Rejected));
        Assert.Equal(2, WithApplication.Count(p => p.Disposition == PostingDisposition.Closed));
        Assert.All(DemoDataset.Postings.Where(p => !p.AppliedDaysBeforeAsOf.HasValue), p => Assert.Empty(p.Events));
    }

    [Fact]
    public void Events_CoverEveryCountedType()
    {
        var used = WithApplication.SelectMany(p => p.Events).Select(e => e.Type).ToHashSet();

        foreach (var type in new[]
                 {
                     ApplicationEventType.Interview, ApplicationEventType.Offer, ApplicationEventType.OfferAccepted,
                     ApplicationEventType.OfferDeclined, ApplicationEventType.Rejected, ApplicationEventType.Withdrawn,
                     ApplicationEventType.FollowUp, ApplicationEventType.Note
                 })
        {
            Assert.Contains(type, used);
        }
    }

    [Fact]
    public void Events_AreNeverBeforeTheApplicationAndNeverInTheFuture()
    {
        foreach (var posting in WithApplication)
        {
            Assert.All(posting.Events, e =>
            {
                Assert.True(e.DaysBeforeAsOf <= posting.AppliedDaysBeforeAsOf, $"{posting.Slug}: event before application");
                Assert.True(e.DaysBeforeAsOf >= 0, $"{posting.Slug}: event in the future");
            });
            Assert.True(posting.FirstSeenDaysBeforeAsOf >= posting.AppliedDaysBeforeAsOf, $"{posting.Slug}: applied before first seen");
        }
    }

    [Fact]
    public void AtLeastThreeOldApplicationsAreGhostedAndAtLeastOneIsRecentAndOpen()
    {
        var silentAndOld = WithApplication.Count(p => p.Events.All(e => e.Type == ApplicationEventType.Note) && p.AppliedDaysBeforeAsOf >= 30);
        var recentOpen = WithApplication.Count(p => p.AppliedDaysBeforeAsOf < 30 && p.Disposition == PostingDisposition.Applied);

        Assert.True(silentAndOld >= 3, $"expected at least 3 old silent applications, found {silentAndOld}");
        Assert.True(recentOpen >= 1);
    }

    [Fact]
    public void NinetyDayWindow_HasOneBigAndOneSmallDenominator()
    {
        var inWindow = WithApplication.Where(p => p.AppliedDaysBeforeAsOf <= 89).ToArray();
        var interviewed = inWindow.Count(p => p.Events.Any(e => e.Type == ApplicationEventType.Interview));

        Assert.True(inWindow.Length >= 10, $"applications applied in the window: {inWindow.Length}");
        Assert.InRange(interviewed, 3, 9);

        var firstSeenInWindow = DemoDataset.Postings.Where(p => p.FirstSeenDaysBeforeAsOf <= 89).ToArray();
        Assert.True(firstSeenInWindow.Length >= 10);
        foreach (var disposition in Enum.GetValues<PostingDisposition>())
        {
            Assert.Contains(firstSeenInWindow, p => p.Disposition == disposition);
        }
    }

    [Fact]
    public void Dates_AreRelativeAndSpanAboutOneHundredFiftyDays()
    {
        var oldest = DemoDataset.Postings.Max(p => p.FirstSeenDaysBeforeAsOf);
        var newest = DemoDataset.Postings.Min(p => p.FirstSeenDaysBeforeAsOf);

        Assert.InRange(oldest, 120, 160);
        Assert.InRange(newest, 0, 5);
    }

    [Fact]
    public void SearchRuns_AreEightWithMixedYieldsAndOneOutsideTheWindow()
    {
        Assert.Equal(8, DemoDataset.SearchRuns.Count);
        Assert.Equal(DemoDataset.SearchRuns.Count, DemoDataset.SearchRuns.Select(r => r.DaysBeforeAsOf).Distinct().Count());
        Assert.Equal(1, DemoDataset.SearchRuns.Count(r => r.DaysBeforeAsOf >= 90));
        Assert.Contains(DemoDataset.SearchRuns, r => r.NewPostings == 0);
        Assert.InRange(DemoDataset.SearchRuns.Max(r => r.NewPostings), 10, 14);
    }

    [Fact]
    public void Notes_AreSixMarkdownBodiesWithoutRawHtmlOrImages()
    {
        Assert.Equal(6, DemoDataset.Notes.Count);
        Assert.Equal(6, DemoDataset.Notes.Select(n => n.Title).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var note in DemoDataset.Notes)
        {
            Assert.DoesNotMatch(RawHtml(), note.Body);
            Assert.DoesNotContain("![", note.Body, StringComparison.Ordinal);
            Assert.Contains("\n## ", "\n" + note.Body, StringComparison.Ordinal);
        }

        Assert.Contains(DemoDataset.Notes, n => n.Body.Contains("| ---", StringComparison.Ordinal));
        Assert.Contains(DemoDataset.Notes, n => n.Body.Contains("- [ ]", StringComparison.Ordinal));
        Assert.Contains(DemoDataset.Notes, n => n.Body.Contains("```", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryUrlUsesAFictionalHost()
    {
        var text = string.Join('\n', DemoDataset.Postings.Select(p => p.Url)
            .Concat(DemoDataset.Notes.Select(n => n.Body))
            .Concat(DemoDataset.Postings.SelectMany(p => p.Signals).Select(s => s.Evidence))
            .Concat(DemoDataset.Postings.Select(p => p.Compensation?.Text ?? string.Empty))
            .Concat(DemoDataset.Companies.Select(c => c.CareerSourceUrl ?? string.Empty)));

        foreach (Match match in Url().Matches(text))
        {
            var host = new Uri(match.Value).Host;
            Assert.True(host is "jobs.example.com" or "example.com" or "www.example.com", $"unexpected host: {host}");
        }

        Assert.All(DemoDataset.Postings, p => Assert.StartsWith("https://jobs.example.com/", p.Url, StringComparison.Ordinal));
        Assert.Equal(DemoDataset.Postings.Count, DemoDataset.Postings.Select(p => p.Url).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void PostingsAreUniqueByCompanyAndTitle_SoTheNaturalKeyDoesNotCollide()
    {
        var keys = DemoDataset.Postings.Select(p => (p.Company, p.Title)).ToArray();
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    // ---- enrichment of the hero records (the screenshots' posting, company and application) ----

    private static DemoPosting Hero => DemoDataset.Postings.Single(p => p.Slug == DemoDataset.HeroPostingSlug);

    [Fact]
    public void HeroPosting_IsHarborlightsHeadOfSoftwareDevelopment()
    {
        Assert.Equal("Harborlight Systems", Hero.Company);
        Assert.Equal("Head of Software Development", Hero.Title);
        Assert.Equal(DemoDataset.HeroCompany, Hero.Company);
    }

    [Fact]
    public void HeroPosting_HasACompensationTextAndRange()
    {
        var pay = Assert.IsType<Compensation>(Hero.Compensation);
        Assert.False(string.IsNullOrWhiteSpace(pay.Text));
        Assert.True(pay.Text!.Length <= Posting.MaxCompensationTextLength);
        Assert.True(pay.Min is > 0 && pay.Max > pay.Min, "hero range must be a real min below max");
        Assert.Equal("USD", pay.Currency);
    }

    [Fact]
    public void HeroPosting_HasAMarkdownAssessmentNoteThatIsOneOfTheSixNotes()
    {
        Assert.False(string.IsNullOrWhiteSpace(Hero.AssessmentNoteTitle));
        var note = Assert.Single(DemoDataset.Notes, n => n.Title == Hero.AssessmentNoteTitle);
        Assert.Equal("assessment", note.Area);
        Assert.Contains("\n## ", "\n" + note.Body, StringComparison.Ordinal);
        Assert.Contains("| ---", note.Body, StringComparison.Ordinal);
        Assert.Single(DemoDataset.Postings, p => p.AssessmentNoteTitle is not null);
    }

    [Fact]
    public void HeroPosting_HasTwoOrThreeDistinctSignals_ConsistentWithItsTimeline()
    {
        Assert.InRange(Hero.Signals.Count, 2, 3);
        Assert.Equal(Hero.Signals.Count, Hero.Signals.Select(s => s.Type).Distinct().Count());
        // It posts a range, so a "no salary" signal would contradict the record.
        Assert.DoesNotContain(Hero.Signals, s => s.Type == SignalType.NoSalary);
        Assert.All(Hero.Signals, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Evidence));
            Assert.True(s.Evidence.Length <= PostingSignal.MaxEvidenceLength);
            Assert.InRange(s.DaysBeforeAsOf, Hero.AppliedDaysBeforeAsOf!.Value, Hero.FirstSeenDaysBeforeAsOf);
            Assert.Equal(s.Type == SignalType.Deadline, s.DeadlineDaysBeforeAsOf.HasValue);
        });
        Assert.All(DemoDataset.Postings.Where(p => p.Slug != DemoDataset.HeroPostingSlug), p => Assert.Empty(p.Signals));
    }

    [Fact]
    public void SomeScoredPostings_PostACompensationRange()
    {
        var withRange = DemoDataset.Postings
            .Where(p => p.Disposition == PostingDisposition.Scored && p.Compensation is { Min: not null, Max: not null })
            .ToArray();

        Assert.InRange(withRange.Length, 3, 6);
        Assert.All(withRange, p =>
        {
            Assert.True(p.Compensation!.Max > p.Compensation.Min, $"{p.Slug}: max must exceed min");
            Assert.False(string.IsNullOrWhiteSpace(p.Compensation.Text), $"{p.Slug}: the detail page shows the text");
        });
    }

    [Fact]
    public void HeroCompany_HasCategoryTagsAndOneFictionalCareerSource()
    {
        var hero = DemoDataset.Companies.Single(c => c.Name == DemoDataset.HeroCompany);

        Assert.InRange(hero.CategoryTags.Count, 2, Company.MaxCategoryTagsCount);
        Assert.All(hero.CategoryTags, t => Assert.InRange(t.Length, 1, Company.MaxCategoryTagLength));
        Assert.Equal(hero.CategoryTags.Count, hero.CategoryTags.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        Assert.NotNull(hero.CareerSourceUrl);
        Assert.StartsWith("https://jobs.example.com/", hero.CareerSourceUrl, StringComparison.Ordinal);
        Assert.True(hero.CareerSourceUrl!.Length <= CareerSource.MaxSlugLength);
        Assert.Single(DemoDataset.Companies, c => c.CareerSourceUrl is not null);
    }

    [Fact]
    public void ScreenshotManifest_NamesTheSeedsHeroRecords()
    {
        // docs/images/manifest.json names the records the screenshot spec opens; they must exist in this data set. The
        // check lives here because the Linux web gate does not copy src/ into its container.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.FullName, "docs", "images", "manifest.json")));
        var records = manifest.RootElement.GetProperty("hero").GetProperty("records");

        Assert.Equal(DemoDataset.HeroCompany, records.GetProperty("company").GetString());
        Assert.Equal(Hero.Title, records.GetProperty("posting").GetString());
        Assert.Contains(DemoDataset.Notes, n => n.Title == records.GetProperty("note").GetString());
        Assert.Equal("interviewed", records.GetProperty("drilldown").GetString());
    }

    [GeneratedRegex(@"<\s*/?\s*[A-Za-z]")]
    private static partial Regex RawHtml();

    [GeneratedRegex(@"https?://[^\s)>\]]+")]
    private static partial Regex Url();
}
