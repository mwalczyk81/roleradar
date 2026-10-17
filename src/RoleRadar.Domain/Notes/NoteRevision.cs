using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Notes;

public sealed class NoteRevision
{
    public const string ReasonReplaceSection = "replace-section";
    public const string ReasonImportOverwrite = "import-overwrite";

    public Guid Id { get; }

    public string OwnerId { get; }

    public Guid NoteId { get; }

    public int RevisionNumber { get; }

    public string Reason { get; }

    public string PriorBody { get; }

    public string PriorBodyHash { get; }

    public Guid PriorVersionToken { get; }

    public DateTime CreatedAtUtc { get; }

    private NoteRevision(
        Guid id,
        string ownerId,
        Guid noteId,
        int revisionNumber,
        string reason,
        string priorBody,
        string priorBodyHash,
        Guid priorVersionToken,
        DateTime createdAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        NoteId = noteId;
        RevisionNumber = revisionNumber;
        Reason = reason;
        PriorBody = priorBody;
        PriorBodyHash = priorBodyHash;
        PriorVersionToken = priorVersionToken;
        CreatedAtUtc = EnsureUtc(createdAtUtc);
    }

    public static NoteRevision Create(
        string ownerId,
        Guid noteId,
        int revisionNumber,
        string reason,
        string priorBody,
        Guid priorVersionToken,
        DateTime createdAtUtc)
    {
        var validOwner = new OwnerId(ownerId).Value;
        if (noteId == Guid.Empty)
        {
            throw new ArgumentException("Note identifier cannot be empty.", nameof(noteId));
        }

        if (revisionNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(revisionNumber), "Revision number must be >= 1.");
        }

        if (reason is not (ReasonReplaceSection or ReasonImportOverwrite))
        {
            throw new ArgumentException(
                $"Revision reason must be '{ReasonReplaceSection}' or '{ReasonImportOverwrite}'.",
                nameof(reason));
        }

        ArgumentNullException.ThrowIfNull(priorBody);
        if (priorVersionToken == Guid.Empty)
        {
            throw new ArgumentException("Prior version token cannot be empty.", nameof(priorVersionToken));
        }

        var priorBodyHash = Sha256Hasher.ComputeLowerHex(priorBody);

        return new NoteRevision(
            Guid.CreateVersion7(),
            validOwner,
            noteId,
            revisionNumber,
            reason,
            priorBody,
            priorBodyHash,
            priorVersionToken,
            EnsureUtc(createdAtUtc));
    }

    public static NoteRevision Rehydrate(
        Guid id,
        string ownerId,
        Guid noteId,
        int revisionNumber,
        string reason,
        string priorBody,
        string priorBodyHash,
        Guid priorVersionToken,
        DateTime createdAtUtc) =>
        new(id, ownerId, noteId, revisionNumber, reason, priorBody, priorBodyHash, priorVersionToken, createdAtUtc);

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
