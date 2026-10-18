namespace RoleRadar.Domain.Tracking;

public sealed class Company
{
    public const int MaxNameLength = 150;
    public const int MaxDescriptionLength = 300;
    public const int MaxParentCompanyLength = 150;
    public const int MaxHeadquartersLength = 150;
    public const int MaxCategoryTagsCount = 20;
    public const int MaxCategoryTagLength = 50;
    public const int MaxLastCheckResultLength = 300;

    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? ParentCompany { get; private set; }
    public OwnershipType OwnershipType { get; private set; } = OwnershipType.Unknown;
    public string? Headquarters { get; private set; }
    public IReadOnlyList<string> CategoryTags { get; private set; } = [];
    public CompanyStatus Status { get; private set; } = CompanyStatus.Neutral;
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public string? LastCheckResult { get; private set; }
    public Guid VersionToken { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; private set; }

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
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException($"Company name cannot exceed {MaxNameLength} characters.", nameof(name));
        }

        if (description is not null && description.Length > MaxDescriptionLength)
        {
            throw new ArgumentException($"Company description cannot exceed {MaxDescriptionLength} characters.", nameof(description));
        }

        if (parentCompany is not null && parentCompany.Length > MaxParentCompanyLength)
        {
            throw new ArgumentException($"Parent company cannot exceed {MaxParentCompanyLength} characters.", nameof(parentCompany));
        }

        if (headquarters is not null && headquarters.Length > MaxHeadquartersLength)
        {
            throw new ArgumentException($"Headquarters cannot exceed {MaxHeadquartersLength} characters.", nameof(headquarters));
        }

        var validatedTags = ValidateCategoryTags(categoryTags);
        var timestamp = now ?? DateTimeOffset.UtcNow;

        return new Company
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId.Trim(),
            Name = trimmedName,
            NormalizedName = trimmedName.ToLowerInvariant(),
            Description = description,
            ParentCompany = parentCompany,
            OwnershipType = ownershipType,
            Headquarters = headquarters,
            CategoryTags = validatedTags,
            Status = status,
            VersionToken = Guid.CreateVersion7(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public void Update(
        string? name = null,
        string? description = null,
        string? parentCompany = null,
        OwnershipType? ownershipType = null,
        string? headquarters = null,
        IReadOnlyList<string>? categoryTags = null,
        CompanyStatus? status = null,
        DateTimeOffset? lastCheckedAt = null,
        string? lastCheckResult = null,
        DateTimeOffset? now = null)
    {
        if (name is not null)
        {
            var trimmedName = name.Trim();
            if (trimmedName.Length == 0 || trimmedName.Length > MaxNameLength)
            {
                throw new ArgumentException($"Company name must be 1..{MaxNameLength} characters.", nameof(name));
            }
            Name = trimmedName;
            NormalizedName = trimmedName.ToLowerInvariant();
        }

        if (description is not null)
        {
            if (description.Length > MaxDescriptionLength)
            {
                throw new ArgumentException($"Company description cannot exceed {MaxDescriptionLength} characters.", nameof(description));
            }
            Description = description;
        }

        if (parentCompany is not null)
        {
            if (parentCompany.Length > MaxParentCompanyLength)
            {
                throw new ArgumentException($"Parent company cannot exceed {MaxParentCompanyLength} characters.", nameof(parentCompany));
            }
            ParentCompany = parentCompany;
        }

        if (ownershipType.HasValue)
        {
            OwnershipType = ownershipType.Value;
        }

        if (headquarters is not null)
        {
            if (headquarters.Length > MaxHeadquartersLength)
            {
                throw new ArgumentException($"Headquarters cannot exceed {MaxHeadquartersLength} characters.", nameof(headquarters));
            }
            Headquarters = headquarters;
        }

        if (categoryTags is not null)
        {
            CategoryTags = ValidateCategoryTags(categoryTags);
        }

        if (status.HasValue)
        {
            Status = status.Value;
        }

        if (lastCheckedAt.HasValue)
        {
            LastCheckedAt = lastCheckedAt.Value;
        }

        if (lastCheckResult is not null)
        {
            if (lastCheckResult.Length > MaxLastCheckResultLength)
            {
                throw new ArgumentException($"Last check result cannot exceed {MaxLastCheckResultLength} characters.", nameof(lastCheckResult));
            }
            LastCheckResult = lastCheckResult;
        }

        VersionToken = Guid.CreateVersion7();
        UpdatedAt = now ?? DateTimeOffset.UtcNow;
    }

    private static List<string> ValidateCategoryTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return [];
        }

        if (tags.Count > MaxCategoryTagsCount)
        {
            throw new ArgumentException($"Category tags count cannot exceed {MaxCategoryTagsCount}.", nameof(tags));
        }

        var list = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            var trimmed = tag.Trim();
            if (trimmed.Length > MaxCategoryTagLength)
            {
                throw new ArgumentException($"Category tag '{trimmed}' exceeds max length of {MaxCategoryTagLength}.", nameof(tags));
            }
            if (trimmed.Length > 0 && !list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(trimmed);
            }
        }

        return list;
    }
}
