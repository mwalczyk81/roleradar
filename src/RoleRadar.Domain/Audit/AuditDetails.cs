namespace RoleRadar.Domain.Audit;

/// <summary>
/// Structured summary of changes recorded in an audit log entry for tracking entities.
/// For contact entities, strictly contains changed field names and contact ID with zero personal details.
/// For other tracking entities, contains changed field names and optional non-sensitive before/after snapshots.
/// </summary>
public sealed record AuditDetails(
    IReadOnlyList<string> ChangedFields,
    IReadOnlyDictionary<string, object?>? Before = null,
    IReadOnlyDictionary<string, object?>? After = null,
    Guid? ContactId = null)
{
    public static AuditDetails ForContact(Guid contactId, IReadOnlyList<string> changedFields) =>
        new(changedFields, ContactId: contactId);

    public static AuditDetails ForEntity(
        IReadOnlyList<string> changedFields,
        IReadOnlyDictionary<string, object?>? before = null,
        IReadOnlyDictionary<string, object?>? after = null) =>
        new(changedFields, Before: before, After: after);
}
