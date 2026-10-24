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
        if (!File.Exists(filePath))
        {
            return Result<bool>.Success(true);
        }

        using var reader = new StreamReader(filePath);
        foreach (var row in _csvParser.Read(reader, hasHeader: true))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var companyName = row["company"]?.Trim();
            if (string.IsNullOrWhiteSpace(companyName))
            {
                continue;
            }

            var decision = row["decision"]?.Trim();
            if (string.Equals(decision, "skip", StringComparison.OrdinalIgnoreCase))
            {
                context.Summary.CompaniesUnchanged++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(decision))
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "watchlist-companies.csv",
                    row.LineNumber,
                    $"Invalid decision value '{decision}'. Expected blank or 'skip'."));
                continue;
            }

            var tier = row["tier"]?.Trim();
            var status = MapStatus(tier);

            var notes = !string.IsNullOrWhiteSpace(row["corrected_notes"])
                ? row["corrected_notes"]!.Trim()
                : (!string.IsNullOrWhiteSpace(row["text"])
                    ? row["text"]!.Trim()
                    : row["notes"]?.Trim() ?? string.Empty);
            var canonicalName = context.CompanyAliases.TryGetValue(companyName, out var alias) ? alias : companyName;

            var existing = await _context.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalName, cancellationToken);
            if (existing.IsSuccess && existing.Value is not null)
            {
                var existingCompany = existing.Value;
                context.CanonicalCompanies[canonicalName] = existingCompany;
                context.Summary.CompaniesUnchanged++;
                continue;
            }

            var company = Company.Create(
                ownerId: context.OwnerId,
                name: canonicalName,
                status: status,
                description: notes);

            var upsertResult = await _context.Companies.UpsertCompanyAsync(company, cancellationToken);
            if (!upsertResult.IsSuccess)
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "watchlist-companies.csv",
                    row.LineNumber,
                    upsertResult.Error?.Message ?? "Failed to save company"));
                continue;
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

            await _context.AppendAuditEntryAsync(auditEntry, cancellationToken);

            context.CanonicalCompanies[canonicalName] = company;
            context.Summary.CompaniesCreated++;
        }

        return Result<bool>.Success(true);
    }

    private static CompanyStatus MapStatus(string? tier)
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
            _ => CompanyStatus.Neutral
        };
    }
}
