using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public static class CsvHeaderValidator
{
    public static readonly IReadOnlyList<string> AppendixHeaders = new[]
    {
        "source", "proposed_disposition", "company", "title", "req", "comp_detected",
        "comp_is_estimate", "remote_mentioned", "details_raw", "added_md", "reason_primary",
        "reason_all", "proposed_resurface", "company_also_in_db", "review_flags", "reason_text",
        "decision", "corrected_notes"
    };

    public static readonly IReadOnlyList<string> ApplicationsHeaders = new[]
    {
        "kind", "company", "title", "req", "applied_on", "rejected", "rejected_date_md",
        "desired_salary", "referral", "fit", "resume_variant", "people_detected",
        "review_flags", "raw", "decision", "corrected_notes"
    };

    public static readonly IReadOnlyList<string> WatchlistHeaders = new[]
    {
        "company", "proposed_status", "text", "decision", "corrected_notes"
    };

    public static readonly IReadOnlyList<string> CompanyAliasesHeaders = new[]
    {
        "alias", "canonical"
    };

    public static Result<bool> Validate(string fileName, IReadOnlyList<string> actualHeaders, IReadOnlyList<string> expectedHeaders)
    {
        if (actualHeaders == null || actualHeaders.Count == 0)
        {
            return Result<bool>.Failure(ErrorCode.ValidationError, $"Missing header row in {fileName}.");
        }

        var missing = expectedHeaders
            .Where(exp => !actualHeaders.Any(act => string.Equals(act.Trim(), exp, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missing.Count > 0)
        {
            return Result<bool>.Failure(ErrorCode.ValidationError, $"Missing or misspelled column '{missing[0]}' in {fileName}.");
        }

        var unknown = actualHeaders
            .Where(act => !string.IsNullOrWhiteSpace(act) && !expectedHeaders.Any(exp => string.Equals(exp, act.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (unknown.Count > 0)
        {
            return Result<bool>.Failure(ErrorCode.ValidationError, $"Unrecognized column '{unknown[0].Trim()}' in {fileName}.");
        }

        return Result<bool>.Success(true);
    }
}
