using System.Globalization;

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
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (applicationId == Guid.Empty)
        {
            throw new ArgumentException("Application ID cannot be empty.", nameof(applicationId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(eventDate);
        if (!DateOnly.TryParseExact(eventDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException($"Event date must be in YYYY-MM-DD format: '{eventDate}'.", nameof(eventDate));
        }

        string? validatedEventSource = null;
        if (eventSource is not null)
        {
            if (eventSource.Length > MaxEventSourceLength)
            {
                throw new ArgumentException($"Event source cannot exceed {MaxEventSourceLength} characters.", nameof(eventSource));
            }
            validatedEventSource = eventSource.Trim();
        }

        string? validatedNotes = null;
        if (notes is not null)
        {
            if (notes.Length > MaxNotesLength)
            {
                throw new ArgumentException($"Notes cannot exceed {MaxNotesLength} characters.", nameof(notes));
            }
            validatedNotes = notes.Trim();
        }

        var ts = now ?? DateTimeOffset.UtcNow;
        return new ApplicationEvent
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = ownerId.Trim(),
            ApplicationId = applicationId,
            EventDate = eventDate.Trim(),
            EventType = eventType,
            EventSource = validatedEventSource,
            Notes = validatedNotes,
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
