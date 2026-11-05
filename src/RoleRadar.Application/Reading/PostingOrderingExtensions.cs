namespace RoleRadar.Application.Reading;

using System.Linq;
using RoleRadar.Application.Reading.Models;

public static class PostingOrderingExtensions
{
    public static IOrderedQueryable<PostingReadModel> OrderByDisposition(this IQueryable<PostingReadModel> source, bool descending)
        => descending ? source.OrderByDescending(p => p.DispositionRank) : source.OrderBy(p => p.DispositionRank);

    public static IOrderedQueryable<PostingReadModel> ThenByDisposition(this IOrderedQueryable<PostingReadModel> source, bool descending)
        => descending ? source.ThenByDescending(p => p.DispositionRank) : source.ThenBy(p => p.DispositionRank);

    public static IOrderedQueryable<PostingReadModel> OrderByScore(this IQueryable<PostingReadModel> source, bool descending)
        => descending
            ? source.OrderByDescending(p => p.HasScore).ThenByDescending(p => p.Score)
            : source.OrderByDescending(p => p.HasScore).ThenBy(p => p.Score);

    public static IOrderedQueryable<PostingReadModel> ThenByScore(this IOrderedQueryable<PostingReadModel> source, bool descending)
        => descending
            ? source.ThenByDescending(p => p.HasScore).ThenByDescending(p => p.Score)
            : source.ThenByDescending(p => p.HasScore).ThenBy(p => p.Score);
}
