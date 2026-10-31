using RoleRadar.Api.Envelopes;
using RoleRadar.Api.Models;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Api.Endpoints;

public static class ApplicationsEndpoints
{
    public const string ApiActor = "api-user";

    public static RouteGroupBuilder MapApplicationsEndpoints(this RouteGroupBuilder group)
    {
        var readGroup = group.MapGroup(string.Empty)
            .RequireAuthorization(Program.ReadPolicyName);
        var writeGroup = group.MapGroup(string.Empty)
            .RequireAuthorization(Program.WritePolicyName);

        readGroup.MapGet("/applications", ListApplicationsAsync)
            .Produces<PagedResult<ApplicationDetailDto>>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest);
        readGroup.MapGet("/applications/{id:guid}", GetApplicationByIdAsync)
            .Produces<ApplicationDetailDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound);
        writeGroup.MapPost("/applications", CreateApplicationAsync)
            .Produces<ApplicationDetailDto>(StatusCodes.Status201Created)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<ErrorEnvelope>(StatusCodes.Status409Conflict);
        writeGroup.MapPost("/applications/{id:guid}/events", AppendApplicationEventAsync)
            .Produces<ApplicationDetailDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound);
        return group;
    }

    private static async Task<IResult> ListApplicationsAsync(
        string? status,
        int? limit,
        int? offset,
        IOwnerIdProvider ownerIdProvider,
        IApplicationRepository applicationRepository,
        IPostingRepository postingRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var clampedLimit = Math.Clamp(limit is null or <= 0 ? 50 : limit.Value, 1, 200);
        var clampedOffset = Math.Max(0, offset ?? 0);

        var listRes = await applicationRepository.ListApplicationsAsync(
            ownerId,
            outcome: string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            page: 1,
            pageSize: 1000,
            cancellationToken);

        if (!listRes.IsSuccess || listRes.Value is null)
        {
            return ErrorEnvelope.FromResultError(listRes.Error);
        }

        var allApps = listRes.Value;
        var totalCount = allApps.Count;
        var pagedApps = allApps.Skip(clampedOffset).Take(clampedLimit).ToList();

        var items = new List<ApplicationDetailDto>(pagedApps.Count);
        foreach (var app in pagedApps)
        {
            var dto = await BuildApplicationDtoAsync(ownerId, app, applicationRepository, postingRepository, cancellationToken);
            items.Add(dto);
        }

        return Results.Ok(new PagedResult<ApplicationDetailDto>(
            Items: items,
            TotalCount: totalCount,
            Limit: clampedLimit,
            Offset: clampedOffset));
    }

    private static async Task<IResult> GetApplicationByIdAsync(
        Guid id,
        IOwnerIdProvider ownerIdProvider,
        IApplicationRepository applicationRepository,
        IPostingRepository postingRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var appRes = await applicationRepository.GetByIdAsync(ownerId, id, cancellationToken);
        if (!appRes.IsSuccess || appRes.Value is null)
        {
            return Results.NotFound(ErrorEnvelope.NotFound("Application not found."));
        }

        var dto = await BuildApplicationDtoAsync(ownerId, appRes.Value, applicationRepository, postingRepository, cancellationToken);
        return Results.Ok(dto);
    }

    private static async Task<IResult> CreateApplicationAsync(
        RecordApplicationRequestDto request,
        IOwnerIdProvider ownerIdProvider,
        ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto> commandHandler,
        IApplicationRepository applicationRepository,
        IPostingRepository postingRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;

        var command = new RecordApplicationCommand(
            PostingId: request.PostingId,
            Action: "create",
            SubmittedDate: request.SubmittedDate,
            DesiredSalaryMin: request.DesiredSalaryMin,
            DesiredSalaryMax: request.DesiredSalaryMax,
            DesiredSalaryCurrency: request.DesiredSalaryCurrency ?? "USD",
            TargetStartDate: request.TargetStartDate,
            ResumeVariant: string.IsNullOrWhiteSpace(request.ResumeVariant) ? "tailored" : request.ResumeVariant,
            ResumePointer: request.ResumePointer,
            ReferralContactId: null,
            ConfirmationCode: request.ConfirmationCode,
            OverrideDecided: request.OverrideDecided,
            OverrideReason: request.OverrideReason,
            Event: null,
            Actor: ApiActor,
            OwnerId: ownerId);

        var result = await commandHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        var appRes = await applicationRepository.GetByIdAsync(ownerId, result.Value.ApplicationId, cancellationToken);
        if (!appRes.IsSuccess || appRes.Value is null)
        {
            return Results.NotFound(ErrorEnvelope.NotFound("Created application could not be retrieved."));
        }

        var dto = await BuildApplicationDtoAsync(ownerId, appRes.Value, applicationRepository, postingRepository, cancellationToken);
        return Results.Created($"/api/v1/applications/{dto.Id}", dto);
    }

    private static async Task<IResult> AppendApplicationEventAsync(
        Guid id,
        AppendApplicationEventRequestDto request,
        IOwnerIdProvider ownerIdProvider,
        ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto> commandHandler,
        IApplicationRepository applicationRepository,
        IPostingRepository postingRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;

        var existingRes = await applicationRepository.GetByIdAsync(ownerId, id, cancellationToken);
        if (!existingRes.IsSuccess || existingRes.Value is null)
        {
            return Results.NotFound(ErrorEnvelope.NotFound("Application not found."));
        }

        var existing = existingRes.Value;
        var command = new RecordApplicationCommand(
            PostingId: existing.PostingId,
            Action: "append_event",
            Event: new ApplicationEventInputDto(
                EventType: request.EventType,
                EventDate: request.EventDate,
                EventSource: request.EventSource ?? "api",
                Notes: request.Notes),
            Actor: ApiActor,
            OwnerId: ownerId);

        var result = await commandHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        var updatedRes = await applicationRepository.GetByIdAsync(ownerId, id, cancellationToken);
        var dto = await BuildApplicationDtoAsync(
            ownerId,
            updatedRes.Value ?? existing,
            applicationRepository,
            postingRepository,
            cancellationToken);

        return Results.Ok(dto);
    }

    private static async Task<ApplicationDetailDto> BuildApplicationDtoAsync(
        string ownerId,
        ApplicationEntity app,
        IApplicationRepository applicationRepository,
        IPostingRepository postingRepository,
        CancellationToken cancellationToken)
    {
        var postingRes = await postingRepository.GetByIdAsync(ownerId, app.PostingId, cancellationToken);
        var companyId = postingRes.IsSuccess && postingRes.Value is not null
            ? postingRes.Value.CompanyId
            : Guid.Empty;

        var eventsRes = await applicationRepository.GetEventsAsync(ownerId, app.Id, cancellationToken);
        var events = eventsRes.IsSuccess && eventsRes.Value is not null
            ? eventsRes.Value.Select(e => new ApplicationEventDto(
                EventType: e.EventType.ToSnakeCase(),
                EventDate: e.EventDate,
                Notes: e.Notes)).ToArray()
            : [];

        return new ApplicationDetailDto(
            Id: app.Id,
            PostingId: app.PostingId,
            CompanyId: companyId,
            Status: app.Outcome.ToSnakeCase(),
            AppliedAt: app.SubmittedDate,
            Events: events);
    }
}
