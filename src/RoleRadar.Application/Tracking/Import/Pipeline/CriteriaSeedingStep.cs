using System.Collections.Concurrent;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed record CriteriaSeedingResult(int CreatedCount, int SkippedCount);

/// <summary>
/// Step 0 in the tracker import pipeline: seeds predefined compensation criteria
/// (comp_preferred_floor, comp_high_priority_bar) idempotently with stated provenance,
/// and dynamically registers reason_primary criteria with inferred provenance on first use.
/// </summary>
public sealed class CriteriaSeedingStep
{
    private const string ImportActor = "cli-import-tracker";
    private readonly ITrackingTransactionContext _context;
    private readonly ConcurrentDictionary<string, Criterion> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CriteriaSeedingStep(ITrackingTransactionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Idempotently seeds comp_preferred_floor=200000 and comp_high_priority_bar=220000
    /// for the given owner if they do not already exist.
    /// </summary>
    public async Task<Result<CriteriaSeedingResult>> SeedPredefinedCriteriaAsync(
        string ownerId,
        DateOnly defaultDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);

        var createdCount = 0;
        var skippedCount = 0;
        var effectiveDateTime = defaultDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var seeds = new (string Key, string Value)[]
        {
            ("comp_preferred_floor", "200000"),
            ("comp_high_priority_bar", "220000")
        };

        foreach (var (key, value) in seeds)
        {
            var existing = await _context.Criteria.GetActiveByKeyAsync(ownerId, key, cancellationToken);
            if (existing.IsSuccess && existing.Value is not null)
            {
                _cache[key] = existing.Value;
                skippedCount++;
                continue;
            }

            var criterion = Criterion.Create(
                ownerId: ownerId,
                key: key,
                category: CriterionCategory.Preference,
                value: value,
                provenance: ProvenanceKind.Stated,
                effectiveFrom: effectiveDateTime);

            var upsertResult = await _context.Criteria.UpsertCriterionAsync(criterion, cancellationToken);
            if (!upsertResult.IsSuccess)
            {
                return Result<CriteriaSeedingResult>.Failure(upsertResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to upsert criterion."));
            }

            var auditEntry = AuditLogEntry.CreateForTracking(
                ownerId: ownerId,
                actor: ImportActor,
                operation: AuditOperation.ImportCriterionRevision,
                entityType: "criterion",
                entityId: criterion.Id,
                resultingVersionToken: criterion.VersionToken,
                occurredAtUtc: DateTime.UtcNow,
                details: new AuditDetails(
                    ChangedFields: ["key", "value", "category", "provenance"],
                    After: new Dictionary<string, object?>
                    {
                        ["key"] = criterion.Key,
                        ["value"] = criterion.Value,
                        ["category"] = criterion.Category.ToString(),
                        ["provenance"] = criterion.Provenance.ToString()
                    }));

            await _context.AppendAuditEntryAsync(auditEntry, cancellationToken);
            _cache[key] = criterion;
            createdCount++;
        }

        return Result<CriteriaSeedingResult>.Success(new CriteriaSeedingResult(createdCount, skippedCount));
    }

    /// <summary>
    /// Retrieves an existing criterion by key or registers a new criterion on first use
    /// with inferred provenance and HardSkip category.
    /// </summary>
    public async Task<Result<Criterion>> GetOrCreateReasonCriterionAsync(
        string ownerId,
        string reasonPrimary,
        DateTimeOffset effectiveDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonPrimary);

        var rawKey = reasonPrimary.Trim();
        var key = TrackerImportFieldLimiter.Limit(rawKey, TrackerImportFieldLimiter.CriteriaKeyLimit, "criteria_key", new TrackerImportSummary())!;
        if (_cache.TryGetValue(key, out var cached))
        {
            return Result<Criterion>.Success(cached);
        }

        var existing = await _context.Criteria.GetActiveByKeyAsync(ownerId, key, cancellationToken);
        if (existing.IsSuccess && existing.Value is not null)
        {
            _cache[key] = existing.Value;
            return Result<Criterion>.Success(existing.Value);
        }

        var criterion = Criterion.Create(
            ownerId: ownerId,
            key: key,
            category: CriterionCategory.HardSkip,
            value: key,
            provenance: ProvenanceKind.Inferred,
            effectiveFrom: effectiveDate);

        var upsertResult = await _context.Criteria.UpsertCriterionAsync(criterion, cancellationToken);
        if (!upsertResult.IsSuccess)
        {
            return Result<Criterion>.Failure(upsertResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to upsert criterion."));
        }

        var auditEntry = AuditLogEntry.CreateForTracking(
            ownerId: ownerId,
            actor: ImportActor,
            operation: AuditOperation.ImportCriterionRevision,
            entityType: "criterion",
            entityId: criterion.Id,
            resultingVersionToken: criterion.VersionToken,
            occurredAtUtc: DateTime.UtcNow,
            details: new AuditDetails(
                ChangedFields: ["key", "value", "category", "provenance"],
                After: new Dictionary<string, object?>
                {
                    ["key"] = criterion.Key,
                    ["value"] = criterion.Value,
                    ["category"] = criterion.Category.ToString(),
                    ["provenance"] = criterion.Provenance.ToString()
                }));

        await _context.AppendAuditEntryAsync(auditEntry, cancellationToken);
        _cache[key] = criterion;

        return Result<Criterion>.Success(criterion);
    }
}
