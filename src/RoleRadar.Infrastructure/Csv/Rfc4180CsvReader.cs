using System.Runtime.CompilerServices;
using System.Text;

namespace RoleRadar.Infrastructure.Csv;

/// <summary>
/// Represents a single parsed row in an RFC 4180 compliant CSV stream.
/// </summary>
public sealed class CsvRow
{
    private readonly IReadOnlyDictionary<string, int>? _headerIndex;

    public CsvRow(int lineNumber, IReadOnlyList<string> fields, IReadOnlyDictionary<string, int>? headerIndex = null)
    {
        LineNumber = lineNumber;
        Fields = fields;
        _headerIndex = headerIndex;
    }

    /// <summary>
    /// The physical 1-based line number in the source file where this record started.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Parsed fields for this record.
    /// </summary>
    public IReadOnlyList<string> Fields { get; }

    /// <summary>
    /// The number of fields in this record.
    /// </summary>
    public int FieldCount => Fields.Count;

    /// <summary>
    /// Retrieves a field value by zero-based column index, or null if index is out of range.
    /// </summary>
    public string? this[int index] => index >= 0 && index < Fields.Count ? Fields[index] : null;

    /// <summary>
    /// Retrieves a field value by column header name, or null if the header is not found or the row is ragged.
    /// </summary>
    public string? this[string headerName] => TryGet(headerName, out var value) ? value : null;

    /// <summary>
    /// Attempts to retrieve a field value by column header name.
    /// </summary>
    public bool TryGet(string headerName, out string? value)
    {
        if (_headerIndex is not null &&
            _headerIndex.TryGetValue(headerName, out var index) &&
            index >= 0 &&
            index < Fields.Count)
        {
            value = Fields[index];
            return true;
        }

        value = null;
        return false;
    }
}

/// <summary>
/// Hand-rolled RFC 4180 compliant CSV reader supporting streaming, UTF-8 BOM, multiline quoted fields,
/// embedded quotes, CRLF/LF line endings, and exact physical line-number tracking.
/// </summary>
public static class Rfc4180CsvReader
{
    public static IEnumerable<CsvRow> Read(TextReader reader, bool hasHeader = true)
    {
        Dictionary<string, int>? headerIndex = null;
        var isFirstRecord = true;

        foreach (var (startLine, fields) in ParseRecords(reader))
        {
            if (isFirstRecord)
            {
                isFirstRecord = false;
                if (hasHeader)
                {
                    headerIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < fields.Count; i++)
                    {
                        var colName = fields[i].Trim();
                        if (!headerIndex.ContainsKey(colName))
                        {
                            headerIndex[colName] = i;
                        }
                    }

                    continue;
                }
            }

            yield return new CsvRow(startLine, fields, headerIndex);
        }
    }

    public static async IAsyncEnumerable<CsvRow> ReadAsync(
        TextReader reader,
        bool hasHeader = true,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // For small-to-medium files (~hundreds of rows), synchronous iteration over TextReader is non-blocking in memory,
        // but providing IAsyncEnumerable satisfies streaming pipeline abstractions.
        foreach (var row in Read(reader, hasHeader))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
            await Task.Yield();
        }
    }

    private static IEnumerable<(int StartLine, List<string> Fields)> ParseRecords(TextReader reader)
    {
        var currentLineNumber = 1;
        var recordStartLine = 1;
        var fields = new List<string>();
        var currentField = new StringBuilder();

        var inQuotes = false;
        var inField = false;
        var firstCharInFile = true;

        while (true)
        {
            var raw = reader.Read();
            if (raw == -1)
            {
                // End of file
                if (inField || fields.Count > 0)
                {
                    fields.Add(currentField.ToString());
                    yield return (recordStartLine, fields);
                }
                break;
            }

            var ch = (char)raw;

            // Strip UTF-8 BOM if present on the very first character
            if (firstCharInFile)
            {
                firstCharInFile = false;
                if (ch == '\uFEFF')
                {
                    continue;
                }
            }

            if (inQuotes)
            {
                if (ch == '"')
                {
                    var next = reader.Peek();
                    if (next == '"')
                    {
                        // Escaped quote: "" -> "
                        reader.Read();
                        currentField.Append('"');
                    }
                    else
                    {
                        // End of quoted section
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(ch);
                    if (ch == '\r')
                    {
                        if (reader.Peek() == '\n')
                        {
                            reader.Read();
                            currentField.Append('\n');
                        }
                        currentLineNumber++;
                    }
                    else if (ch == '\n')
                    {
                        currentLineNumber++;
                    }
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                    inField = true;
                }
                else if (ch == ',')
                {
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                    inField = false;
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && reader.Peek() == '\n')
                    {
                        reader.Read();
                    }

                    // End of record
                    fields.Add(currentField.ToString());
                    currentField.Clear();
                    yield return (recordStartLine, fields);

                    fields = new List<string>();
                    inField = false;
                    currentLineNumber++;
                    recordStartLine = currentLineNumber;
                }
                else
                {
                    inField = true;
                    currentField.Append(ch);
                }
            }
        }
    }
}
