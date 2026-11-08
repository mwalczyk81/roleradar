using System.Text;
using System.Text.RegularExpressions;
using RoleRadar.Infrastructure.Csv;
using RoleRadar.Tests.Unit.Support;
using Xunit;

namespace RoleRadar.Tests.Unit.Csv;

/// <summary>
/// Properties of <see cref="Rfc4180CsvReader"/> (feature 011, story 3). Contract read from the reader: it is lenient and
/// never fails. An unterminated quote runs to end of input, a blank line is a record with one empty field, a leading BOM
/// is dropped, and every record carries the physical line number where it started. All data is fictional and generated.
/// </summary>
public sealed class CsvReaderPropertyTests
{
    private static readonly string[] Alphabet =
    [
        "a", "Z", "7", " ", ",", "\"", "\r", "\n", "\r\n", "é", "日", "\U0001F600", "role", "acme"
    ];

    private static readonly bool[] HeaderModes = [true, false];

    // ---- generated documents ----

    /// <summary>A generated table plus the way it was written to text.</summary>
    private sealed record Document(IReadOnlyList<string[]> Rows, string Terminator, bool QuoteEverything, string Text, IReadOnlyList<int> RowStartOffsets);

    private static Document Build(int[] codes)
    {
        var terminator = (Math.Abs(codes.Length > 0 ? codes[0] : 0) % 3) switch { 0 => "\n", 1 => "\r\n", _ => "\r" };
        var quoteEverything = codes.Length > 1 && Math.Abs(codes[1]) % 2 == 0;

        var rows = new List<string[]>();
        var fields = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in codes.Skip(2).Take(80))
        {
            var pick = Math.Abs(raw % (Alphabet.Length + 2));
            if (pick == Alphabet.Length)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else if (pick == Alphabet.Length + 1)
            {
                fields.Add(current.ToString());
                current.Clear();
                rows.Add(fields.ToArray());
                fields.Clear();
            }
            else
            {
                current.Append(Alphabet[pick]);
            }
        }

        fields.Add(current.ToString());
        rows.Add(fields.ToArray());

        var text = new StringBuilder();
        var offsets = new List<int>();
        foreach (var row in rows)
        {
            offsets.Add(text.Length);
            text.Append(string.Join(',', row.Select(f => Encode(f, quoteEverything))));
            text.Append(terminator);
        }

        return new Document(rows, terminator, quoteEverything, text.ToString(), offsets);
    }

    private static string Encode(string field, bool quoteEverything)
    {
        var needsQuotes = quoteEverything || field.Length == 0 || field.AsSpan().IndexOfAny(",\"\r\n") >= 0;
        return needsQuotes ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;
    }

    private static List<CsvRow> ReadAll(string text, bool hasHeader = false)
        => Rfc4180CsvReader.Read(new StringReader(text), hasHeader).ToList();

    // ---- properties ----

    [Fact]
    public void Property_RoundTrip_ReadingWhatWasWrittenGivesBackTheSameRows()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(Alphabet.Length + 1), codes =>
        {
            var document = Build(codes);
            var rows = ReadAll(document.Text);

            if (rows.Count != document.Rows.Count)
            {
                return false;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Fields.SequenceEqual(document.Rows[i], StringComparer.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }));
    }

    [Fact]
    public void Property_LineNumber_IsOnePlusPhysicalLineBreaksBeforeTheRecord()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(Alphabet.Length + 1), codes =>
        {
            var document = Build(codes);
            var rows = ReadAll(document.Text);
            if (rows.Count != document.Rows.Count)
            {
                return false;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var before = document.Text[..document.RowStartOffsets[i]];
                var expected = 1 + Regex.Count(before, "\r\n|\r|\n");
                if (rows[i].LineNumber != expected)
                {
                    return false;
                }
            }

            return true;
        }));
    }

    [Fact]
    public void Property_ArbitraryText_NeverThrows_AndEveryRecordHasAFieldAndAPositiveIncreasingLine()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll<string>(raw =>
        {
            var text = raw ?? string.Empty;
            foreach (var hasHeader in HeaderModes)
            {
                var rows = ReadAll(text, hasHeader);
                var previousLine = 0;
                foreach (var row in rows)
                {
                    if (row.FieldCount < 1 || row.LineNumber < 1 || row.LineNumber < previousLine)
                    {
                        return false;
                    }

                    previousLine = row.LineNumber;
                }
            }

            return true;
        }));
    }

    [Fact]
    public void Property_ArbitraryBytes_NeverThrow_AndHeaderReadMatchesTheFirstRecord()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll<byte[]>(raw =>
        {
            var bytes = raw ?? [];
            string Decode()
            {
                using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }

            var text = Decode();
            var header = Rfc4180CsvReader.ReadHeader(new StringReader(text));
            var records = ReadAll(text);
            if (records.Count == 0)
            {
                return header.Count == 0;
            }

            return header.SequenceEqual(records[0].Fields.Select(f => f.Trim()), StringComparer.Ordinal);
        }));
    }

    [Fact]
    public void Property_AnUnterminatedQuote_StillYieldsExactlyOneFinalRecordHoldingTheRest()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(Alphabet.Length + 1), codes =>
        {
            var body = string.Concat(codes.Take(40).Select(c => Alphabet[Math.Abs(c % Alphabet.Length)]));
            var text = "a,b\n\"" + body.Replace("\"", string.Empty, StringComparison.Ordinal);
            var rows = ReadAll(text, hasHeader: false);

            return rows.Count == 2
                && rows[1].FieldCount == 1
                && string.Equals(rows[1].Fields[0], body.Replace("\"", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }));
    }
}
