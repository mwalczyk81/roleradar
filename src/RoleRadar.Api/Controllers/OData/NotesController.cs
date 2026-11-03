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
public sealed class NotesController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    // Most recently updated first when the client sends no $orderby.
    private static readonly ODataOrdering<NoteReadModel> Ordering =
        new ODataOrdering<NoteReadModel>("Notes", n => n.Id, q => q.OrderByDescending(n => n.UpdatedAt))
            .Add("heading", n => n.Heading)
            .Add("createdAt", n => n.CreatedAt)
            .Add("updatedAt", n => n.UpdatedAt);

    // Filterable: heading, linkedEntityType, createdAt, updatedAt. The (up to 8 MiB) body is never filterable.
    private static readonly HashSet<string> DisallowedFilter = new(StringComparer.OrdinalIgnoreCase)
    {
        "content",
        "tags",
        "linkedEntityId"
    };

    private static readonly ODataValidationSettings ValidationSettings =
        ODataQueryPipeline.CreateDefaultValidationSettings(Ordering.Names);

    public NotesController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<NoteReadModel> options)
    {
        var ownerId = _ownerIdProvider.GetOwnerId();

        // The body is only read from MongoDB when $select explicitly names it.
        var includeContent = ExplicitlySelectsContent(options);

        return ODataQueryPipeline.ExecuteQuery(
            options,
            _readSeam.QueryNotes(ownerId, includeContent),
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

    private static bool ExplicitlySelectsContent(ODataQueryOptions<NoteReadModel> options)
    {
        var raw = options.SelectExpand?.RawSelect;
        return !string.IsNullOrWhiteSpace(raw)
            && raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(part => string.Equals(part, "content", StringComparison.OrdinalIgnoreCase));
    }
}
