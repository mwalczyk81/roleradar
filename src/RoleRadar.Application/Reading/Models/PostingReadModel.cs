namespace RoleRadar.Application.Reading.Models;

public sealed class PostingReadModel
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? RequisitionId { get; init; }
    public string? Location { get; init; }
    public string Disposition { get; init; } = string.Empty;
    public string ResurfacePolicy { get; init; } = string.Empty;
    public decimal? Score { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When RoleRadar first recorded the posting (stored <c>first_seen_at</c>). Filterable, not sortable.</summary>
    public DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>True when an application exists for the posting. Derived, written only at the repository choke point (ADR 0031).</summary>
    public bool HasApplication { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
    internal int DispositionRank { get; init; }
    internal bool HasScore { get; init; }
}
