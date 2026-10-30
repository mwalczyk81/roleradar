using RoleRadar.Api.Envelopes;
using RoleRadar.Api.Models;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Domain.Common;

namespace RoleRadar.Api.Endpoints;

public static class AncillaryEndpoints
{
    public static RouteGroupBuilder MapAncillaryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/criteria", GetCriteriaAsync);
        group.MapGet("/notes/{id:guid}", GetNoteByIdAsync);
        group.MapGet("/notes", SearchNotesAsync);
        group.MapGet("/search-runs", ListSearchRunsAsync);
        return group;
    }

    private static async Task<IResult> GetCriteriaAsync(
        string? effectiveDate,
        IOwnerIdProvider ownerIdProvider,
        IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto> queryHandler,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var result = await queryHandler.HandleAsync(
            new TrackerQueryRequest(
                Entity: "criteria",
                EffectiveDate: effectiveDate,
                Page: 1,
                PageSize: 200,
                OwnerId: ownerId),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        var dtos = result.Value.Items
            .OfType<CriterionSummaryItemDto>()
            .Select(c => new CriterionDto(
                Id: c.Id,
                Name: c.Key,
                Weight: c.Category,
                RuleText: c.Value,
                EffectiveFrom: c.EffectiveFrom,
                EffectiveTo: c.EffectiveTo,
                IsActive: c.IsActive))
            .ToArray();

        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetNoteByIdAsync(
        Guid id,
        IOwnerIdProvider ownerIdProvider,
        IQueryHandler<GetNoteQuery, NoteDetailDto> queryHandler,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var result = await queryHandler.HandleAsync(
            new GetNoteQuery(Id: id, OwnerId: ownerId),
            cancellationToken);

        if (!result.IsSuccess || result.Value is null)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        var n = result.Value;
        var dto = new NoteDto(
            Id: n.Id,
            Heading: n.Title,
            Content: n.Body,
            Tags: n.Tags,
            LinkedEntityType: n.Area,
            LinkedEntityId: null,
            CreatedAt: new DateTimeOffset(DateTime.SpecifyKind(n.CreatedAtUtc, DateTimeKind.Utc)),
            UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(n.UpdatedAtUtc, DateTimeKind.Utc)));

        return Results.Ok(dto);
    }

    private static async Task<IResult> SearchNotesAsync(
        string? q,
        int? limit,
        int? offset,
        IOwnerIdProvider ownerIdProvider,
        IQueryHandler<SearchNotesQuery, NoteSearchResponseDto> queryHandler,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.BadRequest(ErrorEnvelope.Validation("Query parameter 'q' is required."));
        }

        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var clampedLimit = Math.Clamp(limit is null or <= 0 ? 50 : limit.Value, 1, 200);
        var clampedOffset = Math.Max(0, offset ?? 0);

        var searchResult = await queryHandler.HandleAsync(
            new SearchNotesQuery(Query: q.Trim(), Limit: 100, OwnerId: ownerId),
            cancellationToken);

        if (!searchResult.IsSuccess || searchResult.Value is null)
        {
            return ErrorEnvelope.FromResultError(searchResult.Error);
        }

        var allHits = searchResult.Value.Results;
        var totalCount = allHits.Count;
        var items = allHits
            .Skip(clampedOffset)
            .Take(clampedLimit)
            .Select(h =>
            {
                var updatedAt = new DateTimeOffset(DateTime.SpecifyKind(h.UpdatedAtUtc, DateTimeKind.Utc));
                return new NoteDto(
                    Id: h.Id,
                    Heading: h.Title,
                    Content: h.Snippet,
                    Tags: h.Tags,
                    LinkedEntityType: h.Area,
                    LinkedEntityId: null,
                    CreatedAt: updatedAt,
                    UpdatedAt: updatedAt);
            })
            .ToArray();

        return Results.Ok(new PagedResult<NoteDto>(
            Items: items,
            TotalCount: totalCount,
            Limit: clampedLimit,
            Offset: clampedOffset));
    }

    private static async Task<IResult> ListSearchRunsAsync(
        int? limit,
        int? offset,
        IOwnerIdProvider ownerIdProvider,
        ISearchRunRepository searchRunRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var clampedLimit = Math.Clamp(limit is null or <= 0 ? 50 : limit.Value, 1, 200);
        var clampedOffset = Math.Max(0, offset ?? 0);

        var listRes = await searchRunRepository.ListSearchRunsAsync(
            ownerId,
            page: 1,
            pageSize: 1000,
            cancellationToken);

        if (!listRes.IsSuccess || listRes.Value is null)
        {
            return ErrorEnvelope.FromResultError(listRes.Error);
        }

        var allRuns = listRes.Value;
        var totalCount = allRuns.Count;
        var items = allRuns
            .Skip(clampedOffset)
            .Take(clampedLimit)
            .Select(r => new SearchRunDto(
                Id: r.Id,
                RunDate: r.StartedAt,
                CompaniesSearched: 0,
                PostingsFound: r.NewPostingsCount,
                NewPostings: r.NewPostingsCount,
                ErrorsCount: 0,
                Summary: r.Notes))
            .ToArray();

        return Results.Ok(new PagedResult<SearchRunDto>(
            Items: items,
            TotalCount: totalCount,
            Limit: clampedLimit,
            Offset: clampedOffset));
    }
}
