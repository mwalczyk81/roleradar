using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class CompanyAliasesStep : ITrackerImportStep
{
    private readonly ICsvParser _csvParser;

    public CompanyAliasesStep(ICsvParser csvParser)
    {
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
    }

    public Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var aliasesPath = Path.Combine(context.BundleDirectory, "company-aliases.csv");
        if (!File.Exists(aliasesPath))
        {
            return Task.FromResult(Result<bool>.Success(true));
        }

        var recon = context.Summary.GetOrCreateFileReconciliation("company-aliases.csv");

        using var reader = new StreamReader(aliasesPath);
        foreach (var row in _csvParser.Read(reader, hasHeader: true))
        {
            recon.RecordRead();

            if (!row.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
            {
                recon.RecordSkipped("empty_row");
                continue;
            }

            var alias = row["alias"]?.Trim();
            var canonical = row["canonical"]?.Trim();

            if (!string.IsNullOrWhiteSpace(alias) && !string.IsNullOrWhiteSpace(canonical))
            {
                context.CompanyAliases[alias] = canonical;
                recon.RecordCreated();
            }
            else
            {
                recon.RecordSkipped("missing_required_fields");
            }
        }

        return Task.FromResult(Result<bool>.Success(true));
    }
}
