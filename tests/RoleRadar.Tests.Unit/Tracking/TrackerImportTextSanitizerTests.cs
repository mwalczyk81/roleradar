using RoleRadar.Application.Tracking.Import;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class TrackerImportTextSanitizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void SanitizeFreeText_NullOrWhitespace_ReturnsInput(string? input, string? expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Rock &amp; Roll", "Rock & Roll")]
    [InlineData("Quote: &quot;hello&quot;", "Quote: \"hello\"")]
    [InlineData("5 &lt; 10", "5 < 10")]
    [InlineData("Item 1 &middot; Item 2", "Item 1 · Item 2")]
    [InlineData("Non&nbsp;breaking", "Non breaking")]
    public void SanitizeFreeText_DecodesHtmlEntities(string input, string expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("This is <strong>important</strong>.", "This is **important**.")]
    [InlineData("This is <b>bold</b>.", "This is **bold**.")]
    [InlineData("<STRONG>ALL CAPS</STRONG>", "**ALL CAPS**")]
    [InlineData("<b class=\"lead\">Styled</b>", "**Styled**")]
    public void SanitizeFreeText_ConvertsStrongAndB_ToMarkdownBold(string input, string expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("This is <em>emphasized</em>.", "This is *emphasized*.")]
    [InlineData("This is <i>italic</i>.", "This is *italic*.")]
    [InlineData("<EM>SHOUTING</EM>", "*SHOUTING*")]
    [InlineData("<i id=\"x\">Styled</i>", "*Styled*")]
    public void SanitizeFreeText_ConvertsEmAndI_ToMarkdownItalic(string input, string expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Line 1<br>Line 2", "Line 1\nLine 2")]
    [InlineData("Line 1<br/>Line 2", "Line 1\nLine 2")]
    [InlineData("Line 1<br />Line 2", "Line 1\nLine 2")]
    [InlineData("Line 1<BR>Line 2", "Line 1\nLine 2")]
    [InlineData("Line 1<br class=\"clear\" />Line 2", "Line 1\nLine 2")]
    public void SanitizeFreeText_ConvertsBr_ToNewline(string input, string expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SanitizeFreeText_StripsOtherHtmlTags_PreservingContent()
    {
        var input = "<div><span>Hello</span> <a href=\"https://example.com\">world</a>! <p>Paragraph</p></div>";
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.NotNull(actual);
        Assert.Equal("Hello world! Paragraph", actual.Trim());
    }

    [Fact]
    public void SanitizeFreeText_RemovesHtmlComments()
    {
        var input = "Before <!-- this is a comment --> After";
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal("Before  After", actual);
    }

    [Fact]
    public void SanitizeFreeText_CombinedComplexText()
    {
        var input = "<strong>Overview:</strong><br>Found via <em>LinkedIn</em> &amp; referral.<!-- secret note --><p>Details here.</p>";
        var expected = "**Overview:**\nFound via *LinkedIn* & referral.Details here.";
        var actual = TrackerImportTextSanitizer.SanitizeFreeText(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void SanitizePlainText_NullOrWhitespace_ReturnsInput(string? input, string? expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizePlainText(input);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("<b>Senior</b> Software Engineer", "Senior Software Engineer")]
    [InlineData("<strong>Principal</strong> Architect", "Principal Architect")]
    [InlineData("Staff <i>Engineer</i>", "Staff Engineer")]
    [InlineData("Lead<br>Developer", "Lead Developer")]
    [InlineData("AT&amp;T Services", "AT&T Services")]
    [InlineData("Acme <!-- notes --> Corp", "Acme Corp")]
    public void SanitizePlainText_StripsAllTagsAndDecodesEntities(string input, string expected)
    {
        var actual = TrackerImportTextSanitizer.SanitizePlainText(input);
        Assert.NotNull(actual);
        Assert.Equal(expected, actual.Trim());
    }
}
