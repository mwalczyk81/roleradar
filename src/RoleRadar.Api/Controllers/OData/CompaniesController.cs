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
public sealed class CompaniesController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    private static readonly ODataOrdering<CompanyReadModel> Ordering =
        new ODataOrdering<CompanyReadModel>("Companies", c => c.Id, q => q.OrderBy(c => c.Name))
            .Add("name", c => c.Name)
            .Add("status", c => c.Status)
            .Add("ownershipType", c => c.OwnershipType);

    // Filterable: name, status, ownershipType, headquarters.
    private static readonly HashSet<string> DisallowedFilter = new(StringComparer.OrdinalIgnoreCase)
    {
        "description",
        "categoryTags"
    };

    private static readonly ODataValidationSettings ValidationSettings =
        ODataQueryPipeline.CreateDefaultValidationSettings(Ordering.Names);

    public CompaniesController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<CompanyReadModel> options)
    {
        var ownerId = _ownerIdProvider.GetOwnerId();

        return ODataQueryPipeline.ExecuteQuery(
            options,
            _readSeam.QueryCompanies(ownerId),
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
