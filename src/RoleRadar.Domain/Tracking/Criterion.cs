using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Tracking;

public sealed class Criterion
{
    public const int MaxKeyLength = 100;
    public const int MaxValueLength = 500;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (key.Trim().Length > MaxKeyLength)
        {
            throw new ArgumentException($"Criterion key must not exceed {MaxKeyLength} characters.", nameof(key));
        }

        if (value.Trim().Length > MaxValueLength)
        {
            throw new ArgumentException($"Criterion value must not exceed {MaxValueLength} characters.", nameof(value));
        }

        var now = createdAt ?? DateTimeOffset.UtcNow;
        return new Criterion
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId.Trim(),
            Key = key.Trim(),
            Category = category,
            Value = value.Trim(),
            Provenance = provenance,
            EffectiveFrom = effectiveFrom.ToUniversalTime(),
            EffectiveTo = null,
            IsActive = true,
            VersionToken = versionToken ?? Guid.NewGuid(),
            CreatedAt = now.ToUniversalTime()
        };
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
        return new Criterion
        {
            Id = id,
            OwnerId = ownerId,
            Key = key,
            Category = category,
            Value = value,
            EffectiveFrom = effectiveFrom.ToUniversalTime(),
            EffectiveTo = effectiveTo?.ToUniversalTime(),
            Provenance = provenance,
            IsActive = isActive,
            VersionToken = versionToken,
            CreatedAt = createdAt.ToUniversalTime()
        };
    }

    public Criterion Supersede(
        CriterionCategory newCategory,
        string newValue,
        ProvenanceKind newProvenance,
        DateTimeOffset newEffectiveFrom,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newValue);
        if (newValue.Trim().Length > MaxValueLength)
        {
            throw new ArgumentException($"Criterion value must not exceed {MaxValueLength} characters.", nameof(newValue));
        }

        var utcNewEffectiveFrom = newEffectiveFrom.ToUniversalTime();
        if (utcNewEffectiveFrom <= EffectiveFrom)
        {
            throw new ArgumentException(
                $"New effectiveFrom timestamp '{utcNewEffectiveFrom:O}' must be strictly later than current active version effectiveFrom '{EffectiveFrom:O}'.",
                nameof(newEffectiveFrom));
        }

        // Close current active version
        IsActive = false;
        EffectiveTo = utcNewEffectiveFrom;
        VersionToken = Guid.NewGuid();

        // Create replacement active version
        return new Criterion
        {
            Id = Guid.CreateVersion7(),
            OwnerId = OwnerId,
            Key = Key,
            Category = newCategory,
            Value = newValue.Trim(),
            Provenance = newProvenance,
            EffectiveFrom = utcNewEffectiveFrom,
            EffectiveTo = null,
            IsActive = true,
            VersionToken = Guid.NewGuid(),
            CreatedAt = now.ToUniversalTime()
        };
    }

    public void Deactivate(DateTimeOffset deactivationDate)
    {
        IsActive = false;
        EffectiveTo = deactivationDate.ToUniversalTime();
        VersionToken = Guid.NewGuid();
    }

    public bool IsEffectiveAt(DateTimeOffset date)
    {
        var utc = date.ToUniversalTime();
        if (utc < EffectiveFrom)
        {
            return false;
        }

        if (EffectiveTo.HasValue && utc >= EffectiveTo.Value)
        {
            return false;
        }

        return true;
    }
}
