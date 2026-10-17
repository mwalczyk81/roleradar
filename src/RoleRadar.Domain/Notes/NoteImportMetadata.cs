namespace RoleRadar.Domain.Notes;

public sealed record NoteImportMetadata
{
    public string ImportKey { get; }

    public string SourceFileId { get; }

    public string HeadingPath { get; }

    public int OccurrenceIndex { get; }

    public int SectionOrderIndex { get; }

    public string LastImportedBodyHash { get; }

    private NoteImportMetadata(
        string importKey,
        string sourceFileId,
        string headingPath,
        int occurrenceIndex,
        int sectionOrderIndex,
        string lastImportedBodyHash)
    {
        ImportKey = importKey;
        SourceFileId = sourceFileId;
        HeadingPath = headingPath;
        OccurrenceIndex = occurrenceIndex;
        SectionOrderIndex = sectionOrderIndex;
        LastImportedBodyHash = lastImportedBodyHash;
    }

    public static NoteImportMetadata Create(
        string sourceFileId,
        string headingPath,
        int occurrenceIndex,
        int sectionOrderIndex,
        string lastImportedBodyHash)
    {
        if (string.IsNullOrWhiteSpace(sourceFileId))
        {
            throw new ArgumentException("Source file identifier is required.", nameof(sourceFileId));
        }

        if (string.IsNullOrWhiteSpace(headingPath))
        {
            throw new ArgumentException("Heading path is required.", nameof(headingPath));
        }

        if (occurrenceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrenceIndex), "Occurrence index cannot be negative.");
        }

        if (sectionOrderIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectionOrderIndex), "Section order index cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(lastImportedBodyHash) || lastImportedBodyHash.Length != 64)
        {
            throw new ArgumentException("Last-imported body hash must be a 64-character lowercase hex SHA-256 string.", nameof(lastImportedBodyHash));
        }

        var normalizedSourceId = sourceFileId.Trim();
        var normalizedHeadingPath = headingPath.Trim();
        var importKey = $"{normalizedSourceId}#{normalizedHeadingPath}#{occurrenceIndex}";

        return new NoteImportMetadata(
            importKey,
            normalizedSourceId,
            normalizedHeadingPath,
            occurrenceIndex,
            sectionOrderIndex,
            lastImportedBodyHash.ToLowerInvariant());
    }

    public static NoteImportMetadata Rehydrate(
        string importKey,
        string sourceFileId,
        string headingPath,
        int occurrenceIndex,
        int sectionOrderIndex,
        string lastImportedBodyHash) =>
        new(importKey, sourceFileId, headingPath, occurrenceIndex, sectionOrderIndex, lastImportedBodyHash);

    public NoteImportMetadata WithUpdatedImportState(int sectionOrderIndex, string newImportedBodyHash) =>
        Create(SourceFileId, HeadingPath, OccurrenceIndex, sectionOrderIndex, newImportedBodyHash);
}
