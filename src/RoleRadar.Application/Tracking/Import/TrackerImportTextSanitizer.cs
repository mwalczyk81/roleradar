using System.Net;
using System.Text.RegularExpressions;

namespace RoleRadar.Application.Tracking.Import;

/// <summary>
/// Sanitizes text imported from tracker files (JSON, CSV) by decoding HTML entities,
/// converting basic formatting tags (strong/b, em/i, br) to markdown in free text,
/// and stripping remaining HTML tags.
/// </summary>
public static class TrackerImportTextSanitizer
{
    private static readonly Regex CommentRegex = new(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ScriptRegex = new(@"<\s*script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\s*/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex StyleRegex = new(@"<\s*style\b[^<]*(?:(?!<\/style>)<[^<]*)*<\s*/\s*style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BrRegex = new(@"<\s*br\b[^>]*\s*/?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StrongOpenRegex = new(@"<\s*(?:strong|b)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StrongCloseRegex = new(@"</\s*(?:strong|b)\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmOpenRegex = new(@"<\s*(?:em|i)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex EmCloseRegex = new(@"</\s*(?:em|i)\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"</?[a-z][a-z0-9]*\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NewlineNormalizerRegex = new(@"\r\n|\r", RegexOptions.Compiled);

    /// <summary>
    /// Decodes HTML entities, converts strong/b to **text**, em/i to *text*, br to \n,
    /// and strips any other HTML tags from imported free text (notes, culture, reasons, event notes).
    /// </summary>
    public static string? SanitizeFreeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // 1. Decode HTML entities first (&amp;, &quot;, &lt;, &gt;, &middot;, &ndash;, &mdash;, &nbsp;, etc.)
        var text = WebUtility.HtmlDecode(value);
        text = text.Replace('\u00A0', ' ');

        // 2. Remove HTML comments, scripts, styles
        text = CommentRegex.Replace(text, string.Empty);
        text = ScriptRegex.Replace(text, string.Empty);
        text = StyleRegex.Replace(text, string.Empty);

        // 3. Convert <br> variants to newline
        text = BrRegex.Replace(text, "\n");

        // 4. Convert <strong> and <b> to **
        text = StrongOpenRegex.Replace(text, "**");
        text = StrongCloseRegex.Replace(text, "**");

        // 5. Convert <em> and <i> to *
        text = EmOpenRegex.Replace(text, "*");
        text = EmCloseRegex.Replace(text, "*");

        // 6. Strip all other HTML tags
        text = TagRegex.Replace(text, string.Empty);

        // 7. Normalize line endings to \n
        text = NewlineNormalizerRegex.Replace(text, "\n");

        return text;
    }

    /// <summary>
    /// Decodes HTML entities and strips all HTML tags (including strong/em/br) without formatting.
    /// Used for titles, company names, requisition IDs, URLs, and locations so dedup keys and matching operate on clean text.
    /// </summary>
    public static string? SanitizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        // 1. Decode HTML entities first
        var text = WebUtility.HtmlDecode(value);
        text = text.Replace('\u00A0', ' ');

        // 2. Remove HTML comments, scripts, styles
        text = CommentRegex.Replace(text, string.Empty);
        text = ScriptRegex.Replace(text, string.Empty);
        text = StyleRegex.Replace(text, string.Empty);

        // 3. Convert <br> variants to space so words don't concatenate
        text = BrRegex.Replace(text, " ");

        // 4. Strip all HTML tags
        text = TagRegex.Replace(text, string.Empty);

        // 5. Replace newlines and tabs with space
        text = Regex.Replace(text, @"[\r\n\t]+", " ");

        // 6. Collapse multiple consecutive spaces
        text = Regex.Replace(text, @" {2,}", " ");

        return text;
    }
}
