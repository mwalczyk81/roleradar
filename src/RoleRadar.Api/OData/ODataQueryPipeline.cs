namespace RoleRadar.Api.OData;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Query.Validator;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.OData;
using Microsoft.OData.UriParser;

public static class ODataQueryPipeline
{
    public static ODataValidationSettings CreateDefaultValidationSettings(IEnumerable<string> allowedOrderByProperties)
    {
        var settings = new ODataValidationSettings
        {
            AllowedQueryOptions = AllowedQueryOptions.Filter | AllowedQueryOptions.OrderBy | AllowedQueryOptions.Top | AllowedQueryOptions.Skip | AllowedQueryOptions.Count | AllowedQueryOptions.Select,
            MaxTop = 100,
            MaxNodeCount = 100,
            MaxOrderByNodeCount = 5,
            AllowedFunctions = AllowedFunctions.ToLower | AllowedFunctions.Contains | AllowedFunctions.StartsWith | AllowedFunctions.EndsWith,
            AllowedLogicalOperators = AllowedLogicalOperators.Equal | AllowedLogicalOperators.NotEqual | AllowedLogicalOperators.GreaterThan | AllowedLogicalOperators.GreaterThanOrEqual | AllowedLogicalOperators.LessThan | AllowedLogicalOperators.LessThanOrEqual | AllowedLogicalOperators.And | AllowedLogicalOperators.Or | AllowedLogicalOperators.Not,
            AllowedArithmeticOperators = AllowedArithmeticOperators.None
        };

        foreach (var prop in allowedOrderByProperties)
        {
            settings.AllowedOrderByProperties.Add(prop);
        }

        return settings;
    }

    public static IActionResult ExecuteQuery<T>(
        ODataQueryOptions<T> options,
        IQueryable<T> baseQuery,
        ODataValidationSettings validationSettings,
        Func<IQueryable<T>, OrderByQueryOption?, IQueryable<T>> applyOrdering,
        ISet<string>? disallowedFilterProperties = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(baseQuery);
        ArgumentNullException.ThrowIfNull(validationSettings);
        ArgumentNullException.ThrowIfNull(applyOrdering);

        options.Validate(validationSettings);
        ValidateFilterExpression(options.Filter, disallowedFilterProperties);

        var querySettings = new ODataQuerySettings { HandleNullPropagation = HandleNullPropagationOption.False };

        // 1. Filter
        var filtered = (IQueryable<T>)(options.Filter?.ApplyTo(baseQuery, querySettings) ?? baseQuery);

        // 2. Count
        long? totalCount = null;
        if (options.Count?.Value == true)
        {
            totalCount = filtered.Count();
        }

        // 3. Sort
        var ordered = applyOrdering(filtered, options.OrderBy);

        // 4. Paging
        var paged = ordered;
        if (options.Skip is not null)
        {
            paged = (IQueryable<T>)options.Skip.ApplyTo(paged, querySettings);
        }

        if (options.Top is not null)
        {
            paged = (IQueryable<T>)options.Top.ApplyTo(paged, querySettings);
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
            return new OkObjectResult(new PageResult<object>(projectedList, null, totalCount));
        }

        return new OkObjectResult(new PageResult<T>(items, null, totalCount));
    }

    public static void ValidateFilterExpression(FilterQueryOption? filter, ISet<string>? disallowedFilterProperties = null)
    {
        if (filter?.FilterClause?.Expression is null)
        {
            return;
        }

        ValidateNode(filter.FilterClause.Expression, 1, disallowedFilterProperties);
    }

    private static void ValidateNode(QueryNode node, int depth, ISet<string>? disallowedFilterProperties)
    {
        if (depth > 10)
        {
            throw new ODataException("Filter expression depth exceeds maximum depth limit of 10.");
        }

        if (node is SingleValuePropertyAccessNode propNode)
        {
            if (disallowedFilterProperties is not null &&
                (disallowedFilterProperties.Contains(propNode.Property.Name) ||
                 disallowedFilterProperties.Contains(propNode.Property.Name.ToLowerInvariant())))
            {
                throw new ODataException($"Property '{propNode.Property.Name}' cannot be used in $filter.");
            }
        }
        else if (node is BinaryOperatorNode binary)
        {
            ValidateNode(binary.Left, depth + 1, disallowedFilterProperties);
            ValidateNode(binary.Right, depth + 1, disallowedFilterProperties);
        }
        else if (node is UnaryOperatorNode unary)
        {
            ValidateNode(unary.Operand, depth + 1, disallowedFilterProperties);
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
                    ValidateNode(p, depth + 1, disallowedFilterProperties);
                }
            }
            else
            {
                foreach (var p in fn.Parameters)
                {
                    ValidateNode(p, depth + 1, disallowedFilterProperties);
                }
            }
        }
    }
}
