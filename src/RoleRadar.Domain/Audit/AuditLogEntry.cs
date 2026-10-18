using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Audit;

public sealed class AuditLogEntry
{
    public Guid Id { get; }

    public string OwnerId { get; }

    public string Actor { get; }

    public string Operation { get; }

    public string EntityType { get; }

    public Guid EntityId { get; }

    public Guid TargetNoteId { get; }

    public Guid? NoteRevisionId { get; }

    public string? AppendedText { get; }

    public Guid ResultingVersionToken { get; }

    public string? ResolvedDefaultDate { get; }

    public DateTime OccurredAtUtc { get; }

    public AuditContentSummary? BeforeSummary { get; }

    public AuditContentSummary? AfterSummary { get; }

    public AuditDetails? Details { get; }

    private AuditLogEntry(
        Guid id,
        string ownerId,
        string actor,
        string operation,
        string entityType,
        Guid entityId,
        Guid targetNoteId,
        Guid? noteRevisionId,
        string? appendedText,
        Guid resultingVersionToken,
        string? resolvedDefaultDate,
        DateTime occurredAtUtc,
        AuditContentSummary? beforeSummary,
        AuditContentSummary? afterSummary,
        AuditDetails? details)
    {
        Id = id;
        OwnerId = ownerId;
        Actor = actor;
        Operation = operation;
        EntityType = entityType;
        EntityId = entityId;
        TargetNoteId = targetNoteId;
        NoteRevisionId = noteRevisionId;
        AppendedText = appendedText;
        ResultingVersionToken = resultingVersionToken;
        ResolvedDefaultDate = resolvedDefaultDate;
        OccurredAtUtc = EnsureUtc(occurredAtUtc);
        BeforeSummary = beforeSummary;
        AfterSummary = afterSummary;
        Details = details;
    }

    public static AuditLogEntry Create(
        string ownerId,
        string actor,
        string operation,
        Guid targetNoteId,
        Guid? noteRevisionId,
        string? appendedText,
        Guid resultingVersionToken,
        string? resolvedDefaultDate,
        DateTime occurredAtUtc,
        AuditContentSummary? beforeSummary,
        AuditContentSummary afterSummary)
    {
        var validOwner = new OwnerId(ownerId).Value;
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor identity is required.", nameof(actor));
        }

        if (!AuditOperation.IsValid(operation))
        {
            throw new ArgumentException($"Invalid audit operation '{operation}'.", nameof(operation));
        }

        if (targetNoteId == Guid.Empty)
        {
            throw new ArgumentException("Target note identifier cannot be empty.", nameof(targetNoteId));
        }

        if (resultingVersionToken == Guid.Empty)
        {
            throw new ArgumentException("Resulting version token cannot be empty.", nameof(resultingVersionToken));
        }

        ArgumentNullException.ThrowIfNull(afterSummary);

        var isDestructiveOperation = operation is AuditOperation.NoteReplaceSection or AuditOperation.ImportOverwrite;
        if (isDestructiveOperation && (noteRevisionId is null || noteRevisionId == Guid.Empty))
        {
            throw new ArgumentException($"Operation '{operation}' requires a non-empty NoteRevisionId.", nameof(noteRevisionId));
        }

        if (!isDestructiveOperation && noteRevisionId is not null)
        {
            throw new ArgumentException($"Operation '{operation}' must not specify a NoteRevisionId.", nameof(noteRevisionId));
        }

        var isAppendOperation = operation is AuditOperation.NoteAppend or AuditOperation.ContactAdd or AuditOperation.ContactRedact;
        if (isAppendOperation && string.IsNullOrEmpty(appendedText))
        {
            throw new ArgumentException($"Operation '{operation}' requires non-empty AppendedText.", nameof(appendedText));
        }

        if (!isAppendOperation && appendedText is not null)
        {
            throw new ArgumentException($"Operation '{operation}' must not specify AppendedText.", nameof(appendedText));
        }

        var isImportOperation = operation is AuditOperation.ImportCreate or AuditOperation.ImportOverwrite;
        if (isImportOperation && string.IsNullOrWhiteSpace(resolvedDefaultDate))
        {
            throw new ArgumentException($"Operation '{operation}' requires ResolvedDefaultDate.", nameof(resolvedDefaultDate));
        }

        if (!isImportOperation && resolvedDefaultDate is not null)
        {
            throw new ArgumentException($"Operation '{operation}' must not specify ResolvedDefaultDate.", nameof(resolvedDefaultDate));
        }

        var isCreateOperation = operation is AuditOperation.NoteCreate or AuditOperation.ImportCreate or AuditOperation.ContactAdd;
        if (isCreateOperation && beforeSummary is not null)
        {
            throw new ArgumentException($"Operation '{operation}' must have null BeforeSummary.", nameof(beforeSummary));
        }

        if (!isCreateOperation && beforeSummary is null)
        {
            throw new ArgumentException($"Operation '{operation}' requires BeforeSummary.", nameof(beforeSummary));
        }

        return new AuditLogEntry(
            id: Guid.CreateVersion7(),
            ownerId: validOwner,
            actor: actor.Trim(),
            operation: operation,
            entityType: "note",
            entityId: targetNoteId,
            targetNoteId: targetNoteId,
            noteRevisionId: noteRevisionId,
            appendedText: appendedText,
            resultingVersionToken: resultingVersionToken,
            resolvedDefaultDate: resolvedDefaultDate?.Trim(),
            occurredAtUtc: EnsureUtc(occurredAtUtc),
            beforeSummary: beforeSummary,
            afterSummary: afterSummary,
            details: null);
    }

    public static AuditLogEntry CreateForTracking(
        string ownerId,
        string actor,
        string operation,
        string entityType,
        Guid entityId,
        Guid resultingVersionToken,
        DateTime occurredAtUtc,
        AuditDetails details)
    {
        var validOwner = new OwnerId(ownerId).Value;
        if (string.IsNullOrWhiteSpace(actor))
        {
            throw new ArgumentException("Actor identity is required.", nameof(actor));
        }

        if (!AuditOperation.IsValid(operation))
        {
            throw new ArgumentException($"Invalid audit operation '{operation}'.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("EntityType is required.", nameof(entityType));
        }

        if (entityId == Guid.Empty)
        {
            throw new ArgumentException("Entity identifier cannot be empty.", nameof(entityId));
        }

        if (resultingVersionToken == Guid.Empty)
        {
            throw new ArgumentException("Resulting version token cannot be empty.", nameof(resultingVersionToken));
        }

        ArgumentNullException.ThrowIfNull(details);

        return new AuditLogEntry(
            id: Guid.CreateVersion7(),
            ownerId: validOwner,
            actor: actor.Trim(),
            operation: operation,
            entityType: entityType.Trim().ToLowerInvariant(),
            entityId: entityId,
            targetNoteId: Guid.Empty,
            noteRevisionId: null,
            appendedText: null,
            resultingVersionToken: resultingVersionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: EnsureUtc(occurredAtUtc),
            beforeSummary: null,
            afterSummary: null,
            details: details);
    }

    public static AuditLogEntry CreateForCreate(
        string ownerId,
        string actor,
        Guid targetNoteId,
        Guid resultingVersionToken,
        DateTime occurredAtUtc,
        AuditContentSummary afterSummary) =>
        Create(ownerId, actor, AuditOperation.NoteCreate, targetNoteId, null, null, resultingVersionToken, null, occurredAtUtc, null, afterSummary);

    public static AuditLogEntry CreateForAppend(
        string ownerId,
        string actor,
        Guid targetNoteId,
        string appendedText,
        Guid resultingVersionToken,
        DateTime occurredAtUtc,
        AuditContentSummary beforeSummary,
        AuditContentSummary afterSummary) =>
        Create(ownerId, actor, AuditOperation.NoteAppend, targetNoteId, null, appendedText, resultingVersionToken, null, occurredAtUtc, beforeSummary, afterSummary);

    public static AuditLogEntry CreateForReplaceSection(
        string ownerId,
        string actor,
        Guid targetNoteId,
        Guid noteRevisionId,
        Guid resultingVersionToken,
        DateTime occurredAtUtc,
        AuditContentSummary beforeSummary,
        AuditContentSummary afterSummary) =>
        Create(ownerId, actor, AuditOperation.NoteReplaceSection, targetNoteId, noteRevisionId, null, resultingVersionToken, null, occurredAtUtc, beforeSummary, afterSummary);

    public static AuditLogEntry CreateForArchive(
        string ownerId,
        string actor,
        Guid targetNoteId,
        Guid resultingVersionToken,
        DateTime occurredAtUtc,
        AuditContentSummary beforeSummary,
        AuditContentSummary afterSummary) =>
        Create(ownerId, actor, AuditOperation.NoteArchive, targetNoteId, null, null, resultingVersionToken, null, occurredAtUtc, beforeSummary, afterSummary);

    public static AuditLogEntry CreateForImportCreate(
        string ownerId,
        string actor,
        Guid targetNoteId,
        Guid resultingVersionToken,
        string resolvedDefaultDate,
        DateTime occurredAtUtc,
        AuditContentSummary afterSummary) =>
        Create(ownerId, actor, AuditOperation.ImportCreate, targetNoteId, null, null, resultingVersionToken, resolvedDefaultDate, occurredAtUtc, null, afterSummary);

    public static AuditLogEntry CreateForImportOverwrite(
        string ownerId,
        string actor,
        Guid targetNoteId,
        Guid noteRevisionId,
        Guid resultingVersionToken,
        string resolvedDefaultDate,
        DateTime occurredAtUtc,
        AuditContentSummary beforeSummary,
        AuditContentSummary afterSummary) =>
        Create(ownerId, actor, AuditOperation.ImportOverwrite, targetNoteId, noteRevisionId, null, resultingVersionToken, resolvedDefaultDate, occurredAtUtc, beforeSummary, afterSummary);

    public static AuditLogEntry Rehydrate(
        Guid id,
        string ownerId,
        string actor,
        string operation,
        Guid targetNoteId,
        Guid? noteRevisionId,
        string? appendedText,
        Guid resultingVersionToken,
        string? resolvedDefaultDate,
        DateTime occurredAtUtc,
        AuditContentSummary? beforeSummary,
        AuditContentSummary? afterSummary,
        string? entityType = null,
        Guid? entityId = null,
        AuditDetails? details = null) =>
        new(
            id,
            ownerId,
            actor,
            operation,
            entityType ?? "note",
            entityId ?? (targetNoteId != Guid.Empty ? targetNoteId : id),
            targetNoteId,
            noteRevisionId,
            appendedText,
            resultingVersionToken,
            resolvedDefaultDate,
            occurredAtUtc,
            beforeSummary,
            afterSummary,
            details);

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
