namespace RoleRadar.Application.Reading;

using System.Linq;
using RoleRadar.Application.Reading.Models;

public static class CriterionOrderingExtensions
{
    public static IOrderedQueryable<CriterionReadModel> OrderByEffectiveTo(this IQueryable<CriterionReadModel> source, bool descending)
        => descending
            ? source.OrderByDescending(p => p.HasEffectiveTo).ThenByDescending(p => p.EffectiveTo)
            : source.OrderByDescending(p => p.HasEffectiveTo).ThenBy(p => p.EffectiveTo);

    public static IOrderedQueryable<CriterionReadModel> ThenByEffectiveTo(this IOrderedQueryable<CriterionReadModel> source, bool descending)
        => descending
            ? source.ThenByDescending(p => p.HasEffectiveTo).ThenByDescending(p => p.EffectiveTo)
            : source.ThenByDescending(p => p.HasEffectiveTo).ThenBy(p => p.EffectiveTo);
}
