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
public sealed class CriteriaController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    // effectiveTo is nullable (open-ended criteria): null sorts last in both directions via the stored has_effective_to flag.
    private static readonly ODataOrdering<CriterionReadModel> Ordering =
        new ODataOrdering<CriterionReadModel>("Criteria", c => c.Id, q => q.OrderBy(c => c.Name))
            .Add("name", c => c.Name)
            .Add("category", c => c.Category)
            .Add("effectiveFrom", c => c.EffectiveFrom)
            .AddCustom("effectiveTo", (q, desc) => q.OrderByEffectiveTo(desc), (q, desc) => q.ThenByEffectiveTo(desc))
            .Add("isActive", c => c.IsActive);

    // Filterable: name, category, effectiveFrom, effectiveTo, isActive.
    private static readonly HashSet<string> DisallowedFilter = new(StringComparer.OrdinalIgnoreCase)
    {
        "ruleText"
    };

    private static readonly ODataValidationSettings ValidationSettings =
        ODataQueryPipeline.CreateDefaultValidationSettings(Ordering.Names);

    public CriteriaController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<CriterionReadModel> options)
    {
        var ownerId = _ownerIdProvider.GetOwnerId();

        return ODataQueryPipeline.ExecuteQuery(
            options,
            _readSeam.QueryCriteria(ownerId),
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
