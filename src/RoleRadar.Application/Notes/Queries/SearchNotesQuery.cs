using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Application.Notes.Queries;

public sealed record NoteSearchItemDto(
    Guid Id,
    string Title,
    string Area,
    IReadOnlyList<string> Tags,
    ProvenanceDto Provenance,
    string Status,
    Guid VersionToken,
    string Snippet,
    double Score,
    DateTime UpdatedAtUtc);

public sealed record NoteSearchResponseDto(
    IReadOnlyList<NoteSearchItemDto> Results);

public sealed record SearchNotesQuery(
    string? Query = null,
    IReadOnlyList<string>? Tags = null,
    string? Area = null,
    bool IncludeArchived = false,
    int Limit = 20,
    string? OwnerId = null) : IQuery<NoteSearchResponseDto>;

public sealed class SearchNotesQueryHandler : IQueryHandler<SearchNotesQuery, NoteSearchResponseDto>
{
    private readonly INoteRepository _repository;

    public SearchNotesQueryHandler(INoteRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<Result<NoteSearchResponseDto>> HandleAsync(
        SearchNotesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ownerId = string.IsNullOrWhiteSpace(query.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(query.OwnerId).Value;

        var readiness = await _repository.CheckReadinessAsync(cancellationToken);
        if (!readiness.IsSuccess)
        {
            return Result<NoteSearchResponseDto>.Failure(readiness.Error!.Value);
        }

        var clampedLimit = Math.Clamp(query.Limit <= 0 ? 20 : query.Limit, 1, 100);
        var criteria = new NoteSearchCriteria(
            OwnerId: ownerId,
            Query: string.IsNullOrWhiteSpace(query.Query) ? null : query.Query.Trim(),
            Tags: query.Tags,
            Area: string.IsNullOrWhiteSpace(query.Area) ? null : query.Area.Trim(),
            IncludeArchived: query.IncludeArchived,
            Limit: clampedLimit);

        var hitsResult = await _repository.SearchAsync(criteria, cancellationToken);
        if (!hitsResult.IsSuccess)
        {
            return Result<NoteSearchResponseDto>.Failure(hitsResult.Error!.Value);
        }

        var items = hitsResult.Value!
            .Select(h => new NoteSearchItemDto(
                Id: h.Note.Id,
                Title: h.Note.Title,
                Area: h.Note.Area,
                Tags: h.Note.Tags,
                Provenance: new ProvenanceDto(
                    h.Note.Provenance.Kind.ToWireString(),
                    h.Note.Provenance.Date,
                    h.Note.Provenance.Qualifier),
                Status: h.Note.Status.ToWireString(),
                VersionToken: h.Note.VersionToken,
                Snippet: h.Snippet,
                Score: h.Score,
                UpdatedAtUtc: h.Note.UpdatedAtUtc))
            .ToArray();

        return Result<NoteSearchResponseDto>.Success(new NoteSearchResponseDto(items));
    }
}
