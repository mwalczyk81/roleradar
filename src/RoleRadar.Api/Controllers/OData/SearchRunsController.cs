namespace RoleRadar.Api.Controllers.OData;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using RoleRadar.Api.Authentication;
using RoleRadar.Api.OData;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

[Authorize(Policy = Program.ReadPolicyName)]
public sealed class SearchRunsController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    // Newest run first when the client sends no $orderby.
    private static readonly ODataOrdering<SearchRunReadModel> Ordering =
        new ODataOrdering<SearchRunReadModel>("SearchRuns", r => r.Id, q => q.OrderByDescending(r => r.RunDate))
            .Add("runDate", r => r.RunDate)
            .Add("newPostings", r => r.NewPostings);

    // Filterable: runDate, newPostings. The other counters are constants derived in memory (not stored).
    private static readonly HashSet<string> DisallowedFilter = new(StringComparer.OrdinalIgnoreCase)
    {
        "companiesSearched",
        "postingsFound",
        "errorsCount",
        "summary"
    };

    private static readonly ODataValidationSettings ValidationSettings =
        ODataQueryPipeline.CreateDefaultValidationSettings(Ordering.Names);

    public SearchRunsController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<SearchRunReadModel> options)
    {
        var ownerId = _ownerIdProvider.GetOwnerId();

        return ODataQueryPipeline.ExecuteQuery(
            options,
            _readSeam.QuerySearchRuns(ownerId),
            ValidationSettings,
            Ordering.Apply,
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
}
