namespace RoleRadar.Domain.Tracking;

public sealed class CareerSource
{
    public const int MaxTenantLength = 100;
    public const int MaxSlugLength = 150;
    public const int MaxNotesLength = 500;

    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public Guid CompanyId { get; init; }
    public string AtsType { get; private set; } = string.Empty;
    public string? Tenant { get; private set; }
    public string? Slug { get; private set; }
    public FetchMethod FetchMethod { get; private set; } = FetchMethod.Api;
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public Guid VersionToken { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CareerSource Create(
        string ownerId,
        Guid companyId,
        string atsType,
        string? tenant = null,
        string? slug = null,
        FetchMethod fetchMethod = FetchMethod.Api,
        string? notes = null,
        bool isActive = true,
        Guid? id = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(atsType);

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company ID cannot be empty.", nameof(companyId));
        }

        if (tenant is not null && tenant.Length > MaxTenantLength)
        {
            throw new ArgumentException($"Tenant cannot exceed {MaxTenantLength} characters.", nameof(tenant));
        }

        if (slug is not null && slug.Length > MaxSlugLength)
        {
            throw new ArgumentException($"Slug cannot exceed {MaxSlugLength} characters.", nameof(slug));
        }

        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new ArgumentException($"Notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;

        return new CareerSource
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId.Trim(),
            CompanyId = companyId,
            AtsType = atsType.Trim(),
            Tenant = tenant?.Trim(),
            Slug = slug?.Trim(),
            FetchMethod = fetchMethod,
            Notes = notes,
            IsActive = isActive,
            VersionToken = Guid.CreateVersion7(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public static CareerSource Rehydrate(
        Guid id,
        string ownerId,
        Guid companyId,
        string atsType,
        string? tenant,
        string? slug,
        FetchMethod fetchMethod,
        string? notes,
        bool isActive,
        Guid versionToken,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new CareerSource
        {
            Id = id,
            OwnerId = ownerId,
            CompanyId = companyId,
            AtsType = atsType,
            Tenant = tenant,
            Slug = slug,
            FetchMethod = fetchMethod,
            Notes = notes,
            IsActive = isActive,
            VersionToken = versionToken,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void Retire(DateTimeOffset? now = null)
    {
        IsActive = false;
        VersionToken = Guid.CreateVersion7();
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
    }

    public void Reactivate(DateTimeOffset? now = null)
    {
        IsActive = true;
        VersionToken = Guid.CreateVersion7();
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
    }

    public void Update(
        string? atsType = null,
        string? tenant = null,
        string? slug = null,
        FetchMethod? fetchMethod = null,
        string? notes = null,
        bool? isActive = null,
        DateTimeOffset? now = null)
    {
        if (!string.IsNullOrWhiteSpace(atsType))
        {
            AtsType = atsType.Trim();
        }

        if (tenant is not null)
        {
            if (tenant.Length > MaxTenantLength)
            {
                throw new ArgumentException($"Tenant cannot exceed {MaxTenantLength} characters.", nameof(tenant));
            }
            Tenant = tenant.Trim();
        }

        if (slug is not null)
        {
            if (slug.Length > MaxSlugLength)
            {
                throw new ArgumentException($"Slug cannot exceed {MaxSlugLength} characters.", nameof(slug));
            }
            Slug = slug.Trim();
        }

        if (fetchMethod.HasValue)
        {
            FetchMethod = fetchMethod.Value;
        }

        if (notes is not null)
        {
            if (notes.Length > MaxNotesLength)
            {
                throw new ArgumentException($"Notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
            }
            Notes = notes;
        }

        if (isActive.HasValue)
        {
            IsActive = isActive.Value;
        }

        VersionToken = Guid.CreateVersion7();
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
    }
}
