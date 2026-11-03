namespace RoleRadar.Api.Controllers.OData;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.OData;
using Microsoft.OData.UriParser;
using RoleRadar.Api.Authentication;
using RoleRadar.Api.OData;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

[Authorize(Policy = Program.ReadPolicyName)]
public sealed class ApplicationsController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    private static readonly string[] AllowedOrderBy =
    [
        "postingTitle",
        "companyName",
        "status",
        "appliedAt",
        "hasInterview",
        "firstResponseAt",
        "lastEventAt",
        "ageInDays",
        "id"
    ];

    private static readonly HashSet<string> DisallowedFilter = new(StringComparer.OrdinalIgnoreCase)
    {
        "ageInDays"
    };

    private static readonly ODataValidationSettings ValidationSettings =
        ODataQueryPipeline.CreateDefaultValidationSettings(AllowedOrderBy);

    public ApplicationsController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<ApplicationReadModel> options)
    {
        var ownerId = _ownerIdProvider.GetOwnerId();
        var baseQuery = _readSeam.QueryApplications(ownerId);

        return ODataQueryPipeline.ExecuteQuery(
            options,
            baseQuery,
            ValidationSettings,
            ApplyApplicationsOrdering,
            DisallowedFilter);
    }

    [HttpPost]
    [HttpPut]
    [HttpPatch]
    [HttpDelete]
    public IActionResult DisallowedMethods()
    {
        Response.Headers.Append("Allow", "GET");
        return StatusCode(StatusCodes.Status405MethodNotAllowed);
    }

    private static IQueryable<ApplicationReadModel> ApplyApplicationsOrdering(
        IQueryable<ApplicationReadModel> source,
        OrderByQueryOption? orderBy)
    {
        if (orderBy is null || !orderBy.OrderByNodes.Any())
        {
            return source.OrderBy(a => a.Id);
        }

        IOrderedQueryable<ApplicationReadModel>? ordered = null;

        foreach (var node in orderBy.OrderByNodes)
        {
            if (node is not OrderByPropertyNode propNode)
            {
                throw new ODataException("Only direct property sorting is supported in $orderby.");
            }

            var isDesc = propNode.Direction == OrderByDirection.Descending;
            var prop = propNode.Property.Name;

            switch (prop.ToLowerInvariant())
            {
                case "postingtitle":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(a => a.PostingTitle) : source.OrderBy(a => a.PostingTitle))
                        : (isDesc ? ordered.ThenByDescending(a => a.PostingTitle) : ordered.ThenBy(a => a.PostingTitle));
                    break;

                case "companyname":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(a => a.CompanyName) : source.OrderBy(a => a.CompanyName))
                        : (isDesc ? ordered.ThenByDescending(a => a.CompanyName) : ordered.ThenBy(a => a.CompanyName));
                    break;

                case "status":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(a => a.Status) : source.OrderBy(a => a.Status))
                        : (isDesc ? ordered.ThenByDescending(a => a.Status) : ordered.ThenBy(a => a.Status));
                    break;

                case "appliedat":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(a => a.AppliedAt) : source.OrderBy(a => a.AppliedAt))
                        : (isDesc ? ordered.ThenByDescending(a => a.AppliedAt) : ordered.ThenBy(a => a.AppliedAt));
                    break;

                case "ageindays":
                    // AgeInDays is derived from appliedAt: lower ageInDays = more recent application = appliedAt desc
                    var appliedDesc = !isDesc;
                    ordered = ordered is null
                        ? (appliedDesc ? source.OrderByDescending(a => a.AppliedAt) : source.OrderBy(a => a.AppliedAt))
                        : (appliedDesc ? ordered.ThenByDescending(a => a.AppliedAt) : ordered.ThenBy(a => a.AppliedAt));
                    break;

                case "hasinterview":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(a => a.HasInterview) : source.OrderBy(a => a.HasInterview))
                        : (isDesc ? ordered.ThenByDescending(a => a.HasInterview) : ordered.ThenBy(a => a.HasInterview));
                    break;

                case "firstresponseat":
                    ordered = ordered is null
                        ? source.OrderByFirstResponse(isDesc)
                        : ordered.ThenByFirstResponse(isDesc);
                    break;

                case "lasteventat":
                    ordered = ordered is null
                        ? source.OrderByLastEvent(isDesc)
                        : ordered.ThenByLastEvent(isDesc);
                    break;

                case "id":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(a => a.Id) : source.OrderBy(a => a.Id))
                        : (isDesc ? ordered.ThenByDescending(a => a.Id) : ordered.ThenBy(a => a.Id));
                    break;

                default:
                    throw new ODataException($"Property '{prop}' is not orderable on Applications.");
            }
        }

        return ordered is null
            ? source.OrderBy(a => a.Id)
            : ordered.ThenBy(a => a.Id);
    }
}
