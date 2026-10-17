using System.Text;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Notes.Commands;

public sealed record NoteSectionReplacedDto(
    Guid Id,
    Guid VersionToken,
    Guid RevisionId,
    int RevisionCount,
    string BodyHash,
    DateTime UpdatedAtUtc);

public sealed record ReplaceNoteSectionCommand(
    Guid Id,
    string Heading,
    string NewSectionBody,
    Guid VersionToken,
    string Actor = "mcp-stdio",
    string? OwnerId = null) : ICommand<NoteSectionReplacedDto>;

public sealed class ReplaceNoteSectionCommandValidator : ICommandValidator<ReplaceNoteSectionCommand>
{
    public ResultError? Validate(ReplaceNoteSectionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "Note ID cannot be Guid.Empty.");
        }

        if (string.IsNullOrWhiteSpace(command.Heading))
        {
            return new ResultError(ErrorCode.ValidationError, "Target heading is required and cannot be blank.");
        }

        if (command.NewSectionBody is null)
        {
            return new ResultError(ErrorCode.ValidationError, "NewSectionBody is required.");
        }

        var sectionBytes = Encoding.UTF8.GetByteCount(command.NewSectionBody);
        if (sectionBytes > Note.MaxBodyBytes)
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"NewSectionBody UTF-8 size ({sectionBytes} bytes) exceeds maximum allowed ({Note.MaxBodyBytes} bytes / 8 MiB).");
        }

        if (command.VersionToken == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "VersionToken cannot be Guid.Empty.");
        }

        return null;
    }
}

public sealed class ReplaceNoteSectionCommandHandler : ICommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto>
{
    private static readonly ReplaceNoteSectionCommandValidator Validator = new();
    private readonly INoteRepository _repository;
    private readonly IUtcClock _clock;

    public ReplaceNoteSectionCommandHandler(INoteRepository repository, IUtcClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<NoteSectionReplacedDto>> HandleAsync(
        ReplaceNoteSectionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationError = Validator.Validate(command);
        if (validationError is not null)
        {
            return Result<NoteSectionReplacedDto>.Failure(validationError.Value);
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
                if (note.VersionToken != command.VersionToken)
                {
                    return Result<NoteWriteOutcome>.Failure(
                        ErrorCode.StaleVersionToken,
                        $"Note '{command.Id}' was modified concurrently. Expected version token '{command.VersionToken}', but current version token is '{note.VersionToken}'.",
                        new Dictionary<string, object?>
                        {
                            ["currentVersionToken"] = note.VersionToken.ToString()
                        });
                }

                var sectionOutcome = MarkdownSectionParser.ReplaceSection(
                    note.Body,
                    command.Heading,
                    command.NewSectionBody);

                if (sectionOutcome.Status == SectionReplacementStatus.SectionNotFound)
                {
                    return Result<NoteWriteOutcome>.Failure(
                        ErrorCode.SectionNotFound,
                        $"Heading '{command.Heading}' was not found in note '{command.Id}'.",
                        new Dictionary<string, object?>
                        {
                            ["availableHeadings"] = sectionOutcome.AvailableHeadings
                        });
                }

                if (sectionOutcome.Status == SectionReplacementStatus.AmbiguousHeading)
                {
                    return Result<NoteWriteOutcome>.Failure(
                        ErrorCode.AmbiguousHeading,
                        $"Heading '{command.Heading}' matched multiple sections in note '{command.Id}'. Disambiguate using a heading path.",
                        new Dictionary<string, object?>
                        {
                            ["matchingHeadings"] = sectionOutcome.MatchingHeadings,
                            ["availableHeadings"] = sectionOutcome.AvailableHeadings
                        });
                }

                var resultingBytes = Encoding.UTF8.GetByteCount(sectionOutcome.UpdatedBody!);
                if (resultingBytes > Note.MaxBodyBytes)
                {
                    return Result<NoteWriteOutcome>.Failure(
                        ErrorCode.ValidationError,
                        $"Resulting note body UTF-8 size ({resultingBytes} bytes) after section replacement exceeds maximum allowed ({Note.MaxBodyBytes} bytes / 8 MiB).");
                }

                var beforeSummary = AuditContentSummary.FromNote(note);
                var revision = note.ReplaceBodyWithRevision(
                    newBody: sectionOutcome.UpdatedBody!,
                    reason: NoteRevision.ReasonReplaceSection,
                    occurredAtUtc: now);

                var afterSummary = AuditContentSummary.FromNote(note);
                var audit = AuditLogEntry.CreateForReplaceSection(
                    ownerId: ownerId,
                    actor: actor,
                    targetNoteId: note.Id,
                    noteRevisionId: revision.Id,
                    resultingVersionToken: note.VersionToken,
                    occurredAtUtc: note.UpdatedAtUtc,
                    beforeSummary: beforeSummary,
                    afterSummary: afterSummary);

                return await tx.ReplaceSectionAsync(note, command.VersionToken, revision, audit, ct);
            },
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            return Result<NoteSectionReplacedDto>.Failure(outcome.Error!.Value);
        }

        var updated = outcome.Value!.Note;
        var savedRevision = outcome.Value.Revision!;
        return Result<NoteSectionReplacedDto>.Success(new NoteSectionReplacedDto(
            Id: updated.Id,
            VersionToken: updated.VersionToken,
            RevisionId: savedRevision.Id,
            RevisionCount: updated.RevisionCount,
            BodyHash: updated.CurrentBodyHash,
            UpdatedAtUtc: updated.UpdatedAtUtc));
    }
}
