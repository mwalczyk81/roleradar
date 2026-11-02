namespace RoleRadar.Application.Reading.Models;

public sealed class ApplicationReadModel
{
    public Guid Id { get; init; }
    public Guid PostingId { get; init; }
    public Guid CompanyId { get; init; }
    public string PostingTitle { get; init; } = string.Empty;
    public string CompanyName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset AppliedAt { get; init; }
    public bool HasInterview { get; init; }
    public DateTimeOffset? FirstResponseAt { get; init; }
    public DateTimeOffset LastEventAt { get; init; }
    public int AgeInDays { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
    internal bool HasFirstResponse { get; init; }
    internal bool HasLastEvent { get; init; }
}
