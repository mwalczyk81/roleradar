using System.Text;
using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Import;

public sealed class MarkdownSplitterAndProvenanceParserTests
{
    private const string DefaultTestDate = "2026-10-17";

    [Fact]
    public void ValidateUtf8_WhenGivenInvalidUtf8Bytes_ReturnsFalse()
    {
        // 0xFF and 0xC0 0xAF are invalid UTF-8 byte sequences
        byte[] invalidBytes = [0xFF, 0xFE, 0x41, 0x42];
        var isValid = MarkdownImportParser.ValidateUtf8(invalidBytes);

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateUtf8_WhenGivenValidUtf8BytesWithBomAndCrlf_ReturnsTrue()
    {
        var validText = "\uFEFF# Valid Note\r\nSome body text.\r\n";
        var validBytes = Encoding.UTF8.GetBytes(validText);

        var isValid = MarkdownImportParser.ValidateUtf8(validBytes);

        Assert.True(isValid);
    }

    [Fact]
    public void Parse_YamlFrontMatterContainingHashLines_BelongsToPreambleAndHashesAreNeverHeadings()
    {
        const string markdown =
            "---\r\n" +
            "# tag1\r\n" +
            "key: value # inline comment\r\n" +
            "## another comment\r\n" +
            "---\r\n" +
            "Intro paragraph after front matter.\r\n" +
            "# Real H1 Heading\r\n" +
            "Body of H1.\r\n";

        var sections = MarkdownImportParser.Parse("test.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);

        var preamble = sections[0];
        Assert.Equal("$preamble", preamble.HeadingPath);
        Assert.Equal(0, preamble.SectionOrderIndex);
        Assert.Equal(0, preamble.OccurrenceIndex);
        Assert.Contains("# tag1", preamble.Body);
        Assert.Contains("## another comment", preamble.Body);
        Assert.Contains("Intro paragraph after front matter.", preamble.Body);

        var h1 = sections[1];
        Assert.Equal("# Real H1 Heading", h1.HeadingPath);
        Assert.Equal("Real H1 Heading", h1.Title);
        Assert.Equal(1, h1.SectionOrderIndex);
        Assert.Equal("test.md## Real H1 Heading#0", h1.ImportKey);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_FencedCodeBlockContainingHeadingsAndProvenanceTag_HidesHeadingsAndIgnoresEnclosedTags()
    {
        const string markdown =
            "# Scripting Guide\n" +
            "Intro text.\n" +
            "```powershell\n" +
            "# Fake Heading Inside Fence\n" +
            "## Another Fake Heading [stated 2026-01-01]\n" +
            "Write-Output 'Hello World'\n" +
            "```\n" +
            "More body outside fence. [researched 2026-02-15 verified]\n" +
            "## Real Second Section\n" +
            "Content here.\n";

        var sections = MarkdownImportParser.Parse("guide.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);

        var firstSection = sections[0];
        Assert.Equal("# Scripting Guide", firstSection.HeadingPath);
        Assert.Contains("# Fake Heading Inside Fence", firstSection.Body);
        Assert.Contains("## Another Fake Heading [stated 2026-01-01]", firstSection.Body);

        // The tag inside the code fence [stated 2026-01-01] must be ignored; the outer tag [researched 2026-02-15] wins
        Assert.Equal(ProvenanceKind.Researched, firstSection.Provenance.Kind);
        Assert.Equal("2026-02-15", firstSection.Provenance.Date);
        Assert.Equal("verified", firstSection.Provenance.Qualifier);

        var secondSection = sections[1];
        Assert.Equal("# Scripting Guide / ## Real Second Section", secondSection.HeadingPath);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_TildeFencedCodeBlock_AlsoHidesHeadingsAndTags()
    {
        const string markdown =
            "# Tilde Example\n" +
            "~~~bash\n" +
            "# Not a heading [stated 2025-01-01]\n" +
            "~~~\n" +
            "Body after tilde fence. [inferred]\n";

        var sections = MarkdownImportParser.Parse("tilde.md", markdown, DefaultTestDate);

        Assert.Single(sections);
        Assert.Equal(ProvenanceKind.Inferred, sections[0].Provenance.Kind);
        Assert.Equal(DefaultTestDate, sections[0].Provenance.Date);
    }

    [Fact]
    public void Parse_IndentedCodeLineStartingWithHash_IsTreatedAsCodeAndNotHeading()
    {
        const string markdown =
            "# Main Heading\n" +
            "Normal paragraph.\n" +
            "    # Indented 4 spaces code comment\n" +
            "    ## Another indented line\n" +
            "Trailing line.\n";

        var sections = MarkdownImportParser.Parse("code.md", markdown, DefaultTestDate);

        Assert.Single(sections);
        Assert.Equal("# Main Heading", sections[0].HeadingPath);
        Assert.Contains("    # Indented 4 spaces code comment\n", sections[0].Body);
    }

    [Fact]
    public void Parse_HashTagWithoutWhitespace_IsNotAHeading()
    {
        const string markdown =
            "# Main Heading\n" +
            "Paragraph mentioning #hashtag and #csharp in text.\n" +
            "## Child Heading\n" +
            "More text with #dotnet.\n";

        var sections = MarkdownImportParser.Parse("tags.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);
        Assert.Equal("# Main Heading", sections[0].HeadingPath);
        Assert.Contains("#hashtag", sections[0].Body);
        Assert.Equal("# Main Heading / ## Child Heading", sections[1].HeadingPath);
    }

    [Fact]
    public void Parse_H3AndDeeperHeadings_StayInsideParentSectionWithoutSplitting()
    {
        const string markdown =
            "# Root H1\n" +
            "Body for H1.\n" +
            "### Subheading H3\n" +
            "Body for H3 stays inside H1.\n" +
            "#### Subheading H4\n" +
            "Body for H4.\n" +
            "## Section H2\n" +
            "Body for H2.\n" +
            "### Another H3\n" +
            "H3 under H2 stays inside H2.\n";

        var sections = MarkdownImportParser.Parse("deep.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);
        Assert.Equal("# Root H1", sections[0].HeadingPath);
        Assert.Contains("### Subheading H3\nBody for H3 stays inside H1.", sections[0].Body);
        Assert.Contains("#### Subheading H4\nBody for H4.", sections[0].Body);

        Assert.Equal("# Root H1 / ## Section H2", sections[1].HeadingPath);
        Assert.Contains("### Another H3\nH3 under H2 stays inside H2.", sections[1].Body);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_SetextHeadings_AreNotSplitPointsAndStayVerbatimInBody()
    {
        const string markdown =
            "# ATX Heading 1\n" +
            "Setext Heading 1\n" +
            "===============\n" +
            "Body after setext h1.\n" +
            "Setext Heading 2\n" +
            "---------------\n" +
            "Body after setext h2.\n";

        var sections = MarkdownImportParser.Parse("setext.md", markdown, DefaultTestDate);

        Assert.Single(sections);
        Assert.Equal("# ATX Heading 1", sections[0].HeadingPath);
        Assert.Contains("Setext Heading 1\n===============\n", sections[0].Body);
        Assert.Contains("Setext Heading 2\n---------------\n", sections[0].Body);
    }

    [Fact]
    public void Parse_DuplicateSiblingHeadings_DisambiguatesWithOccurrenceIndex()
    {
        const string markdown =
            "# Company Alpha\n" +
            "## Interview Notes\n" +
            "First loop notes.\n" +
            "## Interview Notes\n" +
            "Second loop notes.\n" +
            "## Interview Notes\n" +
            "Third loop notes.\n";

        var sections = MarkdownImportParser.Parse("company.md", markdown, DefaultTestDate);

        Assert.Equal(4, sections.Count);

        Assert.Equal("# Company Alpha", sections[0].HeadingPath);
        Assert.Equal(0, sections[0].OccurrenceIndex);

        Assert.Equal("# Company Alpha / ## Interview Notes", sections[1].HeadingPath);
        Assert.Equal(0, sections[1].OccurrenceIndex);
        Assert.Equal("company.md## Company Alpha / ## Interview Notes#0", sections[1].ImportKey);

        Assert.Equal("# Company Alpha / ## Interview Notes", sections[2].HeadingPath);
        Assert.Equal(1, sections[2].OccurrenceIndex);
        Assert.Equal("company.md## Company Alpha / ## Interview Notes#1", sections[2].ImportKey);

        Assert.Equal("# Company Alpha / ## Interview Notes", sections[3].HeadingPath);
        Assert.Equal(2, sections[3].OccurrenceIndex);
        Assert.Equal("company.md## Company Alpha / ## Interview Notes#2", sections[3].ImportKey);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_EmptyHeadingAndBareHeadingMarkers_PreservesExactBytesAndAssignsPlaceholderTitle()
    {
        const string markdown =
            "# Main\r\n" +
            "## Empty Section\r\n" +
            "## \r\n" +
            "## Third Section\r\n" +
            "End of file text.\r\n";

        var sections = MarkdownImportParser.Parse("empty.md", markdown, DefaultTestDate);

        Assert.Equal(4, sections.Count);

        var emptyHeading = sections[1];
        Assert.Equal("Empty Section", emptyHeading.Title);
        Assert.Equal("## Empty Section\r\n", emptyHeading.Body);

        var bareHeading = sections[2];
        Assert.False(string.IsNullOrWhiteSpace(bareHeading.Title)); // Deterministic placeholder e.g. "(Untitled Heading #1)"
        Assert.Equal("## \r\n", bareHeading.Body);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_MixedCrlfAndLfInOneFile_PreservesExactBytesVerbatim()
    {
        const string markdown =
            "# Windows Heading\r\n" +
            "Windows body with CRLF.\r\n" +
            "## Linux Heading\n" +
            "Linux body with LF only.\n" +
            "Another mixed line.\r\n";

        var sections = MarkdownImportParser.Parse("mixed.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);
        Assert.Equal("# Windows Heading\r\nWindows body with CRLF.\r\n", sections[0].Body);
        Assert.Equal("## Linux Heading\nLinux body with LF only.\nAnother mixed line.\r\n", sections[1].Body);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_FileWithNoTrailingNewline_PreservesExactBytesWithoutAddingNewline()
    {
        const string markdown = "# Heading\nNo trailing newline here";

        var sections = MarkdownImportParser.Parse("nonewline.md", markdown, DefaultTestDate);

        Assert.Single(sections);
        Assert.Equal(markdown, sections[0].Body);
    }

    [Fact]
    public void Parse_Utf8BomOnPreamble_PreservesBomOnSectionOrderIndexZero()
    {
        const string markdown = "\uFEFFPreamble text with BOM.\r\n# First Heading\r\nContent.\r\n";

        var sections = MarkdownImportParser.Parse("bom.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);
        Assert.Equal(0, sections[0].SectionOrderIndex);
        Assert.StartsWith("\uFEFF", sections[0].Body, StringComparison.Ordinal);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(markdown, reassembled);
    }

    [Fact]
    public void Parse_ProvenanceTagExtraction_FirstTagInDocumentOrderWinsAndUndatedTagUsesDefaultDate()
    {
        const string markdown =
            "# Strategy [stated]\n" +
            "Intro paragraph. [researched 2026-03-01 secondary tag]\n" +
            "## Details\n" +
            "Unprovenanced section.\n";

        var sections = MarkdownImportParser.Parse("prov.md", markdown, DefaultTestDate);

        Assert.Equal(2, sections.Count);

        // First section has [stated] in heading, followed by [researched 2026-03-01] in body -> [stated] wins with DefaultTestDate
        var sec1 = sections[0];
        Assert.Equal(ProvenanceKind.Stated, sec1.Provenance.Kind);
        Assert.Equal(DefaultTestDate, sec1.Provenance.Date);
        Assert.Null(sec1.Provenance.Qualifier);

        // All tags remain verbatim in body
        Assert.Contains("[stated]", sec1.Body);
        Assert.Contains("[researched 2026-03-01 secondary tag]", sec1.Body);

        // Second section has no tag -> defaults to inferred with DefaultTestDate
        var sec2 = sections[1];
        Assert.Equal(ProvenanceKind.Inferred, sec2.Provenance.Kind);
        Assert.Equal(DefaultTestDate, sec2.Provenance.Date);
    }

    [Fact]
    public void Parse_FictionalMemoryPrimaryFixture_Reassembles100PercentByteIdentical()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/fictional-memory-primary.md");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine(Directory.GetCurrentDirectory(), "tests/fixtures/fictional-memory-primary.md");
        }

        var expectedText = File.ReadAllText(fixturePath, Encoding.UTF8);
        var sections = MarkdownImportParser.Parse("fictional-memory-primary.md", expectedText, "2026-01-01");

        Assert.NotEmpty(sections);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(expectedText, reassembled);
    }

    [Fact]
    public void Parse_FictionalMemoryArchiveFixture_Reassembles100PercentByteIdentical()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures/fictional-memory-archive.md");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.Combine(Directory.GetCurrentDirectory(), "tests/fixtures/fictional-memory-archive.md");
        }

        var expectedText = File.ReadAllText(fixturePath, Encoding.UTF8);
        var sections = MarkdownImportParser.Parse("fictional-memory-archive.md", expectedText, "2025-11-01");

        Assert.NotEmpty(sections);

        var reassembled = string.Concat(sections.OrderBy(s => s.SectionOrderIndex).Select(s => s.Body));
        Assert.Equal(expectedText, reassembled);
    }
}
