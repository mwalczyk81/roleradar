using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Notes;

public sealed class Note
{
    public const int MaxBodyBytes = 4 * 1024 * 1024; // Supports >=1 MB bodies comfortably up to 4 MiB

    public Guid Id { get; }

    public string OwnerId { get; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    public string CurrentBodyHash { get; private set; }

    public IReadOnlyList<string> Tags { get; private set; }

    public string Area { get; private set; }

    public ProvenanceMarker Provenance { get; private set; }

    public NoteImportMetadata? ImportMetadata { get; private set; }

    public NoteStatus Status { get; private set; }

    public Guid VersionToken { get; private set; }

    public int RevisionCount { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Detects whether an imported note's body has been modified since its last import write.
    /// Compares CurrentBodyHash against ImportMetadata.LastImportedBodyHash only (excluding Provenance.Date).
    /// </summary>
    public bool HasPostImportEdits =>
        ImportMetadata is not null &&
        !string.Equals(CurrentBodyHash, ImportMetadata.LastImportedBodyHash, StringComparison.Ordinal);

    private Note(
        Guid id,
        string ownerId,
        string title,
        string body,
        string currentBodyHash,
        IReadOnlyList<string> tags,
        string area,
        ProvenanceMarker provenance,
        NoteImportMetadata? importMetadata,
        NoteStatus status,
        Guid versionToken,
        int revisionCount,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        Id = id;
        OwnerId = ownerId;
        Title = title;
        Body = body;
        CurrentBodyHash = currentBodyHash;
        Tags = tags;
        Area = area;
        Provenance = provenance;
        ImportMetadata = importMetadata;
        Status = status;
        VersionToken = versionToken;
        RevisionCount = revisionCount;
        CreatedAtUtc = EnsureUtc(createdAtUtc);
        UpdatedAtUtc = EnsureUtc(updatedAtUtc);
    }

    public static Note Create(
        string ownerId,
        string title,
        string body,
        IEnumerable<string>? tags,
        string area,
        ProvenanceMarker provenance,
        DateTime createdAtUtc)
    {
        var validOwner = new OwnerId(ownerId).Value;
        var normalizedTitle = NormalizeTitle(title);
        ValidateBody(body);
        var normalizedTags = NormalizeTags(tags);
        var normalizedArea = NormalizeArea(area);
        ArgumentNullException.ThrowIfNull(provenance);
        var utcNow = EnsureUtc(createdAtUtc);

        return new Note(
            id: Guid.CreateVersion7(),
            ownerId: validOwner,
            title: normalizedTitle,
            body: body,
            currentBodyHash: Sha256Hasher.ComputeLowerHex(body),
            tags: normalizedTags,
            area: normalizedArea,
            provenance: provenance,
            importMetadata: null,
            status: NoteStatus.Active,
            versionToken: Guid.CreateVersion7(),
            revisionCount: 0,
            createdAtUtc: utcNow,
            updatedAtUtc: utcNow);
    }

    public static Note CreateImported(
        string ownerId,
        string title,
        string body,
        IEnumerable<string>? tags,
        string area,
        ProvenanceMarker provenance,
        NoteImportMetadata importMetadata,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(importMetadata);
        var validOwner = new OwnerId(ownerId).Value;
        var normalizedTitle = NormalizeTitle(title);
        ValidateBody(body);
        var bodyHash = Sha256Hasher.ComputeLowerHex(body);
        if (!string.Equals(bodyHash, importMetadata.LastImportedBodyHash, StringComparison.Ordinal))
        {
            throw new ArgumentException("ImportMetadata.LastImportedBodyHash must match the SHA-256 hash of body.", nameof(importMetadata));
        }

        var normalizedTags = NormalizeTags(tags);
        var normalizedArea = NormalizeArea(area);
        ArgumentNullException.ThrowIfNull(provenance);
        var utcNow = EnsureUtc(createdAtUtc);

        return new Note(
            id: Guid.CreateVersion7(),
            ownerId: validOwner,
            title: normalizedTitle,
            body: body,
            currentBodyHash: bodyHash,
            tags: normalizedTags,
            area: normalizedArea,
            provenance: provenance,
            importMetadata: importMetadata,
            status: NoteStatus.Active,
            versionToken: Guid.CreateVersion7(),
            revisionCount: 0,
            createdAtUtc: utcNow,
            updatedAtUtc: utcNow);
    }

    public static Note Rehydrate(
        Guid id,
        string ownerId,
        string title,
        string body,
        string currentBodyHash,
        IReadOnlyList<string> tags,
        string area,
        ProvenanceMarker provenance,
        NoteImportMetadata? importMetadata,
        NoteStatus status,
        Guid versionToken,
        int revisionCount,
        DateTime createdAtUtc,
        DateTime updatedAtUtc) =>
        new(
            id,
            ownerId,
            title,
            body,
            currentBodyHash,
            tags,
            area,
            provenance,
            importMetadata,
            status,
            versionToken,
            revisionCount,
            createdAtUtc,
            updatedAtUtc);

    /// <summary>
    /// Appends text to the note body without creating a full-body revision snapshot or incrementing RevisionCount.
    /// Rotates VersionToken and updates CurrentBodyHash and UpdatedAtUtc.
    /// </summary>
    public void AppendBody(string textToAppend, DateTime occurredAtUtc)
    {
        if (string.IsNullOrEmpty(textToAppend))
        {
            throw new ArgumentException("Text to append cannot be null or empty.", nameof(textToAppend));
        }

        var updatedBody = string.Concat(Body, textToAppend);
        ValidateBody(updatedBody);

        Body = updatedBody;
        CurrentBodyHash = Sha256Hasher.ComputeLowerHex(updatedBody);
        VersionToken = Guid.CreateVersion7();
        UpdatedAtUtc = EnsureUtc(occurredAtUtc);
    }

    /// <summary>
    /// Replaces the note body on a destructive write ("replace-section" or "import-overwrite"),
    /// returning a full prior-body NoteRevision snapshot, incrementing RevisionCount, and rotating VersionToken.
    /// </summary>
    public NoteRevision ReplaceBodyWithRevision(
        string newBody,
        string reason,
        DateTime occurredAtUtc,
        string? updatedTitle = null,
        ProvenanceMarker? updatedProvenance = null,
        int? updatedSectionOrderIndex = null,
        bool updateLastImportedHash = false)
    {
        ValidateBody(newBody);
        var utcOccurred = EnsureUtc(occurredAtUtc);
        var nextRevisionNumber = RevisionCount + 1;

        var revision = NoteRevision.Create(
            ownerId: OwnerId,
            noteId: Id,
            revisionNumber: nextRevisionNumber,
            reason: reason,
            priorBody: Body,
            priorVersionToken: VersionToken,
            createdAtUtc: utcOccurred);

        var newBodyHash = Sha256Hasher.ComputeLowerHex(newBody);
        Body = newBody;
        CurrentBodyHash = newBodyHash;
        RevisionCount = nextRevisionNumber;
        VersionToken = Guid.CreateVersion7();
        UpdatedAtUtc = utcOccurred;

        if (updatedTitle is not null)
        {
            Title = NormalizeTitle(updatedTitle);
        }

        if (updatedProvenance is not null)
        {
            Provenance = updatedProvenance;
        }

        if (updateLastImportedHash && ImportMetadata is not null)
        {
            ImportMetadata = ImportMetadata.WithUpdatedImportState(
                updatedSectionOrderIndex ?? ImportMetadata.SectionOrderIndex,
                newBodyHash);
        }

        return revision;
    }

    public void UpdateProvenance(ProvenanceMarker provenance, DateTime occurredAtUtc)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        Provenance = provenance;
        VersionToken = Guid.CreateVersion7();
        UpdatedAtUtc = EnsureUtc(occurredAtUtc);
    }

    public void Archive(DateTime occurredAtUtc)
    {
        Status = NoteStatus.Archived;
        VersionToken = Guid.CreateVersion7();
        UpdatedAtUtc = EnsureUtc(occurredAtUtc);
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in tags)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var tag = raw.Trim().ToLowerInvariant();
            if (seen.Add(tag))
            {
                normalized.Add(tag);
            }
        }

        return normalized;
    }

    public static string NormalizeArea(string area)
    {
        if (string.IsNullOrWhiteSpace(area))
        {
            throw new ArgumentException("Area label cannot be null or whitespace.", nameof(area));
        }

        return area.Trim().ToLowerInvariant();
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Note title cannot be null or whitespace.", nameof(title));
        }

        return title.Trim();
    }

    private static void ValidateBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(body);
        if (byteCount > MaxBodyBytes)
        {
            throw new ArgumentException($"Note body byte length ({byteCount}) exceeds maximum allowed ({MaxBodyBytes}).", nameof(body));
        }
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
