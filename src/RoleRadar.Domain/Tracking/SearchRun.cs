using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Tracking;

public sealed class SearchRun
{
    public const int MaxNotesLength = 500;

    public Guid Id { get; }
    public string OwnerId { get; }
    public DateTimeOffset StartedAt { get; }
    public int NewPostingsCount { get; }
    public Guid? ReportNoteId { get; }
    public string? Notes { get; }
    public DateTimeOffset CreatedAt { get; }

    public SearchRun(
        Guid id,
        string ownerId,
        DateTimeOffset startedAt,
        int newPostingsCount,
        Guid? reportNoteId,
        string? notes,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("SearchRun id cannot be empty.", nameof(id));
        }

        OwnerId = new OwnerId(ownerId).Value;
        StartedAt = startedAt;
        if (newPostingsCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newPostingsCount), "NewPostingsCount must be >= 0.");
        }

        NewPostingsCount = newPostingsCount;
        ReportNoteId = reportNoteId;
        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new ArgumentException($"Notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
        }

        Notes = notes?.Trim();
        CreatedAt = createdAt;
        Id = id;
    }

    public static SearchRun Create(
        string ownerId,
        DateTimeOffset startedAt,
        int newPostingsCount,
        Guid? reportNoteId = null,
        string? notes = null,
        DateTimeOffset? createdAt = null,
        Guid? id = null)
    {
        return new SearchRun(
            id: id ?? Guid.CreateVersion7(),
            ownerId: ownerId,
            startedAt: startedAt,
            newPostingsCount: newPostingsCount,
            reportNoteId: reportNoteId,
            notes: notes,
            createdAt: createdAt ?? DateTimeOffset.UtcNow);
    }
}
