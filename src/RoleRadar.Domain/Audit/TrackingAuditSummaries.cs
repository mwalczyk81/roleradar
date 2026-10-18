namespace RoleRadar.Domain.Audit;

public sealed record PostingAuditSummary(
    Guid PostingId,
    IReadOnlyList<string> ChangedFieldNames,
    string? PriorDisposition = null,
    string? NewDisposition = null,
    string? Reason = null,
    DateTimeOffset Timestamp = default);

public sealed record CompanyAuditSummary(
    Guid CompanyId,
    IReadOnlyList<string> ChangedFieldNames,
    string Action,
    DateTimeOffset Timestamp = default);

public sealed record CriterionAuditSummary(
    Guid CriterionId,
    string Key,
    string Action,
    DateTimeOffset Timestamp = default);

public sealed record ApplicationAuditSummary(
    Guid ApplicationId,
    Guid PostingId,
    string Action,
    DateTimeOffset Timestamp = default);

/// <summary>
/// Audit summary for contact operations strictly containing changed field names and record IDs,
/// never personal data (names, roles, or companies) per FR-031.
/// </summary>
public sealed record ContactAuditContentSummary(
    Guid ContactId,
    IReadOnlyList<string> ChangedFieldNames,
    bool IsRedacted,
    DateTimeOffset Timestamp = default);
