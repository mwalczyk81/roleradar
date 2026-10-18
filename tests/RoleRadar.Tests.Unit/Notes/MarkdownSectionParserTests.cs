using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Notes;

public sealed class MarkdownSectionParserTests
{
    [Fact]
    public void ReplaceSection_ReplacesTargetSection_WhileLeavingSiblingSectionsAndCrlfUntouched()
    {
        const string original =
            "# Role Criteria\r\n" +
            "Intro paragraph.\r\n" +
            "## Compensation\r\n" +
            "Base target: 180k.\r\n" +
            "### Equity Details\r\n" +
            "Standard 4-year vesting.\r\n" +
            "## Location\r\n" +
            "Remote-first within US timezones.\r\n";

        var outcome = MarkdownSectionParser.ReplaceSection(
            originalBody: original,
            targetHeading: "Compensation",
            newSectionBody: "## Compensation\r\nBase target: 195k.\r\nUpdated bonus structure.\r\n");

        Assert.Equal(SectionReplacementStatus.Success, outcome.Status);
        Assert.NotNull(outcome.UpdatedBody);

        const string expected =
            "# Role Criteria\r\n" +
            "Intro paragraph.\r\n" +
            "## Compensation\r\n" +
            "Base target: 195k.\r\n" +
            "Updated bonus structure.\r\n" +
            "## Location\r\n" +
            "Remote-first within US timezones.\r\n";

        Assert.Equal(expected, outcome.UpdatedBody);
    }

    [Fact]
    public void ReplaceSection_WhenNewSectionBodyOmitsHeadingLine_PreservesOriginalHeadingLineAndLineEnding()
    {
        const string original =
            "# Interview Runbook\n" +
            "## System Design\n" +
            "Old notes.\n" +
            "## Behavioral\n" +
            "STAR stories.\n";

        var outcome = MarkdownSectionParser.ReplaceSection(
            originalBody: original,
            targetHeading: "## System Design",
            newSectionBody: "Updated system design checklist.\n");

        Assert.Equal(SectionReplacementStatus.Success, outcome.Status);

        const string expected =
            "# Interview Runbook\n" +
            "## System Design\n" +
            "Updated system design checklist.\n" +
            "## Behavioral\n" +
            "STAR stories.\n";

        Assert.Equal(expected, outcome.UpdatedBody);
    }

    [Fact]
    public void ReplaceSection_IgnoresHashCommentsInsideFencedCodeBlocks()
    {
        const string original =
            "# Scripting Guide\n" +
            "```powershell\n" +
            "# Target Section\n" +
            "Write-Host 'Inside code fence'\n" +
            "```\n" +
            "## Target Section\n" +
            "Real section content.\n" +
            "## Next Section\n" +
            "Trailing text.\n";

        var outcome = MarkdownSectionParser.ReplaceSection(
            originalBody: original,
            targetHeading: "Target Section",
            newSectionBody: "## Target Section\nReplaced real content.\n");

        Assert.Equal(SectionReplacementStatus.Success, outcome.Status);
        Assert.Contains("# Target Section\nWrite-Host 'Inside code fence'", outcome.UpdatedBody);
        Assert.Contains("## Target Section\nReplaced real content.\n## Next Section", outcome.UpdatedBody);
    }

    [Fact]
    public void ReplaceSection_WhenHeadingDoesNotExist_ReturnsSectionNotFoundWithAvailableHeadings()
    {
        const string original =
            "# Root Heading\n" +
            "## First Child\n" +
            "Text 1.\n" +
            "## Second Child\n" +
            "Text 2.\n";

        var outcome = MarkdownSectionParser.ReplaceSection(
            originalBody: original,
            targetHeading: "NonExistent Heading",
            newSectionBody: "New text.\n");

        Assert.Equal(SectionReplacementStatus.SectionNotFound, outcome.Status);
        Assert.Null(outcome.UpdatedBody);
        Assert.NotEmpty(outcome.AvailableHeadings);
        Assert.Contains(outcome.AvailableHeadings, h => h.Contains("First Child", StringComparison.Ordinal));
        Assert.Contains(outcome.AvailableHeadings, h => h.Contains("Second Child", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaceSection_WhenDuplicateHeadingsMatchAmbiguously_ReturnsAmbiguousHeadingWithAvailablePaths()
    {
        const string original =
            "# Company Alpha\n" +
            "## Notes\n" +
            "Alpha notes.\n" +
            "# Company Beta\n" +
            "## Notes\n" +
            "Beta notes.\n";

        var ambiguousOutcome = MarkdownSectionParser.ReplaceSection(
            originalBody: original,
            targetHeading: "Notes",
            newSectionBody: "Updated notes.\n");

        Assert.Equal(SectionReplacementStatus.AmbiguousHeading, ambiguousOutcome.Status);
        Assert.Null(ambiguousOutcome.UpdatedBody);
        Assert.Equal(2, ambiguousOutcome.MatchingHeadings.Count);

        // Disambiguating via full heading path succeeds
        var disambiguatedOutcome = MarkdownSectionParser.ReplaceSection(
            originalBody: original,
            targetHeading: "# Company Beta / ## Notes",
            newSectionBody: "## Notes\nUpdated Beta notes.\n");

        Assert.Equal(SectionReplacementStatus.Success, disambiguatedOutcome.Status);
        Assert.Contains("Alpha notes.", disambiguatedOutcome.UpdatedBody);
        Assert.Contains("Updated Beta notes.", disambiguatedOutcome.UpdatedBody);
    }
}
