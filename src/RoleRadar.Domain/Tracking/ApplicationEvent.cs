namespace RoleRadar.Domain.Tracking;

public sealed class ApplicationEvent
{
    public const int MaxEventSourceLength = 100;
    public const int MaxNotesLength = 500;

    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public Guid ApplicationId { get; init; }
    public string EventDate { get; private set; } = string.Empty;
    public ApplicationEventType EventType { get; private set; }
    public string? EventSource { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }

    public static ApplicationEvent Create(
        string ownerId,
        Guid applicationId,
        string eventDate,
        ApplicationEventType eventType,
        string? eventSource = null,
        string? notes = null,
        Guid? id = null,
        DateTimeOffset? now = null)
    {
        // Stub implementation for test-first phase - intentionally does not validate invariants
        var ts = now ?? DateTimeOffset.UtcNow;
        return new ApplicationEvent
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId ?? string.Empty,
            ApplicationId = applicationId,
            EventDate = eventDate ?? string.Empty,
            EventType = eventType,
            EventSource = eventSource,
            Notes = notes,
            CreatedAt = ts
        };
    }

    public static ApplicationEvent Rehydrate(
        Guid id,
        string ownerId,
        Guid applicationId,
        string eventDate,
        ApplicationEventType eventType,
        string? eventSource,
        string? notes,
        DateTimeOffset createdAt)
    {
        return new ApplicationEvent
        {
            Id = id,
            OwnerId = ownerId,
            ApplicationId = applicationId,
            EventDate = eventDate,
            EventType = eventType,
            EventSource = eventSource,
            Notes = notes,
            CreatedAt = createdAt
        };
    }
}
