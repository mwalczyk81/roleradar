using System.Text;

namespace RoleRadar.Domain.Notes;

public enum SectionReplacementStatus
{
    Success,
    SectionNotFound,
    AmbiguousHeading
}

public sealed record SectionReplacementOutcome(
    SectionReplacementStatus Status,
    string? UpdatedBody,
    IReadOnlyList<string> AvailableHeadings,
    IReadOnlyList<string> MatchingHeadings);

public sealed record ParsedMarkdownHeading(
    int Level,
    string RawHeadingLine,
    string TitleText,
    string NormalizedMarkerTitle,
    string HeadingPath,
    int StartCharIndex,
    int HeadingLineEndCharIndex,
    int SectionEndCharIndex,
    string LineEnding);

public static class MarkdownSectionParser
{
    public static SectionReplacementOutcome ReplaceSection(
        string originalBody,
        string targetHeading,
        string newSectionBody)
    {
        ArgumentNullException.ThrowIfNull(originalBody);
        ArgumentNullException.ThrowIfNull(newSectionBody);

        if (string.IsNullOrWhiteSpace(targetHeading))
        {
            return new SectionReplacementOutcome(
                SectionReplacementStatus.SectionNotFound,
                null,
                [],
                []);
        }

        var headings = ParseHeadings(originalBody);
        var availablePaths = headings.Select(h => h.HeadingPath).ToArray();

        if (headings.Count == 0)
        {
            return new SectionReplacementOutcome(
                SectionReplacementStatus.SectionNotFound,
                null,
                availablePaths,
                []);
        }

        var trimmedTarget = targetHeading.Trim();
        var matches = FindMatchingHeadings(headings, trimmedTarget);

        if (matches.Count == 0)
        {
            return new SectionReplacementOutcome(
                SectionReplacementStatus.SectionNotFound,
                null,
                availablePaths,
                []);
        }

        if (matches.Count > 1)
        {
            return new SectionReplacementOutcome(
                SectionReplacementStatus.AmbiguousHeading,
                null,
                availablePaths,
                matches.Select(m => m.HeadingPath).ToArray());
        }

        var target = matches[0];
        var dominantNewline = DetectDominantLineEnding(originalBody, target.LineEnding);
        var formattedReplacement = BuildReplacementSectionText(target, newSectionBody, dominantNewline);

        var sb = new StringBuilder(originalBody.Length + formattedReplacement.Length);
        sb.Append(originalBody.AsSpan(0, target.StartCharIndex));
        sb.Append(formattedReplacement);
        sb.Append(originalBody.AsSpan(target.SectionEndCharIndex));

        return new SectionReplacementOutcome(
            SectionReplacementStatus.Success,
            sb.ToString(),
            availablePaths,
            [target.HeadingPath]);
    }

    public static IReadOnlyList<ParsedMarkdownHeading> ParseHeadings(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var rawHeadings = new List<(int Level, string RawLine, string TitleText, int StartIdx, int LineEndIdx, string LineEnding)>();
        var inFence = false;
        var fenceChar = '\0';
        var fenceLen = 0;

        var pos = 0;
        var len = markdown.Length;

        while (pos < len)
        {
            var lineStart = pos;
            while (pos < len && markdown[pos] != '\r' && markdown[pos] != '\n')
            {
                pos++;
            }

            var contentEnd = pos;
            var lineEnding = "\n";
            if (pos < len)
            {
                if (markdown[pos] == '\r' && pos + 1 < len && markdown[pos + 1] == '\n')
                {
                    lineEnding = "\r\n";
                    pos += 2;
                }
                else
                {
                    lineEnding = markdown[pos].ToString();
                    pos += 1;
                }
            }

            var lineSpan = markdown.AsSpan(lineStart, contentEnd - lineStart);
            if (TryUpdateFenceState(lineSpan, ref inFence, ref fenceChar, ref fenceLen))
            {
                continue;
            }

            if (inFence)
            {
                continue;
            }

            if (TryParseAtxHeading(lineSpan, out var level, out var titleText))
            {
                rawHeadings.Add((
                    Level: level,
                    RawLine: lineSpan.ToString(),
                    TitleText: titleText,
                    StartIdx: lineStart,
                    LineEndIdx: pos,
                    LineEnding: lineEnding));
            }
        }

        if (rawHeadings.Count == 0)
        {
            return [];
        }

        var result = new List<ParsedMarkdownHeading>(rawHeadings.Count);
        var ancestorStack = new List<(int Level, string NormalizedMarkerTitle)>();

        for (var i = 0; i < rawHeadings.Count; i++)
        {
            var current = rawHeadings[i];
            var hashes = new string('#', current.Level);
            var normalizedMarkerTitle = string.IsNullOrEmpty(current.TitleText)
                ? hashes
                : $"{hashes} {current.TitleText}";

            while (ancestorStack.Count > 0 && ancestorStack[^1].Level >= current.Level)
            {
                ancestorStack.RemoveAt(ancestorStack.Count - 1);
            }

            ancestorStack.Add((current.Level, normalizedMarkerTitle));
            var headingPath = string.Join(" / ", ancestorStack.Select(a => a.NormalizedMarkerTitle));

            var sectionEndIdx = len;
            for (var j = i + 1; j < rawHeadings.Count; j++)
            {
                if (rawHeadings[j].Level <= current.Level)
                {
                    sectionEndIdx = rawHeadings[j].StartIdx;
                    break;
                }
            }

            result.Add(new ParsedMarkdownHeading(
                Level: current.Level,
                RawHeadingLine: current.RawLine,
                TitleText: current.TitleText,
                NormalizedMarkerTitle: normalizedMarkerTitle,
                HeadingPath: headingPath,
                StartCharIndex: current.StartIdx,
                HeadingLineEndCharIndex: current.LineEndIdx,
                SectionEndCharIndex: sectionEndIdx,
                LineEnding: current.LineEnding));
        }

        return result;
    }

    private static List<ParsedMarkdownHeading> FindMatchingHeadings(
        IReadOnlyList<ParsedMarkdownHeading> headings,
        string targetHeading)
    {
        if (targetHeading.Contains('/', StringComparison.Ordinal))
        {
            var normalizedSegments = targetHeading
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return headings
                .Where(h => HeadingPathMatches(h.HeadingPath, normalizedSegments))
                .ToList();
        }

        var strippedTarget = StripLeadingHashes(targetHeading);
        var hasExplicitHashes = targetHeading.StartsWith('#');

        return headings
            .Where(h =>
            {
                if (hasExplicitHashes)
                {
                    return string.Equals(h.NormalizedMarkerTitle, NormalizeMarkerHeading(targetHeading), StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(h.TitleText, strippedTarget, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(h.NormalizedMarkerTitle, targetHeading, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
    }

    private static bool HeadingPathMatches(string headingPath, string[] querySegments)
    {
        var pathSegments = headingPath.Split(" / ", StringSplitOptions.None);
        if (querySegments.Length > pathSegments.Length)
        {
            return false;
        }

        var offset = pathSegments.Length - querySegments.Length;
        for (var i = 0; i < querySegments.Length; i++)
        {
            var candidate = pathSegments[offset + i];
            var querySeg = querySegments[i];
            if (querySeg.StartsWith('#'))
            {
                if (!string.Equals(candidate, NormalizeMarkerHeading(querySeg), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                var candidateStripped = StripLeadingHashes(candidate);
                if (!string.Equals(candidateStripped, querySeg, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static string NormalizeMarkerHeading(string input)
    {
        var trimmed = input.Trim();
        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
        {
            i++;
        }

        var hashes = trimmed[..i];
        var rest = trimmed[i..].Trim();
        return string.IsNullOrEmpty(rest) ? hashes : $"{hashes} {rest}";
    }

    private static string StripLeadingHashes(string input)
    {
        var trimmed = input.Trim();
        var i = 0;
        while (i < trimmed.Length && trimmed[i] == '#')
        {
            i++;
        }

        return trimmed[i..].Trim();
    }

    private static string BuildReplacementSectionText(
        ParsedMarkdownHeading target,
        string newSectionBody,
        string newline)
    {
        var startsWithHeading = StartsWithSameHeading(newSectionBody, target);
        var builder = new StringBuilder(newSectionBody.Length + target.RawHeadingLine.Length + 4);

        if (!startsWithHeading)
        {
            builder.Append(target.RawHeadingLine).Append(newline);
        }

        builder.Append(newSectionBody);
        if (builder.Length > 0 && builder[^1] != '\n' && builder[^1] != '\r')
        {
            builder.Append(newline);
        }

        return builder.ToString();
    }

    private static bool StartsWithSameHeading(string newSectionBody, ParsedMarkdownHeading target)
    {
        var span = newSectionBody.AsSpan().TrimStart();
        var lineEnd = 0;
        while (lineEnd < span.Length && span[lineEnd] != '\r' && span[lineEnd] != '\n')
        {
            lineEnd++;
        }

        var firstLine = span[..lineEnd];
        if (!TryParseAtxHeading(firstLine, out var level, out var titleText))
        {
            return false;
        }

        return level == target.Level &&
               string.Equals(titleText, target.TitleText, StringComparison.OrdinalIgnoreCase);
    }

    private static string DetectDominantLineEnding(string body, string fallback)
    {
        var idx = body.IndexOf('\n');
        if (idx > 0 && body[idx - 1] == '\r')
        {
            return "\r\n";
        }

        if (idx >= 0)
        {
            return "\n";
        }

        return fallback;
    }

    public static bool TryUpdateFenceState(
        ReadOnlySpan<char> lineSpan,
        ref bool inFence,
        ref char fenceChar,
        ref int fenceLen)
    {
        var leadingSpaces = 0;
        while (leadingSpaces < lineSpan.Length && leadingSpaces < 3 && lineSpan[leadingSpaces] == ' ')
        {
            leadingSpaces++;
        }

        var trimmed = lineSpan[leadingSpaces..];
        if (trimmed.Length < 3)
        {
            return false;
        }

        var first = trimmed[0];
        if (first != '`' && first != '~')
        {
            return false;
        }

        var run = 0;
        while (run < trimmed.Length && trimmed[run] == first)
        {
            run++;
        }

        if (run < 3)
        {
            return false;
        }

        if (!inFence)
        {
            inFence = true;
            fenceChar = first;
            fenceLen = run;
            return true;
        }

        if (first == fenceChar && run >= fenceLen && trimmed[run..].Trim().IsEmpty)
        {
            inFence = false;
            fenceChar = '\0';
            fenceLen = 0;
            return true;
        }

        return false;
    }

    public static bool TryParseAtxHeading(
        ReadOnlySpan<char> lineSpan,
        out int level,
        out string titleText)
    {
        level = 0;
        titleText = string.Empty;

        var leadingSpaces = 0;
        while (leadingSpaces < lineSpan.Length && leadingSpaces < 3 && lineSpan[leadingSpaces] == ' ')
        {
            leadingSpaces++;
        }

        var span = lineSpan[leadingSpaces..];
        if (span.IsEmpty || span[0] != '#')
        {
            return false;
        }

        var hashes = 0;
        while (hashes < span.Length && span[hashes] == '#')
        {
            hashes++;
        }

        if (hashes is < 1 or > 6)
        {
            return false;
        }

        var rest = span[hashes..];
        if (!rest.IsEmpty && rest[0] != ' ' && rest[0] != '\t')
        {
            return false;
        }

        level = hashes;
        titleText = rest.Trim().ToString();
        return true;
    }
}
