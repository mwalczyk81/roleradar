using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class WatchlistCompaniesStep : ITrackerImportStep
{
    private const string ImportActor = "cli-import-tracker";
    private readonly ICsvParser _csvParser;
    private readonly ITrackingTransactionContext _context;

    public WatchlistCompaniesStep(ICsvParser csvParser, ITrackingTransactionContext context)
    {
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var filePath = Path.Combine(context.BundleDirectory, "watchlist-companies.csv");
        var recon = context.Summary.GetOrCreateFileReconciliation("watchlist-companies.csv");

        if (!File.Exists(filePath))
        {
            recon.IsAbsent = true;
            return Result<bool>.Success(true);
        }

        recon.IsAbsent = false;

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "watchlist-companies.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                "File is present but empty."));
            return Result<bool>.Failure(ErrorCode.ValidationError, "watchlist-companies.csv is present but empty.");
        }

        using var reader = new StringReader(content);
        foreach (var row in _csvParser.Read(reader, hasHeader: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recon.RecordRead();

            try
            {
                if (!row.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    recon.RecordSkipped("empty_row");
                    continue;
                }

                var companyName = row["company"]?.Trim();
                if (string.IsNullOrWhiteSpace(companyName))
                {
                    // If row has any non-blank field, missing company is a validation error
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "watchlist-companies.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        "Missing required 'company' column value."));
                    recon.RecordErrored();
                    continue;
                }

                var decision = row["decision"]?.Trim();
                if (string.Equals(decision, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    context.Summary.CompaniesUnchanged++;
                    recon.RecordSkipped("decision_skip");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(decision))
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "watchlist-companies.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        $"Invalid decision value '{decision}'. Expected blank or 'skip'."));
                    recon.RecordErrored();
                    continue;
                }

                var tier = row["tier"]?.Trim();
                var status = MapStatus(tier, context);

                var rawNotes = !string.IsNullOrWhiteSpace(row["corrected_notes"])
                    ? row["corrected_notes"]!.Trim()
                    : (!string.IsNullOrWhiteSpace(row["text"])
                        ? row["text"]!.Trim()
                        : row["notes"]?.Trim() ?? string.Empty);

                var canonicalName = context.CompanyAliases.TryGetValue(companyName, out var alias) ? alias : companyName;

                // Enforce domain limits via shared limiter
                canonicalName = TrackerImportFieldLimiter.LimitRequired(canonicalName, TrackerImportFieldLimiter.CompanyNameLimit, "company_name", context.Summary);
                var notes = TrackerImportFieldLimiter.LimitCompanyDescription(rawNotes, context.Summary) ?? string.Empty;

                var existing = await _context.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalName, cancellationToken);
                if (existing.IsSuccess && existing.Value is not null)
                {
                    var existingCompany = existing.Value;
                    context.CanonicalCompanies[canonicalName] = existingCompany;
                    context.Summary.CompaniesUnchanged++;
                    recon.RecordUnchanged();
                    continue;
                }

                Company? createdCompany = null;
                var rowResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
                {
                    var company = Company.Create(
                        ownerId: context.OwnerId,
                        name: canonicalName,
                        status: status,
                        description: notes);

                    var upsertResult = await txCtx.Companies.UpsertCompanyAsync(company, ct);
                    if (!upsertResult.IsSuccess)
                    {
                        return Result<bool>.Failure(upsertResult.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to save company"));
                    }

                    var auditEntry = AuditLogEntry.CreateForTracking(
                        ownerId: context.OwnerId,
                        actor: ImportActor,
                        operation: AuditOperation.ImportCompanyCreate,
                        entityType: "company",
                        entityId: company.Id,
                        resultingVersionToken: company.VersionToken,
                        occurredAtUtc: DateTime.UtcNow,
                        details: new AuditDetails(
                            ChangedFields: ["name", "status", "description"],
                            After: new Dictionary<string, object?>
                            {
                                ["name"] = company.Name,
                                ["status"] = company.Status.ToString()
                            }));

                    await txCtx.AppendAuditEntryAsync(auditEntry, ct);
                    createdCompany = company;
                    return Result<bool>.Success(true);
                }, cancellationToken);

                if (!rowResult.IsSuccess)
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "watchlist-companies.csv",
                        row.LineNumber,
                        rowResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                        rowResult.Error?.Message ?? "Failed to save company"));
                    recon.RecordErrored();
                    continue;
                }

                if (createdCompany is not null)
                {
                    context.CanonicalCompanies[canonicalName] = createdCompany;
                    context.Summary.CompaniesCreated++;
                    recon.RecordCreated();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "watchlist-companies.csv",
                    row.LineNumber,
                    ErrorCode.ValidationError.ToString(),
                    ex.Message));
                recon.RecordErrored();
            }
        }

        return Result<bool>.Success(true);
    }

    private static CompanyStatus MapStatus(string? tier, TrackerImportContext context)
    {
        if (string.IsNullOrWhiteSpace(tier))
        {
            return CompanyStatus.Neutral;
        }

        return tier.Trim().ToLowerInvariant() switch
        {
            "target" => CompanyStatus.Target,
            "watch" => CompanyStatus.Watch,
            "avoid" => CompanyStatus.Avoid,
            "neutral" => CompanyStatus.Neutral,
            _ => FallbackTier(context, tier)
        };
    }

    private static CompanyStatus FallbackTier(TrackerImportContext context, string rawTier)
    {
        context.Summary.RecordUnmappedValue("tier", rawTier);
        return CompanyStatus.Neutral;
    }
}
