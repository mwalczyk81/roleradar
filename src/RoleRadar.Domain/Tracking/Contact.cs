using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Tracking;

public enum ContactRelationship
{
    Recruiter,
    Referral,
    HiringManager,
    Peer,
    Redacted
}

public sealed class Contact
{
    public const int MaxNameLength = 150;
    public const int MaxRoleLength = 150;
    public const int MaxCompanyAffiliationLength = 150;
    public const int MaxNotesLength = 500;
    public const string RedactedPlaceholder = "[REDACTED]";

    public Guid Id { get; }
    public string OwnerId { get; }
    public string Name { get; private set; }
    public string? Role { get; private set; }
    public string? CompanyAffiliation { get; private set; }
    public Guid? CompanyId { get; private set; }
    public ContactRelationship Relationship { get; private set; }
    public string? Notes { get; private set; }
    public bool IsRedacted { get; private set; }
    public Guid VersionToken { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime UpdatedAt { get; private set; }

    private Contact(
        Guid id,
        string ownerId,
        string name,
        string? role,
        string? companyAffiliation,
        Guid? companyId,
        ContactRelationship relationship,
        string? notes,
        bool isRedacted,
        Guid versionToken,
        DateTime createdAt,
        DateTime updatedAt)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        Role = role;
        CompanyAffiliation = companyAffiliation;
        CompanyId = companyId;
        Relationship = relationship;
        Notes = notes;
        IsRedacted = isRedacted;
        VersionToken = versionToken;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public static Contact Create(
        string ownerId,
        string name,
        string? role = null,
        string? companyAffiliation = null,
        Guid? companyId = null,
        ContactRelationship relationship = ContactRelationship.Peer,
        string? notes = null,
        Guid? id = null,
        DateTime? now = null)
    {
        var validOwner = new OwnerId(ownerId).Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Contact name is required.", nameof(name));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException($"Contact name cannot exceed {MaxNameLength} characters.", nameof(name));
        }

        var trimmedRole = role?.Trim();
        if (trimmedRole is not null && trimmedRole.Length > MaxRoleLength)
        {
            throw new ArgumentException($"Contact role cannot exceed {MaxRoleLength} characters.", nameof(role));
        }

        var trimmedCompany = companyAffiliation?.Trim();
        if (trimmedCompany is not null && trimmedCompany.Length > MaxCompanyAffiliationLength)
        {
            throw new ArgumentException($"Company affiliation cannot exceed {MaxCompanyAffiliationLength} characters.", nameof(companyAffiliation));
        }

        if (notes is not null && notes.Length > MaxNotesLength)
        {
            throw new ArgumentException($"Contact notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
        }

        var timestamp = now ?? DateTime.UtcNow;
        var utcNow = timestamp.Kind == DateTimeKind.Utc ? timestamp : DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);

        return new Contact(
            id: id ?? Guid.CreateVersion7(),
            ownerId: validOwner,
            name: trimmedName,
            role: trimmedRole,
            companyAffiliation: trimmedCompany,
            companyId: companyId,
            relationship: relationship,
            notes: notes,
            isRedacted: false,
            versionToken: Guid.CreateVersion7(),
            createdAt: utcNow,
            updatedAt: utcNow);
    }

    public void Redact(DateTimeOffset now)
    {
        if (IsRedacted)
        {
            return;
        }

        Name = RedactedPlaceholder;
        Role = null;
        CompanyAffiliation = null;
        CompanyId = null;
        Relationship = ContactRelationship.Redacted;
        Notes = null;
        IsRedacted = true;
        VersionToken = Guid.CreateVersion7();
        UpdatedAt = now.UtcDateTime;
    }

    public static Contact Rehydrate(
        Guid id,
        string ownerId,
        string name,
        string? role,
        string? companyAffiliation,
        Guid? companyId,
        ContactRelationship relationship,
        string? notes,
        bool isRedacted,
        Guid versionToken,
        DateTime createdAt,
        DateTime updatedAt)
    {
        return new Contact(
            id,
            ownerId,
            name,
            role,
            companyAffiliation,
            companyId,
            relationship,
            notes,
            isRedacted,
            versionToken,
            createdAt,
            updatedAt);
    }
}
