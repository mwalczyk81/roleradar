using System.Buffers.Text;
using System.Globalization;
using System.Text;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application.Audit.Queries;

/// <summary>Lists one owner's audit entries, newest first (feature 013, FR-020). Read-only: it writes nothing.</summary>
public sealed record ListAuditEntriesQuery(
    OwnerId OwnerId,
    string? EntityType = null,
    string? EntityId = null,
    string? Actor = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int Limit = ListAuditEntriesQuery.DefaultLimit,
    string? Cursor = null,
    bool IncludeChangedFields = false) : IQuery<AuditListPage>
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;
    public const int MaxEntityIdLength = 128;
    public const int MaxActorLength = 256;
}

/// <summary>
/// One audit entry as the listing shows it. There is deliberately no member for appended text, before or after values or
/// content summaries, so none of them can be carried, whatever the store returns.
/// </summary>
public sealed record AuditListItem(
    DateTime OccurredAtUtc,
    string EntityType,
    string EntityId,
    string Operation,
    string Actor,
    string? RequestCorrelationId,
    IReadOnlyList<string>? ChangedFields);

/// <summary>One page of the listing. <see cref="NextCursor"/> is null on the last page.</summary>
public sealed record AuditListPage(IReadOnlyList<AuditListItem> Items, string? NextCursor);

/// <summary>The entity types that tracking and note writes put in <c>audit_log.entity_type</c>.</summary>
public static class AuditEntityTypes
{
    private static readonly string[] KnownTypes =
        ["note", "company", "posting", "application", "application_event", "contact", "criterion", "search_run"];

    public static IReadOnlyList<string> Known => KnownTypes;

    public static bool IsKnown(string? entityType) =>
        entityType is not null && Array.Exists(KnownTypes, known => string.Equals(known, entityType, StringComparison.Ordinal));
}

/// <summary>
/// The opaque paging token: the (occurred_at, _id) of the last entry on a page. Keyset paging on that pair stays stable
/// when entries are appended between pages. The token is URL-safe base64 of <c>v1:{unix milliseconds}:{id}</c>.
/// </summary>
public static class AuditCursorCodec
{
    private const string Prefix = "v1:";
    private const long MinUnixMilliseconds = -62135596800000;
    private const long MaxUnixMilliseconds = 253402300799999;

    public static string Encode(DateTime occurredAtUtc, Guid id)
    {
        var utc = occurredAtUtc.Kind == DateTimeKind.Utc ? occurredAtUtc : DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc);
        var millis = new DateTimeOffset(utc).ToUnixTimeMilliseconds();
        var payload = string.Create(CultureInfo.InvariantCulture, $"{Prefix}{millis}:{id:N}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    public static bool TryDecode(string? cursor, out DateTime occurredAtUtc, out Guid id)
    {
        occurredAtUtc = default;
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        if (!payload.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = payload[Prefix.Length..].Split(':');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var millis)
            || millis is < MinUnixMilliseconds or > MaxUnixMilliseconds
            || !Guid.TryParseExact(parts[1], "N", out var parsedId)
            || parsedId == Guid.Empty)
        {
            return false;
        }

        occurredAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;
        id = parsedId;
        return true;
    }
}

/// <summary>The validation rules of data-model.md "ListAuditEntriesQuery". A failure is a result, never an exception.</summary>
public static class ListAuditEntriesQueryValidator
{
    public static ResultError? Validate(ListAuditEntriesQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (string.IsNullOrWhiteSpace(query.OwnerId.Value))
        {
            return Invalid("An owner is required.");
        }

        if (query.Limit is < 1 or > ListAuditEntriesQuery.MaxLimit)
        {
            return Invalid($"Limit must be between 1 and {ListAuditEntriesQuery.MaxLimit}.");
        }

        if (query.EntityType is not null && !AuditEntityTypes.IsKnown(query.EntityType))
        {
            return Invalid($"Entity type must be one of: {string.Join(", ", AuditEntityTypes.Known)}.");
        }

        if (query.EntityId is not null
            && (string.IsNullOrWhiteSpace(query.EntityId) || query.EntityId.Length > ListAuditEntriesQuery.MaxEntityIdLength))
        {
            return Invalid($"Entity id must be 1 to {ListAuditEntriesQuery.MaxEntityIdLength} characters.");
        }

        if (query.Actor is not null
            && (string.IsNullOrWhiteSpace(query.Actor) || query.Actor.Length > ListAuditEntriesQuery.MaxActorLength))
        {
            return Invalid($"Actor must be 1 to {ListAuditEntriesQuery.MaxActorLength} characters.");
        }

        if (query.FromUtc is { } from && query.ToUtc is { } to && from >= to)
        {
            return Invalid("From must be earlier than to.");
        }

        if (query.Cursor is not null && !AuditCursorCodec.TryDecode(query.Cursor, out _, out _))
        {
            return Invalid("Cursor is not a valid paging token.");
        }

        return null;
    }

    private static ResultError Invalid(string message) => new(ErrorCode.ValidationError, message);
}

/// <summary>
/// Read-only handler for <c>roleradar audit list</c> (ADR 0041). It validates, then asks <see cref="IAuditLogReader"/>.
/// It is a query, so the transaction and audit decorator does not wrap it: it writes nothing.
/// </summary>
public sealed class ListAuditEntriesQueryHandler(IAuditLogReader reader) : IQueryHandler<ListAuditEntriesQuery, AuditListPage>
{
    private readonly IAuditLogReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public async Task<Result<AuditListPage>> HandleAsync(ListAuditEntriesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var invalid = ListAuditEntriesQueryValidator.Validate(query);
        if (invalid is not null)
        {
            return Result<AuditListPage>.Failure(invalid.Value);
        }

        return await _reader.ListAsync(query, cancellationToken);
    }
}
