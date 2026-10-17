using System.Text.Json.Serialization;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Notes.Queries;

public sealed record NoteDetailDto(
    Guid Id,
    string Title,
    string Body,
    int TotalChars,
    bool Truncated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? NextOffset,
    string Area,
    IReadOnlyList<string> Tags,
    ProvenanceDto Provenance,
    string Status,
    Guid VersionToken,
    string BodyHash,
    int RevisionCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record GetNoteQuery(
    Guid Id,
    int Offset = 0,
    int MaxChars = Note.DefaultGetMaxChars,
    string? OwnerId = null) : IQuery<NoteDetailDto>;

public sealed class GetNoteQueryHandler : IQueryHandler<GetNoteQuery, NoteDetailDto>
{
    private readonly INoteRepository _repository;

    public GetNoteQueryHandler(INoteRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<NoteDetailDto>> HandleAsync(
        GetNoteQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Id == Guid.Empty)
        {
            return Result<NoteDetailDto>.Failure(
                ErrorCode.ValidationError,
                "Note ID cannot be Guid.Empty.");
        }

        if (query.Offset < 0)
        {
            return Result<NoteDetailDto>.Failure(
                ErrorCode.ValidationError,
                $"Offset ({query.Offset}) must be greater than or equal to 0.");
        }

        if (query.MaxChars < 1 || query.MaxChars > Note.MaxGetMaxChars)
        {
            return Result<NoteDetailDto>.Failure(
                ErrorCode.ValidationError,
                $"MaxChars ({query.MaxChars}) must be between 1 and {Note.MaxGetMaxChars}.");
        }

        var ownerId = string.IsNullOrWhiteSpace(query.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(query.OwnerId).Value;

        var readiness = await _repository.CheckReadinessAsync(cancellationToken);
        if (!readiness.IsSuccess)
        {
            return Result<NoteDetailDto>.Failure(readiness.Error!.Value);
        }

        var noteResult = await _repository.GetByIdAsync(ownerId, query.Id, cancellationToken);
        if (!noteResult.IsSuccess)
        {
            return Result<NoteDetailDto>.Failure(noteResult.Error!.Value);
        }

        var note = noteResult.Value!;
        var fullBody = note.Body;
        var totalChars = fullBody.Length;

        string slicedBody;
        bool truncated;
        int? nextOffset;

        if (query.Offset >= totalChars)
        {
            slicedBody = string.Empty;
            truncated = false;
            nextOffset = null;
        }
        else
        {
            var startOffset = query.Offset;
            if (startOffset > 0 &&
                char.IsLowSurrogate(fullBody[startOffset]) &&
                char.IsHighSurrogate(fullBody[startOffset - 1]))
            {
                startOffset--;
            }

            var take = Math.Min(query.MaxChars, totalChars - startOffset);
            var endOffset = startOffset + take;

            if (endOffset < totalChars &&
                endOffset > startOffset &&
                char.IsHighSurrogate(fullBody[endOffset - 1]) &&
                char.IsLowSurrogate(fullBody[endOffset]))
            {
                if (take > 1)
                {
                    take--;
                    endOffset--;
                }
                else
                {
                    take = 2;
                    endOffset = startOffset + 2;
                }
            }

            slicedBody = fullBody.Substring(startOffset, take);
            truncated = endOffset < totalChars;
            nextOffset = truncated ? endOffset : null;
        }

        return Result<NoteDetailDto>.Success(new NoteDetailDto(
            Id: note.Id,
            Title: note.Title,
            Body: slicedBody,
            TotalChars: totalChars,
            Truncated: truncated,
            NextOffset: nextOffset,
            Area: note.Area,
            Tags: note.Tags,
            Provenance: new ProvenanceDto(
                note.Provenance.Kind.ToWireString(),
                note.Provenance.Date,
                note.Provenance.Qualifier),
            Status: note.Status.ToWireString(),
            VersionToken: note.VersionToken,
            BodyHash: note.CurrentBodyHash,
            RevisionCount: note.RevisionCount,
            CreatedAtUtc: note.CreatedAtUtc,
            UpdatedAtUtc: note.UpdatedAtUtc));
    }
}
