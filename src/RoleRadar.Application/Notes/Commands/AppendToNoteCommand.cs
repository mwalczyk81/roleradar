using System.Text;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Notes.Commands;

public sealed record NoteUpdatedDto(
    Guid Id,
    Guid VersionToken,
    string BodyHash,
    int ByteLength,
    DateTime UpdatedAtUtc);

public sealed record AppendToNoteCommand(
    Guid Id,
    string TextToAppend,
    string Actor = "mcp-stdio",
    string? OwnerId = null) : ICommand<NoteUpdatedDto>;

public sealed class AppendToNoteCommandValidator : ICommandValidator<AppendToNoteCommand>
{
    public ResultError? Validate(AppendToNoteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Id == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "Note ID cannot be Guid.Empty.");
        }

        if (string.IsNullOrEmpty(command.TextToAppend))
        {
            return new ResultError(ErrorCode.ValidationError, "TextToAppend cannot be empty.");
        }

        var appendBytes = Encoding.UTF8.GetByteCount(command.TextToAppend);
        if (appendBytes > Note.MaxBodyBytes)
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"TextToAppend UTF-8 size ({appendBytes} bytes) exceeds maximum allowed ({Note.MaxBodyBytes} bytes / 8 MiB).");
        }

        return null;
    }
}

public sealed class AppendToNoteCommandHandler : ICommandHandler<AppendToNoteCommand, NoteUpdatedDto>
{
    private static readonly AppendToNoteCommandValidator Validator = new();
    private readonly INoteRepository _repository;
    private readonly IUtcClock _clock;

    public AppendToNoteCommandHandler(INoteRepository repository, IUtcClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public async Task<Result<NoteUpdatedDto>> HandleAsync(
        AppendToNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validationError = Validator.Validate(command);
        if (validationError is not null)
        {
            return Result<NoteUpdatedDto>.Failure(validationError.Value);
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var actor = string.IsNullOrWhiteSpace(command.Actor) ? "mcp-stdio" : command.Actor.Trim();
        var now = _clock.UtcNow;

        var outcome = await _repository.ExecuteInTransactionAsync(
            (tx, ct) => tx.AppendAsync(ownerId, command.Id, command.TextToAppend, actor, now, ct),
            cancellationToken);

        if (!outcome.IsSuccess)
        {
            return Result<NoteUpdatedDto>.Failure(outcome.Error!.Value);
        }

        var updated = outcome.Value!.Note;
        return Result<NoteUpdatedDto>.Success(new NoteUpdatedDto(
            Id: updated.Id,
            VersionToken: updated.VersionToken,
            BodyHash: updated.CurrentBodyHash,
            ByteLength: Encoding.UTF8.GetByteCount(updated.Body),
            UpdatedAtUtc: updated.UpdatedAtUtc));
    }
}
