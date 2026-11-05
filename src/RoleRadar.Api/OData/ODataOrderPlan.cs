namespace RoleRadar.Api.OData;

using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData;
using Microsoft.OData.UriParser;

/// <summary>
/// The single choke point that turns a caller's <c>$orderby</c> into the sort keys that are actually applied.
/// MongoDB rejects a sort document that names the same field twice ("Duplicate element name"), so:
/// a repeated key is ignored (the first occurrence wins, direction included), and the stable <c>id</c> tiebreaker
/// is only appended when the caller did not already sort by <c>id</c>.
/// </summary>
public sealed record ODataOrderPlan(IReadOnlyList<OrderByPropertyNode> Nodes, bool SortsById)
{
    /// <param name="orderBy">The parsed <c>$orderby</c>, or <c>null</c>.</param>
    /// <param name="sortKeyName">
    /// Maps a property name to the name of the stored key it sorts on, for properties that share one
    /// (for example <c>ageInDays</c> sorts on <c>appliedAt</c>). Null means each property is its own key.
    /// </param>
    public static ODataOrderPlan From(OrderByQueryOption? orderBy, Func<string, string>? sortKeyName = null)
    {
        var nodes = new List<OrderByPropertyNode>();
        if (orderBy is null)
        {
            return new ODataOrderPlan(nodes, false);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sortsById = false;
        foreach (var node in orderBy.OrderByNodes)
        {
            if (node is not OrderByPropertyNode propNode)
            {
                throw new ODataException("Only direct property sorting is supported in $orderby.");
            }

            var name = propNode.Property.Name;
            var key = sortKeyName?.Invoke(name) ?? name;
            if (!seen.Add(key))
            {
                continue;
            }

            sortsById |= string.Equals(key, "id", StringComparison.OrdinalIgnoreCase);
            nodes.Add(propNode);
        }

        return new ODataOrderPlan(nodes, sortsById);
    }
}
