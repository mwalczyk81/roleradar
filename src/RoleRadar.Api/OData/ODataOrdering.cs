namespace RoleRadar.Api.OData;

using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData;
using Microsoft.OData.UriParser;

/// <summary>
/// Allowlisted <c>$orderby</c> for one entity set. Each sortable property is registered once; anything else
/// is a 400. A stable <c>id</c> tiebreaker is always appended so paging is deterministic.
/// </summary>
public sealed class ODataOrdering<T>
{
    private readonly Dictionary<string, Func<IQueryable<T>, IOrderedQueryable<T>?, bool, IOrderedQueryable<T>>> _sorts =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _entitySet;
    private readonly Expression<Func<T, Guid>> _id;
    private readonly Func<IQueryable<T>, IOrderedQueryable<T>> _defaultOrder;

    public ODataOrdering(string entitySet, Expression<Func<T, Guid>> id, Func<IQueryable<T>, IOrderedQueryable<T>> defaultOrder)
    {
        _entitySet = entitySet;
        _id = id;
        _defaultOrder = defaultOrder;
        Add("id", id);
    }

    public IEnumerable<string> Names => _sorts.Keys;

    public ODataOrdering<T> Add<TKey>(string name, Expression<Func<T, TKey>> key)
    {
        _sorts[name] = (source, ordered, descending) => ordered is null
            ? (descending ? source.OrderByDescending(key) : source.OrderBy(key))
            : (descending ? ordered.ThenByDescending(key) : ordered.ThenBy(key));
        return this;
    }

    /// <summary>Registers a property that needs more than one sort key (for example a nulls-last flag plus the value).</summary>
    public ODataOrdering<T> AddCustom(
        string name,
        Func<IQueryable<T>, bool, IOrderedQueryable<T>> first,
        Func<IOrderedQueryable<T>, bool, IOrderedQueryable<T>> then)
    {
        _sorts[name] = (source, ordered, descending) => ordered is null ? first(source, descending) : then(ordered, descending);
        return this;
    }

    public IQueryable<T> Apply(IQueryable<T> source, OrderByQueryOption? orderBy)
    {
        if (orderBy is null || !orderBy.OrderByNodes.Any())
        {
            return _defaultOrder(source).ThenBy(_id);
        }

        IOrderedQueryable<T>? ordered = null;
        foreach (var node in orderBy.OrderByNodes)
        {
            if (node is not OrderByPropertyNode propNode)
            {
                throw new ODataException("Only direct property sorting is supported in $orderby.");
            }

            if (!_sorts.TryGetValue(propNode.Property.Name, out var sort))
            {
                throw new ODataException($"Property '{propNode.Property.Name}' is not orderable on {_entitySet}.");
            }

            ordered = sort(source, ordered, propNode.Direction == OrderByDirection.Descending);
        }

        return ordered is null ? _defaultOrder(source).ThenBy(_id) : ordered.ThenBy(_id);
    }
}
