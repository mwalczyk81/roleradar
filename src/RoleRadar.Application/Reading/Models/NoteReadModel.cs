namespace RoleRadar.Application.Reading.Models;

public sealed class NoteReadModel
{
    public Guid Id { get; init; }
    public string Heading { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string LinkedEntityType { get; init; } = string.Empty;
    public Guid? LinkedEntityId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? Content { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
}
