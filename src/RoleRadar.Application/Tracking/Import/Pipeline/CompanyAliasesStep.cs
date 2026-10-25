using System.Net;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;

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
        var recon = context.Summary.GetOrCreateFileReconciliation("company-aliases.csv");

        if (!File.Exists(aliasesPath))
        {
            recon.IsAbsent = true;
            return Task.FromResult(Result<bool>.Success(true));
        }

        recon.IsAbsent = false;

        var content = File.ReadAllText(aliasesPath);
        if (string.IsNullOrWhiteSpace(content))
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "company-aliases.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                "File is present but empty."));
            return Task.FromResult(Result<bool>.Failure(ErrorCode.ValidationError, "company-aliases.csv is present but empty."));
        }

        using var headerReader = new StringReader(content);
        var headers = _csvParser.ReadHeader(headerReader);
        var headerValidation = CsvHeaderValidator.Validate("company-aliases.csv", headers, CsvHeaderValidator.CompanyAliasesHeaders);
        if (!headerValidation.IsSuccess)
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "company-aliases.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                headerValidation.Error!.Value.Message));
            return Task.FromResult(Result<bool>.Failure(headerValidation.Error!.Value));
        }

        using var reader = new StringReader(content);
        foreach (var row in _csvParser.Read(reader, hasHeader: true))
        {
            recon.RecordRead();

            if (!row.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
            {
                recon.RecordSkipped("empty_row");
                continue;
            }

            var alias = Decode(row["alias"])?.Trim();
            var canonical = Decode(row["canonical"])?.Trim();

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

    private static string? Decode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : WebUtility.HtmlDecode(value);
}
