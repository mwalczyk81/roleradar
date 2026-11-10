using RoleRadar.Application.Audit.Queries;

namespace RoleRadar.Application.Abstractions;

/// <summary>
/// Storage port for the read-only audit listing (feature 013, FR-020, ADR 0041). Implementations always filter by the
/// query's owner, page by keyset on (occurred_at desc, _id desc) and project only the fields of
/// <see cref="AuditListItem"/>. Expected failures come back as a <see cref="Result{T}"/>; nothing is thrown.
/// </summary>
public interface IAuditLogReader
{
    Task<Result<AuditListPage>> ListAsync(ListAuditEntriesQuery query, CancellationToken cancellationToken = default);
}
