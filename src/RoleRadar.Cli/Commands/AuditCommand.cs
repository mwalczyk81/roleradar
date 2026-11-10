using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Audit.Queries;
using RoleRadar.Domain.Common;

namespace RoleRadar.Cli.Commands;

/// <summary>
/// <c>roleradar audit list</c> (feature 013, FR-020, ADR 0041): the owner's read-only view of the append-only audit log.
/// It is served by <see cref="ListAuditEntriesQuery"/>, writes nothing, and prints no appended text, before and after
/// values or content summaries. There is deliberately no MCP tool and no API endpoint for it.
/// </summary>
public static partial class AuditCommand
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private const int ExitValidation = 2;
    private const int ExitFailure = 1;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var auditCommand = new Command("audit", "Read the append-only audit log (read-only)");
        auditCommand.Add(CreateListCommand(services));
        return auditCommand;
    }

    // ISO 8601 with a designator: "Z" or a numeric offset. A timestamp with no offset is ambiguous, so it is rejected.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex Iso8601WithOffset();

    private static Command CreateListCommand(IServiceProvider services)
    {
        var cmd = new Command("list", "List audit entries for the owner, newest first");

        var entityTypeOption = new Option<string?>("--entity-type") { Description = "Only this entity type (note, company, posting, application, application_event, contact, criterion, search_run)" };
        var entityIdOption = new Option<string?>("--entity-id") { Description = "Only this entity id (exact match)" };
        var actorOption = new Option<string?>("--actor") { Description = "Only this actor (exact match), for example a subject, cli-user or mcp-stdio" };
        var fromOption = new Option<string?>("--from") { Description = "Earliest time, inclusive: ISO 8601 with Z or an offset" };
        var toOption = new Option<string?>("--to") { Description = "Latest time, exclusive: ISO 8601 with Z or an offset" };
        var limitOption = new Option<string?>("--limit") { Description = $"Entries per page, 1 to {ListAuditEntriesQuery.MaxLimit} (default {ListAuditEntriesQuery.DefaultLimit})" };
        var cursorOption = new Option<string?>("--cursor") { Description = "Paging token from the previous page's next cursor" };
        var fieldsOption = new Option<bool>("--fields") { Description = "Also print the names of the changed fields (never their values)" };
        var jsonOption = new Option<bool>("--json") { Description = "Emit structured JSON output" };

        cmd.Add(entityTypeOption);
        cmd.Add(entityIdOption);
        cmd.Add(actorOption);
        cmd.Add(fromOption);
        cmd.Add(toOption);
        cmd.Add(limitOption);
        cmd.Add(cursorOption);
        cmd.Add(fieldsOption);
        cmd.Add(jsonOption);

        cmd.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var json = parseResult.GetValue(jsonOption);
            var includeFields = parseResult.GetValue(fieldsOption);

            var parsed = ParseOptions(
                parseResult.GetValue(limitOption),
                parseResult.GetValue(fromOption),
                parseResult.GetValue(toOption));
            if (parsed.Error is { } optionError)
            {
                return WriteError(json, Classify(optionError.Code), optionError.Message);
            }

            var query = new ListAuditEntriesQuery(
                OwnerId.Default,
                EntityType: parseResult.GetValue(entityTypeOption),
                EntityId: parseResult.GetValue(entityIdOption),
                Actor: parseResult.GetValue(actorOption),
                FromUtc: parsed.FromUtc,
                ToUtc: parsed.ToUtc,
                Limit: parsed.Limit,
                Cursor: parseResult.GetValue(cursorOption),
                IncludeChangedFields: includeFields);

            Result<AuditListPage> result;
            try
            {
                using var scope = services.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<ListAuditEntriesQuery, AuditListPage>>();
                result = await handler.HandleAsync(query, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The type only: an exception message from the driver can carry connection details.
                return WriteError(json, InternalError, $"The audit log could not be read ({ex.GetType().Name}).");
            }

            if (!result.IsSuccess || result.Value is null)
            {
                var error = result.Error;
                return error is { } failure
                    ? WriteError(json, Classify(failure.Code), failure.Message)
                    : WriteError(json, InternalError, "The audit log could not be read.");
            }

            if (json)
            {
                WriteJson(result.Value, includeFields);
            }
            else
            {
                WriteText(result.Value, includeFields);
            }

            return 0;
        });

        return cmd;
    }

    private readonly record struct ParsedOptions(int Limit, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, ResultError? Error);

    private static ParsedOptions ParseOptions(string? rawLimit, string? rawFrom, string? rawTo)
    {
        var limit = ListAuditEntriesQuery.DefaultLimit;
        if (rawLimit is not null
            && !int.TryParse(rawLimit, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit))
        {
            return Rejected($"Limit must be a whole number between 1 and {ListAuditEntriesQuery.MaxLimit}.");
        }

        if (!TryParseTimestamp(rawFrom, out var from))
        {
            return Rejected("From must be an ISO 8601 timestamp with Z or an offset, for example 2026-11-09T14:03:22Z.");
        }

        if (!TryParseTimestamp(rawTo, out var to))
        {
            return Rejected("To must be an ISO 8601 timestamp with Z or an offset, for example 2026-11-09T14:03:22Z.");
        }

        return new ParsedOptions(limit, from, to, null);

        static ParsedOptions Rejected(string message) =>
            new(ListAuditEntriesQuery.DefaultLimit, null, null, new ResultError(ErrorCode.ValidationError, message));
    }

    private static bool TryParseTimestamp(string? raw, out DateTimeOffset? utc)
    {
        utc = null;
        if (raw is null)
        {
            return true;
        }

        if (!Iso8601WithOffset().IsMatch(raw)
            || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        utc = parsed.ToUniversalTime();
        return true;
    }

    private static void WriteText(AuditListPage page, bool includeFields)
    {
        foreach (var item in page.Items)
        {
            var line =
                $"{Printable(item.OccurredAtUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture))}  {Printable(item.EntityType)}  {Printable(item.EntityId)}  " +
                $"{Printable(item.Operation)}  {Printable(item.Actor)}  req={Printable(item.RequestCorrelationId)}";

            if (includeFields)
            {
                line += $"  fields={FieldsOf(item)}";
            }

            Console.WriteLine(line);
        }

        if (page.NextCursor is not null)
        {
            Console.WriteLine($"next: {Printable(page.NextCursor)}");
        }
    }

    private static string FieldsOf(AuditListItem item) =>
        item.ChangedFields is { Count: > 0 } fields ? string.Join(',', fields.Select(Printable)) : "-";

    private static void WriteJson(AuditListPage page, bool includeFields)
    {
        // JSON escapes control characters itself, so the stored values survive intact for a machine reader.
        var items = page.Items.Select(item =>
        {
            var entry = new Dictionary<string, object?>
            {
                ["occurredAt"] = item.OccurredAtUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture),
                ["entityType"] = item.EntityType,
                ["entityId"] = item.EntityId,
                ["operation"] = item.Operation,
                ["actor"] = item.Actor,
                ["requestCorrelationId"] = item.RequestCorrelationId,
            };

            if (includeFields)
            {
                entry["changedFields"] = item.ChangedFields ?? [];
            }

            return entry;
        }).ToList();

        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, data = new { items, nextCursor = page.NextCursor } }, JsonOpts));
    }

    private readonly record struct Failure(string Code, int ExitCode);

    private static readonly Failure ValidationFailure = new("validation_error", ExitValidation);
    private static readonly Failure UnavailableFailure = new("service_unavailable", ExitFailure);
    private static readonly Failure InternalError = new("internal_error", ExitFailure);

    private static Failure Classify(ErrorCode code) => code switch
    {
        ErrorCode.ValidationError => ValidationFailure,
        ErrorCode.DatastoreOffline => UnavailableFailure,
        _ => InternalError,
    };

    private static int WriteError(bool json, Failure failure, string message)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ok = false, error = new { code = failure.Code, message } }, JsonOpts));
        }
        else
        {
            Console.Error.WriteLine($"Error: {Printable(message)}");
        }

        return failure.ExitCode;
    }

    /// <summary>
    /// Text output is read in a terminal, so a stored value must not be able to move the cursor, colour the screen or
    /// start a second line or reorder it: every control character, the Unicode line and paragraph separators and every
    /// format character (bidirectional overrides, zero-width characters) becomes "?".
    /// An empty value prints as "-", so the columns stay countable.
    /// </summary>
    public static string Printable(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                span[i] = IsUnsafe(source[i]) ? '?' : source[i];
            }
        });
    }

    private static bool IsUnsafe(char c) =>
        char.IsControl(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.LineSeparator
            or UnicodeCategory.ParagraphSeparator
            // Format characters (Cf) draw nothing but change what is read: U+202E reverses the text after it, U+200B hides.
            or UnicodeCategory.Format;
}
