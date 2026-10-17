using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;

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
    private readonly INoteRepository _repository;
    private readonly IUtcClock _clock;

    public ArchiveNoteCommandHandler(INoteRepository repository, IUtcClock clock)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(clock);
        _repository = repository;
        _clock = clock;
    }

    public Task<Result<NoteArchivedDto>> HandleAsync(
        ArchiveNoteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Stub implementation for TDD phase: returns failure so tests fail on assertion, not compilation
        return Task.FromResult(Result<NoteArchivedDto>.Failure(
            ErrorCode.ValidationError,
            "ArchiveNoteCommandHandler is not implemented yet."));
    }
}
