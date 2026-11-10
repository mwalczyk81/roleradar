using RoleRadar.Api.Authentication;
using RoleRadar.Api.Models;
using RoleRadar.Api.Problems;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Api.Endpoints;

public static class PostingsEndpoints
{
    public static RouteGroupBuilder MapPostingsEndpoints(this RouteGroupBuilder group)
    {
        var readGroup = group.MapGroup(string.Empty)
            .RequireAuthorization(Program.ReadPolicyName);
        var writeGroup = group.MapGroup(string.Empty)
            .RequireAuthorization(Program.WritePolicyName);

        readGroup.MapGet("/postings/{id:guid}", GetPostingByIdAsync)
            .Produces<PostingDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        writeGroup.MapPost("/postings/{id:guid}/transition", TransitionPostingAsync)
            .Produces<PostingDetailDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return group;
    }

    private static async Task<IResult> GetPostingByIdAsync(
        Guid id,
        IOwnerIdProvider ownerIdProvider,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        INoteRepository noteRepository,
        IApplicationRepository applicationRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        return await BuildPostingDetailResultAsync(
            ownerId,
            id,
            postingRepository,
            companyRepository,
            noteRepository,
            applicationRepository,
            cancellationToken);
    }

    private static async Task<IResult> TransitionPostingAsync(
        Guid id,
        TransitionPostingRequestDto request,
        IOwnerIdProvider ownerIdProvider,
        HttpActorAccessor actor,
        ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> commandHandler,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        INoteRepository noteRepository,
        IApplicationRepository applicationRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;

        if (!Guid.TryParse(request.VersionToken, out var parsedVersionToken) || parsedVersionToken == Guid.Empty)
        {
            return ApiProblems.Validation("A valid non-empty versionToken UUID is required.");
        }

        var command = new TransitionPostingCommand(
            PostingId: id,
            TargetDisposition: request.ToDisposition,
            VersionToken: parsedVersionToken,
            Reason: request.Reason,
            CriterionId: request.CriterionId,
            ResurfacePolicy: request.ResurfacePolicy,
            OverrideReason: request.OverrideReason,
            EventDate: request.EventDate,
            Actor: actor.Subject,
            OwnerId: ownerId);

        var result = await commandHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiProblems.FromResultError(result.Error);
        }

        // The warning text comes from the Application layer; the API only maps it.
        var warnings = result.Value!.Warnings
            .Select(w => new PostingWarningDto(w.Code, w.Message))
            .ToArray();

        return await BuildPostingDetailResultAsync(
            ownerId,
            id,
            postingRepository,
            companyRepository,
            noteRepository,
            applicationRepository,
            cancellationToken,
            warnings);
    }

    private static async Task<IResult> BuildPostingDetailResultAsync(
        string ownerId,
        Guid postingId,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        INoteRepository noteRepository,
        IApplicationRepository applicationRepository,
        CancellationToken cancellationToken,
        IReadOnlyList<PostingWarningDto>? warnings = null)
    {
        var postingRes = await postingRepository.GetByIdAsync(ownerId, postingId, cancellationToken);
        if (!postingRes.IsSuccess || postingRes.Value is null)
        {
            return ApiProblems.NotFound("Posting not found.");
        }

        var posting = postingRes.Value;
        var companyRes = await companyRepository.GetByIdAsync(ownerId, posting.CompanyId, cancellationToken);
        var companyName = companyRes.IsSuccess && companyRes.Value is not null
            ? companyRes.Value.Name
            : string.Empty;

        var signalsRes = await postingRepository.GetSignalsAsync(ownerId, posting.Id, cancellationToken);
        var signals = signalsRes.IsSuccess && signalsRes.Value is not null
            ? signalsRes.Value.Select(s => new PostingSignalDto(
                SignalType: s.SignalType.ToSnakeCase(),
                Evidence: s.Evidence,
                DetectedAt: s.ObservedAt)).ToArray()
            : [];

        AssessmentNoteSummaryDto? assessmentNote = null;
        if (posting.ScreeningNoteId.HasValue)
        {
            var noteRes = await noteRepository.GetByIdAsync(ownerId, posting.ScreeningNoteId.Value, cancellationToken);
            if (noteRes.IsSuccess && noteRes.Value is not null)
            {
                var n = noteRes.Value;
                assessmentNote = new AssessmentNoteSummaryDto(
                    Id: n.Id,
                    Summary: n.Title,
                    UpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(n.UpdatedAtUtc, DateTimeKind.Utc)));
            }
        }

        // One owner-scoped lookup. A failed lookup reads as "no application" rather than failing the read.
        var applicationRes = await applicationRepository.GetByPostingIdAsync(ownerId, posting.Id, cancellationToken);
        var applicationId = applicationRes.IsSuccess ? applicationRes.Value?.Id : null;

        var hasSalary = posting.Compensation.Min.HasValue ||
                        posting.Compensation.Max.HasValue ||
                        !string.IsNullOrWhiteSpace(posting.Compensation.Text);

        var detail = new PostingDetailDto(
            Id: posting.Id,
            CompanyId: posting.CompanyId,
            CompanyName: companyName,
            Title: posting.Title,
            RequisitionId: posting.RequisitionId,
            Location: posting.Location,
            Disposition: posting.Disposition.ToSnakeCase(),
            ResurfacePolicy: posting.ResurfacePolicy.ToSnakeCase(),
            Score: posting.Score,
            VersionToken: posting.VersionToken,
            CreatedAt: posting.CreatedAt,
            UpdatedAt: posting.UpdatedAt,
            Url: posting.OriginalUrl,
            Compensation: new PostingCompensationDto(
                Currency: posting.Compensation.Currency,
                RawText: posting.Compensation.Text,
                HasSalary: hasSalary,
                CompHighPriority: posting.CompHighPriority),
            Signals: signals,
            AssessmentNote: assessmentNote,
            Warnings: warnings,
            ApplicationId: applicationId);

        return Results.Ok(detail);
    }
}
