using RoleRadar.Domain.Tracking;

namespace RoleRadar.Domain.Tracking;

public sealed class Criterion
{
    public Guid Id { get; private set; }
    public string OwnerId { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public CriterionCategory Category { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }
    public ProvenanceKind Provenance { get; private set; }
    public bool IsActive { get; private set; }
    public Guid VersionToken { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Criterion() { }

    public static Criterion Create(
        string ownerId,
        string key,
        CriterionCategory category,
        string value,
        ProvenanceKind provenance,
        DateTimeOffset effectiveFrom,
        Guid? id = null,
        Guid? versionToken = null,
        DateTimeOffset? createdAt = null)
    {
        throw new NotImplementedException();
    }

    public static Criterion Rehydrate(
        Guid id,
        string ownerId,
        string key,
        CriterionCategory category,
        string value,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        ProvenanceKind provenance,
        bool isActive,
        Guid versionToken,
        DateTimeOffset createdAt)
    {
        throw new NotImplementedException();
    }

    public Criterion Supersede(
        CriterionCategory newCategory,
        string newValue,
        ProvenanceKind newProvenance,
        DateTimeOffset newEffectiveFrom,
        DateTimeOffset now)
    {
        throw new NotImplementedException();
    }

    public void Deactivate(DateTimeOffset deactivationDate)
    {
        throw new NotImplementedException();
    }

    public bool IsEffectiveAt(DateTimeOffset date)
    {
        throw new NotImplementedException();
    }
}
