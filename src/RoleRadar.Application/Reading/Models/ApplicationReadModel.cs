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
    public DateTimeOffset? LastEventAt { get; init; }

    /// <summary>Any offer event. Derived at the repository choke point (ADR 0031).</summary>
    public bool HasOffer { get; init; }

    /// <summary>Any rejected event. Derived at the repository choke point (ADR 0031).</summary>
    public bool HasRejection { get; init; }

    /// <summary>A rejection with no interview dated strictly before it; false without a rejection (ADR 0031).</summary>
    public bool RejectedBeforeInterview { get; init; }
    public int AgeInDays
    {
        get => AppliedAt == default ? 0 : Math.Max(0, (int)(DateTimeOffset.UtcNow.Date - AppliedAt.UtcDateTime.Date).TotalDays);
        init { }
    }

    internal string OwnerId { get; init; } = string.Empty;
    internal bool HasFirstResponse { get; init; }
    internal bool HasLastEvent { get; init; }
}
