namespace RoleRadar.Api.Controllers.OData;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.OData;
using Microsoft.OData.UriParser;
using RoleRadar.Api.Authentication;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

[Authorize(Policy = Program.ReadPolicyName)]
public sealed class PostingsController : ODataController
{
    private readonly IOwnerIdProvider _ownerIdProvider;
    private readonly IOwnerReadSeam _readSeam;

    public PostingsController(IOwnerIdProvider ownerIdProvider, IOwnerReadSeam readSeam)
    {
        _ownerIdProvider = ownerIdProvider ?? throw new ArgumentNullException(nameof(ownerIdProvider));
        _readSeam = readSeam ?? throw new ArgumentNullException(nameof(readSeam));
    }

    [HttpGet]
    public IActionResult Get(ODataQueryOptions<PostingReadModel> options)
    {
        var validationSettings = new ODataValidationSettings
        {
            AllowedQueryOptions = AllowedQueryOptions.Filter | AllowedQueryOptions.OrderBy | AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.Count | AllowedQueryOptions.Select,
            MaxTop = 100,
            MaxNodeCount = 100,
            MaxOrderByNodeCount = 5,
            AllowedFunctions = AllowedFunctions.ToLower | AllowedFunctions.Contains | AllowedFunctions.StartsWith | AllowedFunctions.EndsWith,
            AllowedLogicalOperators = AllowedLogicalOperators.Equal | AllowedLogicalOperators.NotEqual | AllowedLogicalOperators.GreaterThan | AllowedLogicalOperators.GreaterThanOrEqual | AllowedLogicalOperators.LessThan | AllowedLogicalOperators.LessThanOrEqual | AllowedLogicalOperators.And | AllowedLogicalOperators.Or | AllowedLogicalOperators.Not,
            AllowedArithmeticOperators = AllowedArithmeticOperators.None
        };

        validationSettings.AllowedOrderByProperties.Add("title");
        validationSettings.AllowedOrderByProperties.Add("companyName");
        validationSettings.AllowedOrderByProperties.Add("location");
        validationSettings.AllowedOrderByProperties.Add("disposition");
        validationSettings.AllowedOrderByProperties.Add("resurfacePolicy");
        validationSettings.AllowedOrderByProperties.Add("score");
        validationSettings.AllowedOrderByProperties.Add("createdAt");
        validationSettings.AllowedOrderByProperties.Add("updatedAt");
        validationSettings.AllowedOrderByProperties.Add("id");

        options.Validate(validationSettings);
        ValidateStringFunctions(options.Filter);

        var ownerId = _ownerIdProvider.GetOwnerId();
        var baseQuery = _readSeam.QueryPostings(ownerId);
        var querySettings = new ODataQuerySettings { HandleNullPropagation = HandleNullPropagationOption.False };

        // 1. Filter
        var filtered = (IQueryable<PostingReadModel>)(options.Filter?.ApplyTo(baseQuery, querySettings) ?? baseQuery);

        // 2. Count
        long? totalCount = null;
        if (options.Count?.Value == true)
        {
            totalCount = filtered.Count();
        }

        // 3. Sort
        var ordered = ApplyPostingsOrdering(filtered, options.OrderBy);

        // 4. Paging
        var paged = ordered;
        if (options.Skip is not null)
        {
            paged = (IQueryable<PostingReadModel>)options.Skip.ApplyTo(paged, querySettings);
        }

        if (options.Top is not null)
        {
            paged = (IQueryable<PostingReadModel>)options.Top.ApplyTo(paged, querySettings);
        }
        else
        {
            paged = paged.Take(25);
        }

        // 5. Materialize capped page
        var items = paged.ToList();

        // 6. Project if $select is present
        if (!string.IsNullOrWhiteSpace(options.SelectExpand?.RawSelect))
        {
            var projected = options.SelectExpand.ApplyTo(items.AsQueryable(), querySettings);
            var projectedList = projected.Cast<object>().ToList();
            return Ok(new PageResult<object>(projectedList, null, totalCount));
        }

        return Ok(new PageResult<PostingReadModel>(items, null, totalCount));
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
            : ordered.ThenBy(p => p.Id);
    }

    private static void ValidateStringFunctions(FilterQueryOption? filter)
    {
        if (filter?.FilterClause?.Expression is null)
        {
            return;
        }

        ValidateNode(filter.FilterClause.Expression, 1);
    }

    private static void ValidateNode(QueryNode node, int depth)
    {
        if (depth > 10)
        {
            throw new ODataException("Filter expression depth exceeds maximum depth limit of 10.");
        }

        if (node is BinaryOperatorNode binary)
        {
            ValidateNode(binary.Left, depth + 1);
            ValidateNode(binary.Right, depth + 1);
        }
        else if (node is UnaryOperatorNode unary)
        {
            ValidateNode(unary.Operand, depth + 1);
        }
        else if (node is SingleValueFunctionCallNode fn)
        {
            var fnName = fn.Name?.ToLowerInvariant();
            if (fnName is "contains" or "startswith" or "endswith")
            {
                var parameters = fn.Parameters.ToList();
                if (parameters.Count < 2)
                {
                    throw new ODataException($"Function '{fn.Name}' requires 2 parameters.");
                }

                var firstArg = parameters[0];
                if (firstArg is not SingleValueFunctionCallNode innerFn ||
                    !string.Equals(innerFn.Name, "tolower", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ODataException($"String search function '{fn.Name}' must wrap property in tolower().");
                }

                var secondArg = parameters[1];
                if (secondArg is ConstantNode constant && constant.Value is string s)
                {
                    if (!string.Equals(s, s.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        throw new ODataException($"String search term '{s}' in function '{fn.Name}' must be lowercase.");
                    }
                }

                foreach (var p in parameters)
                {
                    ValidateNode(p, depth + 1);
                }
            }
            else
            {
                foreach (var p in fn.Parameters)
                {
                    ValidateNode(p, depth + 1);
                }
            }
        }
    }
}
