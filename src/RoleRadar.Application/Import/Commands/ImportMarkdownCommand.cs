using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Import.Commands;

public sealed record ImportMarkdownConflictDto(
    string ImportKey,
    Guid NoteId,
    string Title,
    string Reason);

public sealed record ImportMarkdownOrphanDto(
    string ImportKey,
    Guid NoteId,
    string Title);

public sealed record ImportMarkdownSummaryDto(
    int FilesProcessed,
    int CreatedCount,
    int UpdatedCount,
    int UnchangedCount,
    int ConflictCount,
    int OrphanCount,
    IReadOnlyList<ImportMarkdownConflictDto> Conflicts,
    IReadOnlyList<ImportMarkdownOrphanDto> Orphans);

public sealed record ImportMarkdownCommand(
    IReadOnlyList<string> FilePaths,
    string Area = "imported",
    IReadOnlyList<string>? Tags = null,
    string? DefaultDate = null,
    bool DryRun = false,
    string? SourceFileId = null,
    string Actor = "cli-import",
    string? OwnerId = null) : ICommand<ImportMarkdownSummaryDto>;

public sealed class ImportMarkdownCommandValidator : ICommandValidator<ImportMarkdownCommand>
{
    public ResultError? Validate(ImportMarkdownCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.FilePaths is null || command.FilePaths.Count == 0)
        {
            return new ResultError(ErrorCode.ValidationError, "At least one file path must be provided for import.");
        }

        foreach (var path in command.FilePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ResultError(ErrorCode.ValidationError, "File path cannot be null or whitespace.");
            }

            if (!File.Exists(path))
            {
                return new ResultError(ErrorCode.ValidationError, $"File not found: '{path}'.");
            }
        }

        var collidingGroups = command.FilePaths
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();

        if (collidingGroups.Count > 0)
        {
            var firstGroup = collidingGroups[0];
            var collidingPaths = string.Join("', '", firstGroup);
            return new ResultError(
                ErrorCode.ValidationError,
                $"Input file paths resolve to colliding source file identifier '{firstGroup.Key}': '{collidingPaths}'.");
        }

        if (!string.IsNullOrWhiteSpace(command.SourceFileId))
        {
            if (command.FilePaths.Count > 1)
            {
                return new ResultError(
                    ErrorCode.ValidationError,
                    "Option '--source-id' can only be specified when importing exactly one file.");
            }
        }

        if (string.IsNullOrWhiteSpace(command.Area))
        {
            return new ResultError(ErrorCode.ValidationError, "Area label is required and cannot be blank.");
        }

        if (command.Area.Trim().Length > Note.MaxAreaChars)
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Area label length ({command.Area.Trim().Length}) exceeds maximum allowed ({Note.MaxAreaChars} characters).");
        }

        if (command.Tags is not null)
        {
            if (command.Tags.Count > Note.MaxTagCount)
            {
                return new ResultError(
                    ErrorCode.ValidationError,
                    $"Tag count ({command.Tags.Count}) exceeds maximum allowed ({Note.MaxTagCount} tags).");
            }

            foreach (var tag in command.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    return new ResultError(ErrorCode.ValidationError, "Tag values cannot be blank or whitespace.");
                }

                if (tag.Trim().Length > Note.MaxTagChars)
                {
                    return new ResultError(
                        ErrorCode.ValidationError,
                        $"Tag length ({tag.Trim().Length}) exceeds maximum allowed ({Note.MaxTagChars} characters).");
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(command.DefaultDate) && !ProvenanceMarker.IsValidIsoDate(command.DefaultDate))
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Invalid default date '{command.DefaultDate}'. Expected ISO-8601 date 'YYYY-MM-DD'.");
        }

        return null;
    }
}

public sealed class ImportMarkdownCommandHandler : ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>
{
    private static readonly ImportMarkdownCommandValidator Validator = new();
    private readonly INoteRepository _repository;
    private readonly IUtcClock _clock;

    public ImportMarkdownCommandHandler(INoteRepository repository, IUtcClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<ImportMarkdownSummaryDto>> HandleAsync(
        ImportMarkdownCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationError = Validator.Validate(command);
        if (validationError is not null)
        {
            return Result<ImportMarkdownSummaryDto>.Failure(validationError.Value);
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var actor = string.IsNullOrWhiteSpace(command.Actor) ? "cli-import" : command.Actor.Trim();
        var normalizedTags = Note.NormalizeTags(command.Tags);
        var normalizedArea = Note.NormalizeArea(command.Area);
        var now = _clock.UtcNow;

        // Pre-flight UTF-8 validation across all input files before any database operations
        var fileDataList = new List<(string FilePath, string SourceFileId, byte[] Bytes)>(command.FilePaths.Count);
        foreach (var path in command.FilePaths)
        {
            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            }
            catch (Exception ex)
            {
                return Result<ImportMarkdownSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"Failed to read file '{path}': {ex.Message}");
            }

            if (!MarkdownImportParser.ValidateUtf8(bytes))
            {
                return Result<ImportMarkdownSummaryDto>.Failure(
                    ErrorCode.InvalidUtf8,
                    $"File '{path}' contains invalid UTF-8 byte sequences.");
            }

            var sourceFileId = (!string.IsNullOrWhiteSpace(command.SourceFileId) && command.FilePaths.Count == 1)
                ? command.SourceFileId.Trim()
                : Path.GetFileName(path);

            fileDataList.Add((path, sourceFileId, bytes));
        }

        var createdCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var conflictCount = 0;
        var orphanCount = 0;

        var conflicts = new List<ImportMarkdownConflictDto>();
        var orphans = new List<ImportMarkdownOrphanDto>();

        foreach (var (filePath, sourceFileId, bytes) in fileDataList)
        {
            var resolvedDefaultDate = !string.IsNullOrWhiteSpace(command.DefaultDate)
                ? command.DefaultDate.Trim()
                : File.GetLastWriteTimeUtc(filePath).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            var parsedSections = MarkdownImportParser.ParseBytes(sourceFileId, bytes, resolvedDefaultDate);

            var existingNotesResult = await _repository.GetImportedBySourceFileAsync(ownerId, sourceFileId, cancellationToken);
            if (!existingNotesResult.IsSuccess)
            {
                return Result<ImportMarkdownSummaryDto>.Failure(existingNotesResult.Error!.Value);
            }

            var existingMap = existingNotesResult.Value!
                .Where(n => n.ImportMetadata is not null)
                .ToDictionary(n => n.ImportMetadata!.ImportKey, StringComparer.Ordinal);

            var seenImportKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var section in parsedSections)
            {
                seenImportKeys.Add(section.ImportKey);

                if (!existingMap.TryGetValue(section.ImportKey, out var existing))
                {
                    // Brand new section
                    if (!command.DryRun)
                    {
                        var upsertReq = new UpsertImportedSectionRequest(
                            OwnerId: ownerId,
                            SourceFileId: sourceFileId,
                            HeadingPath: section.HeadingPath,
                            OccurrenceIndex: section.OccurrenceIndex,
                            SectionOrderIndex: section.SectionOrderIndex,
                            Title: section.Title,
                            Body: section.Body,
                            Tags: normalizedTags,
                            Area: normalizedArea,
                            Provenance: section.Provenance,
                            ResolvedDefaultDate: resolvedDefaultDate,
                            Actor: actor,
                            OccurredAtUtc: now);

                        var outcome = await _repository.ExecuteInTransactionAsync(
                            (tx, ct) => tx.UpsertImportedSectionAsync(upsertReq, ct),
                            cancellationToken);

                        if (!outcome.IsSuccess)
                        {
                            return Result<ImportMarkdownSummaryDto>.Failure(outcome.Error!.Value);
                        }
                    }

                    createdCount++;
                }
                else
                {
                    // Existing section in DB
                    if (existing.HasPostImportEdits)
                    {
                        // Stored note was modified after its last import: conflict, do not overwrite!
                        conflicts.Add(new ImportMarkdownConflictDto(
                            ImportKey: section.ImportKey,
                            NoteId: existing.Id,
                            Title: existing.Title,
                            Reason: "Note was modified after its last import."));
                        conflictCount++;
                    }
                    else if (string.Equals(existing.CurrentBodyHash, section.BodyHash, StringComparison.Ordinal))
                    {
                        // Unchanged body: no write needed, no duplicate revision
                        unchangedCount++;
                    }
                    else
                    {
                        // Source section updated and stored note unmodified: update note with revision
                        if (!command.DryRun)
                        {
                            var upsertReq = new UpsertImportedSectionRequest(
                                OwnerId: ownerId,
                                SourceFileId: sourceFileId,
                                HeadingPath: section.HeadingPath,
                                OccurrenceIndex: section.OccurrenceIndex,
                                SectionOrderIndex: section.SectionOrderIndex,
                                Title: section.Title,
                                Body: section.Body,
                                Tags: normalizedTags,
                                Area: normalizedArea,
                                Provenance: section.Provenance,
                                ResolvedDefaultDate: resolvedDefaultDate,
                                Actor: actor,
                                OccurredAtUtc: now);

                            var outcome = await _repository.ExecuteInTransactionAsync(
                                (tx, ct) => tx.UpsertImportedSectionAsync(upsertReq, ct),
                                cancellationToken);

                            if (!outcome.IsSuccess)
                            {
                                return Result<ImportMarkdownSummaryDto>.Failure(outcome.Error!.Value);
                            }
                        }

                        updatedCount++;
                    }
                }
            }

            // Detect orphaned notes that existed for this source file but are no longer in the parsed source file
            foreach (var (key, note) in existingMap)
            {
                if (!seenImportKeys.Contains(key))
                {
                    orphans.Add(new ImportMarkdownOrphanDto(
                        ImportKey: key,
                        NoteId: note.Id,
                        Title: note.Title));
                    orphanCount++;
                }
            }
        }

        var summary = new ImportMarkdownSummaryDto(
            FilesProcessed: fileDataList.Count,
            CreatedCount: createdCount,
            UpdatedCount: updatedCount,
            UnchangedCount: unchangedCount,
            ConflictCount: conflictCount,
            OrphanCount: orphanCount,
            Conflicts: conflicts,
            Orphans: orphans);

        return Result<ImportMarkdownSummaryDto>.Success(summary);
    }
}
