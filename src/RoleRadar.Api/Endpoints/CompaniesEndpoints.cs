using RoleRadar.Api.Envelopes;
using RoleRadar.Api.Models;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Api.Endpoints;

public static class CompaniesEndpoints
{
    public const string ApiActor = "api-user";

    public static RouteGroupBuilder MapCompaniesEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/companies", ListCompaniesAsync)
            .Produces<PagedResult<CompanyDetailDto>>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest);
        group.MapGet("/companies/{id:guid}", GetCompanyByIdAsync)
            .Produces<CompanyDetailDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound);
        group.MapPatch("/companies/{id:guid}", PatchCompanyAsync)
            .Produces<CompanyDetailDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest)
            .Produces<ErrorEnvelope>(StatusCodes.Status404NotFound)
            .Produces<ErrorEnvelope>(StatusCodes.Status409Conflict);
        return group;
    }

    private static async Task<IResult> ListCompaniesAsync(
        int? limit,
        int? offset,
        IOwnerIdProvider ownerIdProvider,
        ICompanyRepository companyRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var clampedLimit = Math.Clamp(limit is null or <= 0 ? 50 : limit.Value, 1, 200);
        var clampedOffset = Math.Max(0, offset ?? 0);

        var listRes = await companyRepository.ListAsync(ownerId, status: null, cancellationToken);
        if (!listRes.IsSuccess || listRes.Value is null)
        {
            return ErrorEnvelope.FromResultError(listRes.Error);
        }

        var allCompanies = listRes.Value;
        var totalCount = allCompanies.Count;
        var pageCompanies = allCompanies.Skip(clampedOffset).Take(clampedLimit).ToList();

        var items = new List<CompanyDetailDto>(pageCompanies.Count);
        foreach (var company in pageCompanies)
        {
            var sourcesRes = await companyRepository.GetCareerSourcesAsync(ownerId, company.Id, includeInactive: true, cancellationToken);
            var sources = sourcesRes.IsSuccess && sourcesRes.Value is not null
                ? sourcesRes.Value.Select(MapCareerSource).ToArray()
                : [];

            items.Add(MapCompanyDetail(company, sources));
        }

        return Results.Ok(new PagedResult<CompanyDetailDto>(
            Items: items,
            TotalCount: totalCount,
            Limit: clampedLimit,
            Offset: clampedOffset));
    }

    private static async Task<IResult> GetCompanyByIdAsync(
        Guid id,
        IOwnerIdProvider ownerIdProvider,
        ICompanyRepository companyRepository,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;
        return await BuildCompanyDetailResultAsync(ownerId, id, companyRepository, cancellationToken);
    }

    private static async Task<IResult> PatchCompanyAsync(
        Guid id,
        UpdateCompanyRequestDto request,
        IOwnerIdProvider ownerIdProvider,
        ICompanyRepository companyRepository,
        ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto> commandHandler,
        CancellationToken cancellationToken)
    {
        var ownerId = ownerIdProvider.GetOwnerId().Value;

        var existingRes = await companyRepository.GetByIdAsync(ownerId, id, cancellationToken);
        if (!existingRes.IsSuccess || existingRes.Value is null)
        {
            return Results.NotFound(ErrorEnvelope.NotFound("Company not found."));
        }

        var existing = existingRes.Value;

        IReadOnlyList<CareerSourceInputDto>? careerSources = null;
        if (request.CareerSources is not null)
        {
            careerSources = request.CareerSources
                .Select(cs => new CareerSourceInputDto(
                    Id: cs.Id,
                    AtsType: cs.AtsProvider ?? cs.SourceType ?? "custom",
                    Tenant: cs.AtsTenant,
                    Slug: cs.SourceUrl,
                    FetchMethod: cs.FetchMethod,
                    Notes: null,
                    IsActive: cs.Active,
                    VersionToken: cs.VersionToken))
                .ToArray();
        }

        var command = new RecordCompanyCommand(
            Id: id,
            Name: string.IsNullOrWhiteSpace(request.Name) ? existing.Name : request.Name,
            Description: request.Description,
            ParentCompany: null,
            OwnershipType: request.OwnershipType,
            Headquarters: request.Headquarters,
            Status: request.Status,
            CategoryTags: request.CategoryTags,
            CareerSources: careerSources,
            VersionToken: request.VersionToken ?? existing.VersionToken,
            OwnerId: ownerId,
            Actor: ApiActor);

        var result = await commandHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        return await BuildCompanyDetailResultAsync(ownerId, id, companyRepository, cancellationToken);
    }

    private static async Task<IResult> BuildCompanyDetailResultAsync(
        string ownerId,
        Guid companyId,
        ICompanyRepository companyRepository,
        CancellationToken cancellationToken)
    {
        var companyRes = await companyRepository.GetByIdAsync(ownerId, companyId, cancellationToken);
        if (!companyRes.IsSuccess || companyRes.Value is null)
        {
            return Results.NotFound(ErrorEnvelope.NotFound("Company not found."));
        }

        var company = companyRes.Value;
        var sourcesRes = await companyRepository.GetCareerSourcesAsync(ownerId, company.Id, includeInactive: true, cancellationToken);
        var sources = sourcesRes.IsSuccess && sourcesRes.Value is not null
            ? sourcesRes.Value.Select(MapCareerSource).ToArray()
            : [];

        return Results.Ok(MapCompanyDetail(company, sources));
    }

    private static CompanyDetailDto MapCompanyDetail(Company company, IReadOnlyList<CareerSourceDto> sources) =>
        new(
            Id: company.Id,
            Name: company.Name,
            Status: company.Status.ToSnakeCase(),
            OwnershipType: company.OwnershipType.ToSnakeCase(),
            Description: company.Description,
            Headquarters: company.Headquarters,
            CategoryTags: company.CategoryTags,
            CareerSources: sources,
            VersionToken: company.VersionToken);

    private static CareerSourceDto MapCareerSource(CareerSource source) =>
        new(
            SourceType: "ats",
            AtsProvider: source.AtsType,
            AtsTenant: source.Tenant,
            SourceUrl: source.Slug,
            FetchMethod: source.FetchMethod.ToSnakeCase(),
            Active: source.IsActive);
}
