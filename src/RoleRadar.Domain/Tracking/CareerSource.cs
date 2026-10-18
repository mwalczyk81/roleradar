namespace RoleRadar.Domain.Tracking;

public sealed class CareerSource
{
    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public Guid CompanyId { get; init; }
    public string AtsType { get; init; } = string.Empty;
    public string? Tenant { get; init; }
    public string? Slug { get; init; }
    public FetchMethod FetchMethod { get; init; } = FetchMethod.Api;
    public string? Notes { get; init; }
    public bool IsActive { get; private set; }
    public Guid VersionToken { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

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
        return new CareerSource
        {
            Id = id ?? Guid.Empty,
            OwnerId = ownerId ?? string.Empty,
            CompanyId = companyId,
            AtsType = atsType ?? string.Empty,
            Tenant = tenant,
            Slug = slug,
            FetchMethod = fetchMethod,
            Notes = notes,
            IsActive = false, // Intentionally false so default active assertion fails
            VersionToken = Guid.Empty,
            CreatedAt = now ?? DateTimeOffset.UtcNow,
            UpdatedAt = now ?? DateTimeOffset.UtcNow
        };
    }

    public void Retire(DateTimeOffset? now = null)
    {
        // Stub: does not mutate
    }

    public void Reactivate(DateTimeOffset? now = null)
    {
        // Stub: does not mutate
    }
}
