using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record TrackerLogCommand(
    string Action,
    string? Key = null,
    string? Category = null,
    string? Value = null,
    string? Provenance = null,
    string? EffectiveFrom = null,
    Guid? VersionToken = null,
    string? StartedAt = null,
    int? NewPostingsCount = null,
    Guid? ReportNoteId = null,
    string? Notes = null,
    string? OwnerId = null) : ICommand<TrackerLogResultDto>;

public sealed record TrackerLogResultDto(
    string Action,
    Guid RecordId,
    string Status,
    DateTimeOffset UpdatedAtUtc,
    Guid? VersionToken = null);
