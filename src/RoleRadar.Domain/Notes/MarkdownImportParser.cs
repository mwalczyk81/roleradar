using System.Text;
using System.Text.RegularExpressions;
using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Notes;

public sealed record ParsedImportSection(
    string SourceFileId,
    string HeadingPath,
    int OccurrenceIndex,
    int SectionOrderIndex,
    string Title,
    string Body,
    ProvenanceMarker Provenance,
    string BodyHash,
    string ImportKey);

public static partial class MarkdownImportParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [GeneratedRegex(@"\[(stated|researched|inferred)(?:\s+(\d{4}-\d{2}-\d{2})(?:\s+([^\]]+?))?)?\]", RegexOptions.Compiled)]
    private static partial Regex ProvenanceTagRegex();

    public static bool ValidateUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    public static IReadOnlyList<ParsedImportSection> ParseBytes(
        string sourceFileId,
        byte[] bytes,
        string defaultDate)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (!ValidateUtf8(bytes))
        {
            throw new DecoderFallbackException($"File '{sourceFileId}' contains invalid UTF-8 byte sequences.");
        }

        var markdown = StrictUtf8.GetString(bytes);
        return Parse(sourceFileId, markdown, defaultDate);
    }

    public static IReadOnlyList<ParsedImportSection> Parse(
        string sourceFileId,
        string markdown,
        string defaultDate)
    {
        ArgumentNullException.ThrowIfNull(sourceFileId);
        ArgumentNullException.ThrowIfNull(markdown);

        if (string.IsNullOrWhiteSpace(defaultDate) || !ProvenanceMarker.IsValidIsoDate(defaultDate))
        {
            throw new ArgumentException($"Default date '{defaultDate}' must be a valid ISO-8601 date in 'YYYY-MM-DD' format.", nameof(defaultDate));
        }

        var normalizedSourceId = sourceFileId.Trim();
        var lines = SplitLines(markdown);

        var headingLocations = FindSplitHeadings(markdown, lines);

        var sections = new List<ParsedImportSection>();
        var headingPathOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var bareHeadingCounter = 0;
        var sectionOrderIndex = 0;

        string? currentH1Path = null;

        if (headingLocations.Count == 0)
        {
            if (markdown.Length > 0)
            {
                var prov = ExtractSectionProvenance(markdown, lines, 0, lines.Count, defaultDate);
                var hash = Sha256Hasher.ComputeLowerHex(markdown);
                const string path = "$preamble";
                var importKey = $"{normalizedSourceId}#{path}#0";

                sections.Add(new ParsedImportSection(
                    SourceFileId: normalizedSourceId,
                    HeadingPath: path,
                    OccurrenceIndex: 0,
                    SectionOrderIndex: 0,
                    Title: "Preamble",
                    Body: markdown,
                    Provenance: prov,
                    BodyHash: hash,
                    ImportKey: importKey));
            }

            return sections;
        }

        // Check if there is preamble text before the first # or ## heading
        var firstHeadingStart = headingLocations[0].StartCharIndex;
        if (firstHeadingStart > 0)
        {
            var preambleBody = markdown[..firstHeadingStart];
            var firstHeadingLineIndex = headingLocations[0].LineIndex;
            var prov = ExtractSectionProvenance(markdown, lines, 0, firstHeadingLineIndex, defaultDate);
            var hash = Sha256Hasher.ComputeLowerHex(preambleBody);
            const string path = "$preamble";
            var importKey = $"{normalizedSourceId}#{path}#0";

            sections.Add(new ParsedImportSection(
                SourceFileId: normalizedSourceId,
                HeadingPath: path,
                OccurrenceIndex: 0,
                SectionOrderIndex: sectionOrderIndex++,
                Title: "Preamble",
                Body: preambleBody,
                Provenance: prov,
                BodyHash: hash,
                ImportKey: importKey));
        }

        for (var i = 0; i < headingLocations.Count; i++)
        {
            var current = headingLocations[i];
            var nextStart = (i + 1 < headingLocations.Count)
                ? headingLocations[i + 1].StartCharIndex
                : markdown.Length;

            var sectionBody = markdown[current.StartCharIndex..nextStart];
            var nextLineIndex = (i + 1 < headingLocations.Count)
                ? headingLocations[i + 1].LineIndex
                : lines.Count;

            var prov = ExtractSectionProvenance(markdown, lines, current.LineIndex, nextLineIndex, defaultDate);

            string title;
            if (string.IsNullOrWhiteSpace(current.RawTitle))
            {
                bareHeadingCounter++;
                title = $"(Untitled Heading #{bareHeadingCounter})";
            }
            else
            {
                title = current.RawTitle;
            }

            var markerTitle = string.IsNullOrWhiteSpace(current.RawTitle)
                ? new string('#', current.Level)
                : $"{new string('#', current.Level)} {current.RawTitle}";

            string headingPath;
            if (current.Level == 1)
            {
                headingPath = markerTitle;
                currentH1Path = headingPath;
            }
            else // Level == 2
            {
                headingPath = currentH1Path is not null
                    ? $"{currentH1Path} / {markerTitle}"
                    : markerTitle;
            }

            if (!headingPathOccurrences.TryGetValue(headingPath, out var count))
            {
                count = 0;
            }

            headingPathOccurrences[headingPath] = count + 1;
            var occurrenceIndex = count;

            var hash = Sha256Hasher.ComputeLowerHex(sectionBody);
            var importKey = $"{normalizedSourceId}#{headingPath}#{occurrenceIndex}";

            sections.Add(new ParsedImportSection(
                SourceFileId: normalizedSourceId,
                HeadingPath: headingPath,
                OccurrenceIndex: occurrenceIndex,
                SectionOrderIndex: sectionOrderIndex++,
                Title: title,
                Body: sectionBody,
                Provenance: prov,
                BodyHash: hash,
                ImportKey: importKey));
        }

        return sections;
    }

    private sealed record LineInfo(int LineIndex, int StartCharIndex, int EndCharIndex, string Text, bool InFrontMatter);

    private sealed record HeadingLocation(int LineIndex, int Level, string RawTitle, int StartCharIndex);

    private static List<LineInfo> SplitLines(string markdown)
    {
        var lines = new List<LineInfo>();
        var pos = 0;
        var len = markdown.Length;
        var lineIndex = 0;
        var inFrontMatter = false;

        while (pos < len)
        {
            var lineStart = pos;
            while (pos < len && markdown[pos] != '\r' && markdown[pos] != '\n')
            {
                pos++;
            }

            var contentEnd = pos;
            if (pos < len)
            {
                if (markdown[pos] == '\r' && pos + 1 < len && markdown[pos + 1] == '\n')
                {
                    pos += 2;
                }
                else
                {
                    pos += 1;
                }
            }

            var lineText = markdown[lineStart..contentEnd];

            if (lineIndex == 0 && lineText.Trim() == "---")
            {
                inFrontMatter = true;
                lines.Add(new LineInfo(lineIndex++, lineStart, pos, lineText, InFrontMatter: true));
                continue;
            }

            if (inFrontMatter)
            {
                lines.Add(new LineInfo(lineIndex++, lineStart, pos, lineText, InFrontMatter: true));
                if (lineText.Trim() == "---")
                {
                    inFrontMatter = false;
                }
                continue;
            }

            lines.Add(new LineInfo(lineIndex++, lineStart, pos, lineText, InFrontMatter: false));
        }

        return lines;
    }

    private static List<HeadingLocation> FindSplitHeadings(string markdown, List<LineInfo> lines)
    {
        var headings = new List<HeadingLocation>();
        var inFence = false;
        var fenceChar = '\0';
        var fenceLen = 0;

        foreach (var line in lines)
        {
            if (line.InFrontMatter)
            {
                continue;
            }

            var span = line.Text.AsSpan();
            if (MarkdownSectionParser.TryUpdateFenceState(span, ref inFence, ref fenceChar, ref fenceLen))
            {
                continue;
            }

            if (inFence)
            {
                continue;
            }

            // Indented code block: 4 or more leading spaces or tab
            if (IsIndentedCodeLine(span))
            {
                continue;
            }

            if (TryParseAtxHeading(span, out var level, out var title))
            {
                // We split ONLY on level 1 (#) and level 2 (##)
                if (level is 1 or 2)
                {
                    headings.Add(new HeadingLocation(line.LineIndex, level, title, line.StartCharIndex));
                }
            }
        }

        return headings;
    }

    private static bool IsIndentedCodeLine(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return false;
        }

        if (span[0] == '\t')
        {
            return true;
        }

        var spaces = 0;
        while (spaces < span.Length && span[spaces] == ' ')
        {
            spaces++;
        }

        return spaces >= 4;
    }

    private static bool TryParseAtxHeading(ReadOnlySpan<char> span, out int level, out string title)
    {
        level = 0;
        title = string.Empty;

        var spaces = 0;
        while (spaces < span.Length && spaces < 3 && span[spaces] == ' ')
        {
            spaces++;
        }

        var trimmedLeading = span[spaces..];
        if (trimmedLeading.IsEmpty || trimmedLeading[0] != '#')
        {
            return false;
        }

        var hashes = 0;
        while (hashes < trimmedLeading.Length && trimmedLeading[hashes] == '#')
        {
            hashes++;
        }

        if (hashes is < 1 or > 6)
        {
            return false;
        }

        var rest = trimmedLeading[hashes..];
        if (!rest.IsEmpty && rest[0] != ' ' && rest[0] != '\t')
        {
            return false;
        }

        level = hashes;

        var rawTitle = rest.Trim();
        // Check for optional closing sequence of # preceded by space/tab
        var titleStr = StripTrailingClosingHashes(rawTitle);
        title = titleStr;
        return true;
    }

    private static string StripTrailingClosingHashes(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
        {
            return string.Empty;
        }

        var end = span.Length;
        while (end > 0 && (span[end - 1] == ' ' || span[end - 1] == '\t'))
        {
            end--;
        }

        var trailingHashes = 0;
        while (end - trailingHashes > 0 && span[end - trailingHashes - 1] == '#')
        {
            trailingHashes++;
        }

        if (trailingHashes > 0 && end - trailingHashes > 0 && (span[end - trailingHashes - 1] == ' ' || span[end - trailingHashes - 1] == '\t'))
        {
            var beforeHashes = span[..(end - trailingHashes)].Trim();
            return beforeHashes.ToString();
        }

        return span.Trim().ToString();
    }

    private static ProvenanceMarker ExtractSectionProvenance(
        string markdown,
        List<LineInfo> lines,
        int startLineIndex,
        int endLineIndex,
        string defaultDate)
    {
        var inFence = false;
        var fenceChar = '\0';
        var fenceLen = 0;

        for (var i = startLineIndex; i < endLineIndex && i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.InFrontMatter)
            {
                continue;
            }

            var span = line.Text.AsSpan();
            if (MarkdownSectionParser.TryUpdateFenceState(span, ref inFence, ref fenceChar, ref fenceLen))
            {
                continue;
            }

            if (inFence)
            {
                continue;
            }

            var match = ProvenanceTagRegex().Match(line.Text);
            if (match.Success)
            {
                var kind = ProvenanceKindExtensions.FromWireString(match.Groups[1].Value);
                var date = match.Groups[2].Success && !string.IsNullOrWhiteSpace(match.Groups[2].Value)
                    ? match.Groups[2].Value.Trim()
                    : defaultDate;
                var qualifier = match.Groups[3].Success && !string.IsNullOrWhiteSpace(match.Groups[3].Value)
                    ? match.Groups[3].Value.Trim()
                    : null;

                return new ProvenanceMarker(kind, date, qualifier);
            }
        }

        return new ProvenanceMarker(ProvenanceKind.Inferred, defaultDate);
    }
}
