namespace RoleRadar.Api.Envelopes;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Limit,
    int Offset);
