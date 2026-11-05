namespace RoleRadar.Application.Reading;

using System.Linq;
using RoleRadar.Application.Reading.Models;

public static class ApplicationOrderingExtensions
{
    public static IOrderedQueryable<ApplicationReadModel> OrderByFirstResponse(this IQueryable<ApplicationReadModel> source, bool descending)
        => descending
            ? source.OrderByDescending(p => p.HasFirstResponse).ThenByDescending(p => p.FirstResponseAt)
            : source.OrderByDescending(p => p.HasFirstResponse).ThenBy(p => p.FirstResponseAt);

    public static IOrderedQueryable<ApplicationReadModel> ThenByFirstResponse(this IOrderedQueryable<ApplicationReadModel> source, bool descending)
        => descending
            ? source.ThenByDescending(p => p.HasFirstResponse).ThenByDescending(p => p.FirstResponseAt)
            : source.ThenByDescending(p => p.HasFirstResponse).ThenBy(p => p.FirstResponseAt);

    public static IOrderedQueryable<ApplicationReadModel> OrderByLastEvent(this IQueryable<ApplicationReadModel> source, bool descending)
        => descending
            ? source.OrderByDescending(p => p.HasLastEvent).ThenByDescending(p => p.LastEventAt)
            : source.OrderByDescending(p => p.HasLastEvent).ThenBy(p => p.LastEventAt);

    public static IOrderedQueryable<ApplicationReadModel> ThenByLastEvent(this IOrderedQueryable<ApplicationReadModel> source, bool descending)
        => descending
            ? source.ThenByDescending(p => p.HasLastEvent).ThenByDescending(p => p.LastEventAt)
            : source.ThenByDescending(p => p.HasLastEvent).ThenBy(p => p.LastEventAt);
}
