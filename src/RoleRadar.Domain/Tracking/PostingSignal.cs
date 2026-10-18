using RoleRadar.Domain.Common;

namespace RoleRadar.Domain.Tracking;

public sealed record PostingSignal
{
    public const int MaxEvidenceLength = 500;

    public Guid Id { get; init; }
    public string OwnerId { get; init; }
    public Guid PostingId { get; init; }
    public SignalType SignalType { get; init; }
    public string Evidence { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public string? DeadlineDate { get; init; }

    public PostingSignal(
        Guid id,
        string ownerId,
        Guid postingId,
        SignalType signalType,
        string evidence,
        DateTimeOffset observedAt,
        string? deadlineDate = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Signal identifier cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier cannot be null or whitespace.", nameof(ownerId));
        }

        if (postingId == Guid.Empty)
        {
            throw new ArgumentException("Posting identifier cannot be empty.", nameof(postingId));
        }

        if (string.IsNullOrWhiteSpace(evidence))
        {
            throw new ArgumentException("Evidence cannot be null or whitespace.", nameof(evidence));
        }

        if (evidence.Length > MaxEvidenceLength)
        {
            throw new ArgumentException($"Evidence cannot exceed {MaxEvidenceLength} characters.", nameof(evidence));
        }

        if (!string.IsNullOrWhiteSpace(deadlineDate))
        {
            if (!DateOnly.TryParseExact(deadlineDate.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            {
                throw new ArgumentException("Deadline date must be in YYYY-MM-DD format.", nameof(deadlineDate));
            }
        }

        Id = id;
        OwnerId = ownerId.Trim();
        PostingId = postingId;
        SignalType = signalType;
        Evidence = evidence.Trim();
        ObservedAt = observedAt;
        DeadlineDate = string.IsNullOrWhiteSpace(deadlineDate) ? null : deadlineDate.Trim();
    }
}
