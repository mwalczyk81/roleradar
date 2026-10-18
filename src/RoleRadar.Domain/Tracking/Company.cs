namespace RoleRadar.Domain.Tracking;

public sealed class Company
{
    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string NormalizedName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? ParentCompany { get; init; }
    public OwnershipType OwnershipType { get; init; } = OwnershipType.Unknown;
    public string? Headquarters { get; init; }
    public IReadOnlyList<string> CategoryTags { get; init; } = [];
    public CompanyStatus Status { get; init; } = CompanyStatus.Neutral;
    public DateTimeOffset? LastCheckedAt { get; init; }
    public string? LastCheckResult { get; init; }
    public Guid VersionToken { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    // Stub factory method for initial compilation; does not yet validate invariants so tests fail on assertions.
    public static Company Create(
        string ownerId,
        string name,
        string? description = null,
        string? parentCompany = null,
        OwnershipType ownershipType = OwnershipType.Unknown,
        string? headquarters = null,
        IReadOnlyList<string>? categoryTags = null,
        CompanyStatus status = CompanyStatus.Neutral,
        Guid? id = null,
        DateTimeOffset? now = null)
    {
        return new Company
        {
            Id = id ?? Guid.Empty,
            OwnerId = ownerId ?? string.Empty,
            Name = name ?? string.Empty,
            NormalizedName = string.Empty, // Intentionally empty so assertions fail before implementation
            Description = description,
            ParentCompany = parentCompany,
            OwnershipType = ownershipType,
            Headquarters = headquarters,
            CategoryTags = categoryTags ?? [],
            Status = status,
            VersionToken = Guid.Empty,
            CreatedAt = now ?? DateTimeOffset.UtcNow,
            UpdatedAt = now ?? DateTimeOffset.UtcNow
        };
    }
}
