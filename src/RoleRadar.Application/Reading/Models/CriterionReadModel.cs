namespace RoleRadar.Application.Reading.Models;

public sealed class CriterionReadModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string RuleText { get; init; } = string.Empty;
    public DateTimeOffset EffectiveFrom { get; init; }
    public DateTimeOffset? EffectiveTo { get; init; }
    public bool IsActive { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
}
