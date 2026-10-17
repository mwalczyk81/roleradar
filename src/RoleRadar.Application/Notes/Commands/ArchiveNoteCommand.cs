using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Notes.Commands;

public sealed record NoteArchivedDto(
    Guid Id,
    Guid VersionToken,
    string Status,
    DateTime UpdatedAtUtc);

public sealed record ArchiveNoteCommand(
    Guid Id,
    Guid? ExpectedVersionToken = null,
    string Actor = "mcp-stdio",
    string? OwnerId = null) : ICommand<NoteArchivedDto>;

public sealed class ArchiveNoteCommandValidator : ICommandValidator<ArchiveNoteCommand>
{
    public ResultError? Validate(ArchiveNoteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "Note ID cannot be Guid.Empty.");
        }

        if (command.ExpectedVersionToken.HasValue && command.ExpectedVersionToken.Value == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "ExpectedVersionToken cannot be Guid.Empty.");
        }

        return null;
    }
}

public sealed class ArchiveNoteCommandHandler : ICommandHandler<ArchiveNoteCommand, NoteArchivedDto>
{
    private static readonly ArchiveNoteCommandValidator Validator = new();
    private readonly INoteRepository _repository;
    private readonly IUtcClock _clock;

    public ArchiveNoteCommandHandler(INoteRepository repository, IUtcClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<NoteArchivedDto>> HandleAsync(
        ArchiveNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationError = Validator.Validate(command);
        if (validationError is not null)
        {
            return Result<NoteArchivedDto>.Failure(validationError.Value);
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var actor = string.IsNullOrWhiteSpace(command.Actor) ? "mcp-stdio" : command.Actor.Trim();
        var now = _clock.UtcNow;

        var outcome = await _repository.ExecuteInTransactionAsync(
            async (tx, ct) =>
            {
                var existingResult = await _repository.GetByIdAsync(ownerId, command.Id, ct);
                if (!existingResult.IsSuccess)
                {
                    return Result<NoteWriteOutcome>.Failure(existingResult.Error!.Value);
                }

                var note = existingResult.Value!;

                // Check caller expected version token if provided
                if (command.ExpectedVersionToken.HasValue &&
                    command.ExpectedVersionToken.Value != note.VersionToken)
                {
                    return Result<NoteWriteOutcome>.Failure(
                        ErrorCode.StaleVersionToken,
                        $"Note '{command.Id}' was modified concurrently. Expected version token '{command.ExpectedVersionToken.Value}', but current version token is '{note.VersionToken}'.",
                        new Dictionary<string, object?>
                        {
                            ["currentVersionToken"] = note.VersionToken.ToString()
                        });
                }

                // Idempotency: archiving an already-archived note is a success,
                // producing NO version rotation and NO second audit row.
                if (note.Status == NoteStatus.Archived)
                {
                    return Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(
                        note,
                        AuditLogEntry.CreateForArchive(
                            ownerId,
                            actor,
                            note.Id,
                            note.VersionToken,
                            now,
                            AuditContentSummary.FromNote(note),
                            AuditContentSummary.FromNote(note))));
                }

                var expectedToken = note.VersionToken;
                var beforeSummary = AuditContentSummary.FromNote(note);

                note.Archive(now);

                var afterSummary = AuditContentSummary.FromNote(note);
                var audit = AuditLogEntry.CreateForArchive(
                    ownerId: ownerId,
                    actor: actor,
                    targetNoteId: note.Id,
                    resultingVersionToken: note.VersionToken,
                    occurredAtUtc: note.UpdatedAtUtc,
                    beforeSummary: beforeSummary,
                    afterSummary: afterSummary);

                return await tx.ArchiveAsync(note, expectedToken, audit, ct);
            },
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            return Result<NoteArchivedDto>.Failure(outcome.Error!.Value);
        }

        var archived = outcome.Value!.Note;
        return Result<NoteArchivedDto>.Success(new NoteArchivedDto(
            Id: archived.Id,
            VersionToken: archived.VersionToken,
            Status: archived.Status.ToWireString(),
            UpdatedAtUtc: archived.UpdatedAtUtc));
    }
}
