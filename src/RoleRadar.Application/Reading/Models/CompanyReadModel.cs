namespace RoleRadar.Application.Reading.Models;

public sealed class CompanyReadModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string OwnershipType { get; init; } = string.Empty;
    public string? Headquarters { get; init; }
    public string? Description { get; init; }
    public List<string> CategoryTags { get; init; } = [];

    internal string OwnerId { get; init; } = string.Empty;
}
