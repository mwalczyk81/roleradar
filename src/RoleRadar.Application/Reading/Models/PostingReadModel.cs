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
    public DateTimeOffset UpdatedAt { get; init; }

    internal string OwnerId { get; init; } = string.Empty;
    internal int DispositionRank { get; init; }
    internal bool HasScore { get; init; }
}
