using System.Text;
using System.Text.Json.Serialization;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Notes.Commands;

public sealed record ProvenanceDto(
    string Kind,
    string Date,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Qualifier);

public sealed record NoteCreatedDto(
    Guid Id,
    string Title,
    string Area,
    IReadOnlyList<string> Tags,
    ProvenanceDto Provenance,
    string Status,
    Guid VersionToken,
    string BodyHash,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateNoteCommand(
    string Title,
    string Body,
    string Area,
    IReadOnlyList<string>? Tags,
    string ProvenanceKind,
    string ProvenanceDate,
    string? ProvenanceQualifier,
    string Actor = "mcp-stdio",
    string? OwnerId = null) : ICommand<NoteCreatedDto>;

public sealed class CreateNoteCommandValidator : ICommandValidator<CreateNoteCommand>
{
    public ResultError? Validate(CreateNoteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return new ResultError(ErrorCode.ValidationError, "Note title is required and cannot be blank.");
        }

        var trimmedTitleLength = command.Title.Trim().Length;
        if (trimmedTitleLength > Note.MaxTitleChars)
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Note title length ({trimmedTitleLength} characters) exceeds maximum allowed ({Note.MaxTitleChars} characters).");
        }

        if (command.Body is null)
        {
            return new ResultError(ErrorCode.ValidationError, "Note body is required.");
        }

        var bodyByteCount = Encoding.UTF8.GetByteCount(command.Body);
        if (bodyByteCount > Note.MaxBodyBytes)
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Note body UTF-8 size ({bodyByteCount} bytes) exceeds maximum allowed ({Note.MaxBodyBytes} bytes / 8 MiB).");
        }

        if (string.IsNullOrWhiteSpace(command.Area))
        {
            return new ResultError(ErrorCode.ValidationError, "Note area is required and cannot be blank.");
        }

        var trimmedAreaLength = command.Area.Trim().Length;
        if (trimmedAreaLength > Note.MaxAreaChars)
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Note area length ({trimmedAreaLength} characters) exceeds maximum allowed ({Note.MaxAreaChars} characters).");
        }

        if (command.Tags is not null)
        {
            if (command.Tags.Count > Note.MaxTagCount)
            {
                return new ResultError(
                    ErrorCode.ValidationError,
                    $"Tag count ({command.Tags.Count}) exceeds maximum allowed ({Note.MaxTagCount} tags).");
            }

            foreach (var rawTag in command.Tags)
            {
                if (string.IsNullOrWhiteSpace(rawTag))
                {
                    return new ResultError(ErrorCode.ValidationError, "Tag values cannot be blank or whitespace.");
                }

                var trimmedTagLength = rawTag.Trim().Length;
                if (trimmedTagLength > Note.MaxTagChars)
                {
                    return new ResultError(
                        ErrorCode.ValidationError,
                        $"Tag length ({trimmedTagLength} characters) exceeds maximum allowed ({Note.MaxTagChars} characters).");
                }
            }
        }

        if (!ProvenanceKindExtensions.TryParseWireString(command.ProvenanceKind, out _))
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Invalid provenance kind '{command.ProvenanceKind}'. Expected 'stated', 'researched', or 'inferred'.");
        }

        if (!ProvenanceMarker.IsValidIsoDate(command.ProvenanceDate))
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Invalid provenance date '{command.ProvenanceDate}'. Expected ISO-8601 date 'YYYY-MM-DD'.");
        }

        return null;
    }
}

public sealed class CreateNoteCommandHandler : ICommandHandler<CreateNoteCommand, NoteCreatedDto>
{
    private static readonly CreateNoteCommandValidator Validator = new();
    private readonly INoteRepository _repository;
    private readonly IUtcClock _clock;

    public CreateNoteCommandHandler(INoteRepository repository, IUtcClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<NoteCreatedDto>> HandleAsync(
        CreateNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationError = Validator.Validate(command);
        if (validationError is not null)
        {
            return Result<NoteCreatedDto>.Failure(validationError.Value);
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var actor = string.IsNullOrWhiteSpace(command.Actor) ? "mcp-stdio" : command.Actor.Trim();
        var now = _clock.UtcNow;

        var provKind = ProvenanceKindExtensions.FromWireString(command.ProvenanceKind);
        var provenance = new ProvenanceMarker(provKind, command.ProvenanceDate, command.ProvenanceQualifier);

        var note = Note.Create(
            ownerId: ownerId,
            title: command.Title,
            body: command.Body,
            tags: command.Tags,
            area: command.Area,
            provenance: provenance,
            createdAtUtc: now);

        var audit = AuditLogEntry.CreateForCreate(
            ownerId: ownerId,
            actor: actor,
            targetNoteId: note.Id,
            resultingVersionToken: note.VersionToken,
            occurredAtUtc: now,
            afterSummary: AuditContentSummary.FromNote(note));

        var outcome = await _repository.ExecuteInTransactionAsync(
            (tx, ct) => tx.CreateAsync(note, audit, ct),
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            return Result<NoteCreatedDto>.Failure(outcome.Error!.Value);
        }

        var saved = outcome.Value!.Note;
        return Result<NoteCreatedDto>.Success(new NoteCreatedDto(
            Id: saved.Id,
            Title: saved.Title,
            Area: saved.Area,
            Tags: saved.Tags,
            Provenance: new ProvenanceDto(
                saved.Provenance.Kind.ToWireString(),
                saved.Provenance.Date,
                saved.Provenance.Qualifier),
            Status: saved.Status.ToWireString(),
            VersionToken: saved.VersionToken,
            BodyHash: saved.CurrentBodyHash,
            CreatedAtUtc: saved.CreatedAtUtc,
            UpdatedAtUtc: saved.UpdatedAtUtc));
    }
}
