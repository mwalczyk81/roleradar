using RoleRadar.Api.Envelopes;
using RoleRadar.Api.Models;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Common;

namespace RoleRadar.Api.Endpoints;

public static class AncillaryEndpoints
{
    public static RouteGroupBuilder MapAncillaryEndpoints(this RouteGroupBuilder group)
    {
        var readGroup = group.MapGroup(string.Empty)
            .RequireAuthorization(Program.ReadPolicyName);

        readGroup.MapGet("/notes/{id:guid}", GetNoteByIdAsync)
            .Produces<NoteDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound);
        return group;
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

}
