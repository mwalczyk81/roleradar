using RoleRadar.Api.Envelopes;
using RoleRadar.Api.Models;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Api.Endpoints;

public static class PostingsEndpoints
{
    public const string ApiActor = "api-user";

    public static RouteGroupBuilder MapPostingsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/postings", GetPostingsAsync)
            .Produces<PagedResult<PostingListItemDto>>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest);
        group.MapGet("/postings/{id:guid}", GetPostingByIdAsync)
            .Produces<PostingDetailDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound);
        group.MapPost("/postings/{id:guid}/transition", TransitionPostingAsync)
            .Produces<PostingDetailDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound)
            .Produces<ErrorEnvelope>(StatusCodes.Status409Conflict);
        return group;
    }

    private static async Task<IResult> GetPostingsAsync(
        string? disposition,
        Guid? companyId,
        string? resurfacePolicy,
        decimal? minScore,
        string? q,
        int? limit,
        int? offset,
        IOwnerIdProvider ownerIdProvider,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var clampedLimit = Math.Clamp(limit is null or <= 0 ? 50 : limit.Value, 1, 200);
        var clampedOffset = Math.Max(0, offset ?? 0);

        PostingDisposition? parsedDisposition = null;
        if (!string.IsNullOrWhiteSpace(disposition))
        {
            if (!TrackingEnumExtensions.TryParseDisposition(disposition, out var disp))
            {
                return Results.BadRequest(ErrorEnvelope.Validation($"Invalid disposition '{disposition}'."));
            }
            parsedDisposition = disp;
        }

        ResurfacePolicy? parsedResurface = null;
        if (!string.IsNullOrWhiteSpace(resurfacePolicy))
        {
            if (!TrackingEnumExtensions.TryParseResurfacePolicy(resurfacePolicy, out var rp))
            {
                return Results.BadRequest(ErrorEnvelope.Validation($"Invalid resurfacePolicy '{resurfacePolicy}'."));
            }
            parsedResurface = rp;
        }

        var listRes = await postingRepository.ListPostingsAsync(
            ownerId,
            companyId,
            parsedDisposition,
            page: 1,
            pageSize: 1000,
            cancellationToken);

        if (!listRes.IsSuccess || listRes.Value is null)
        {
            return ErrorEnvelope.FromResultError(listRes.Error);
        }

        var companiesRes = await companyRepository.ListAsync(ownerId, status: null, cancellationToken);
        var companyNames = companiesRes.IsSuccess && companiesRes.Value is not null
            ? companiesRes.Value.ToDictionary(c => c.Id, c => c.Name)
            : new Dictionary<Guid, string>();

        IEnumerable<Posting> filtered = listRes.Value;

        if (parsedResurface.HasValue)
        {
            filtered = filtered.Where(p => p.ResurfacePolicy == parsedResurface.Value);
        }

        if (minScore.HasValue)
        {
            filtered = filtered.Where(p => p.Score.HasValue && p.Score.Value >= minScore.Value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var queryText = q.Trim();
            filtered = filtered.Where(p =>
                p.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase) ||
                (p.Location is not null && p.Location.Contains(queryText, StringComparison.OrdinalIgnoreCase)) ||
                (p.RequisitionId is not null && p.RequisitionId.Contains(queryText, StringComparison.OrdinalIgnoreCase)));
        }

        var materialized = filtered.ToList();
        var totalCount = materialized.Count;
        var pageItems = materialized
            .Skip(clampedOffset)
            .Take(clampedLimit)
            .Select(p => MapToListItemDto(p, companyNames.GetValueOrDefault(p.CompanyId, string.Empty)))
            .ToArray();

        return Results.Ok(new PagedResult<PostingListItemDto>(
            Items: pageItems,
            TotalCount: totalCount,
            Limit: clampedLimit,
            Offset: clampedOffset));
    }

    private static async Task<IResult> GetPostingByIdAsync(
        Guid id,
        IOwnerIdProvider ownerIdProvider,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        INoteRepository noteRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        return await BuildPostingDetailResultAsync(
            ownerId,
            id,
            postingRepository,
            companyRepository,
            noteRepository,
            cancellationToken);
    }

    private static async Task<IResult> TransitionPostingAsync(
        Guid id,
        TransitionPostingRequestDto request,
        IOwnerIdProvider ownerIdProvider,
        ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> commandHandler,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        INoteRepository noteRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;

        if (!Guid.TryParse(request.VersionToken, out var parsedVersionToken) || parsedVersionToken == Guid.Empty)
        {
            return Results.BadRequest(ErrorEnvelope.Validation("A valid non-empty versionToken UUID is required."));
        }

        var command = new TransitionPostingCommand(
            PostingId: id,
            TargetDisposition: request.ToDisposition,
            VersionToken: parsedVersionToken,
            Reason: request.Reason,
            CriterionId: request.CriterionId,
            ResurfacePolicy: request.ResurfacePolicy,
            OverrideReason: request.OverrideReason,
            Actor: ApiActor,
            OwnerId: ownerId);

        var result = await commandHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        return await BuildPostingDetailResultAsync(
            ownerId,
            id,
            postingRepository,
            companyRepository,
            noteRepository,
            cancellationToken);
    }

    private static async Task<IResult> BuildPostingDetailResultAsync(
        string ownerId,
        Guid postingId,
        IPostingRepository postingRepository,
        ICompanyRepository companyRepository,
        INoteRepository noteRepository,
        CancellationToken cancellationToken)
    {
        var postingRes = await postingRepository.GetByIdAsync(ownerId, postingId, cancellationToken);
        if (!postingRes.IsSuccess || postingRes.Value is null)
        {
            return Results.NotFound(ErrorEnvelope.NotFound("Posting not found."));
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
            AssessmentNote: assessmentNote);

        return Results.Ok(detail);
    }

    private static PostingListItemDto MapToListItemDto(Posting p, string companyName) =>
        new(
            Id: p.Id,
            CompanyId: p.CompanyId,
            CompanyName: companyName,
            Title: p.Title,
            RequisitionId: p.RequisitionId,
            Location: p.Location,
            Disposition: p.Disposition.ToSnakeCase(),
            ResurfacePolicy: p.ResurfacePolicy.ToSnakeCase(),
            Score: p.Score,
            VersionToken: p.VersionToken,
            CreatedAt: p.CreatedAt,
            UpdatedAt: p.UpdatedAt);
}
