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
public sealed class PostingsController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    private static readonly string[] AllowedOrderBy =
    [
        "title",
        "companyName",
        "location",
        "disposition",
        "resurfacePolicy",
        "score",
        "createdAt",
        "updatedAt",
        "id"
    ];

    private static readonly ODataValidationSettings ValidationSettings =
        ODataQueryPipeline.CreateDefaultValidationSettings(AllowedOrderBy);

    public PostingsController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<PostingReadModel> options)
    {
        var ownerId = _ownerIdProvider.GetOwnerId();
        var baseQuery = _readSeam.QueryPostings(ownerId);

        return ODataQueryPipeline.ExecuteQuery(
            options,
            baseQuery,
            ValidationSettings,
            ApplyPostingsOrdering);
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

    private static IQueryable<PostingReadModel> ApplyPostingsOrdering(
        IQueryable<PostingReadModel> source,
        OrderByQueryOption? orderBy)
    {
        if (orderBy is null || !orderBy.OrderByNodes.Any())
        {
            return source.OrderBy(p => p.Id);
        }

        IOrderedQueryable<PostingReadModel>? ordered = null;

        var plan = ODataOrderPlan.From(orderBy);
        foreach (var propNode in plan.Nodes)
        {
            var isDesc = propNode.Direction == OrderByDirection.Descending;
            var prop = propNode.Property.Name;

            switch (prop.ToLowerInvariant())
            {
                case "disposition":
                    ordered = ordered is null
                        ? source.OrderByDisposition(isDesc)
                        : ordered.ThenByDisposition(isDesc);
                    break;

                case "score":
                    ordered = ordered is null
                        ? source.OrderByScore(isDesc)
                        : ordered.ThenByScore(isDesc);
                    break;

                case "title":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.Title) : source.OrderBy(p => p.Title))
                        : (isDesc ? ordered.ThenByDescending(p => p.Title) : ordered.ThenBy(p => p.Title));
                    break;

                case "companyname":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.CompanyName) : source.OrderBy(p => p.CompanyName))
                        : (isDesc ? ordered.ThenByDescending(p => p.CompanyName) : ordered.ThenBy(p => p.CompanyName));
                    break;

                case "location":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.Location) : source.OrderBy(p => p.Location))
                        : (isDesc ? ordered.ThenByDescending(p => p.Location) : ordered.ThenBy(p => p.Location));
                    break;

                case "resurfacepolicy":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.ResurfacePolicy) : source.OrderBy(p => p.ResurfacePolicy))
                        : (isDesc ? ordered.ThenByDescending(p => p.ResurfacePolicy) : ordered.ThenBy(p => p.ResurfacePolicy));
                    break;

                case "createdat":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.CreatedAt) : source.OrderBy(p => p.CreatedAt))
                        : (isDesc ? ordered.ThenByDescending(p => p.CreatedAt) : ordered.ThenBy(p => p.CreatedAt));
                    break;

                case "updatedat":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.UpdatedAt) : source.OrderBy(p => p.UpdatedAt))
                        : (isDesc ? ordered.ThenByDescending(p => p.UpdatedAt) : ordered.ThenBy(p => p.UpdatedAt));
                    break;

                case "id":
                    ordered = ordered is null
                        ? (isDesc ? source.OrderByDescending(p => p.Id) : source.OrderBy(p => p.Id))
                        : (isDesc ? ordered.ThenByDescending(p => p.Id) : ordered.ThenBy(p => p.Id));
                    break;

                default:
                    throw new ODataException($"Property '{prop}' is not orderable on Postings.");
            }
        }

        return ordered is null
            ? source.OrderBy(p => p.Id)
            : (plan.SortsById ? ordered : ordered.ThenBy(p => p.Id));
    }
}
