using System.Globalization;
using System.Text;
using RoleRadar.Application.Tracking.Import.Commands;

namespace RoleRadar.Application.Tracking.Import.Report;

public static class TrackerImportReportGenerator
{
    public static bool IsInsideGitWorkTree(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(path);
        var dir = new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return true;
            }

            dir = dir.Parent;
        }

        return false;
    }

    public static async Task GenerateReportAsync(
        string reportPath,
        ImportTrackerCommand command,
        TrackerImportSummary summary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reportPath);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(summary);

        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        // 1. Header & Execution Mode
        sb.AppendLine("================================================================================");
        sb.AppendLine("                 RoleRadar Tracker Import Execution Report                      ");
        sb.AppendLine("================================================================================");
        sb.AppendLine(inv, $"Timestamp (UTC):     {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
        sb.AppendLine(inv, $"Execution Mode:      {(summary.IsDryRun ? "DRY_RUN" : "APPLIED")}");
        sb.AppendLine(inv, $"Bundle Directory:    {command.BundleDirectory}");
        sb.AppendLine(inv, $"Default Date Anchor: {(command.DefaultDate.HasValue ? command.DefaultDate.Value.ToString("yyyy-MM-dd", inv) : "None")}");
        sb.AppendLine();

        // 2. Aggregated Counts Summary
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                          Aggregated Counts Summary                             ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(inv, $"Companies Created:                     {summary.CompaniesCreated}");
        sb.AppendLine(inv, $"Companies Updated:                     {summary.CompaniesUpdated}");
        sb.AppendLine(inv, $"Companies Unchanged/Skipped:           {summary.CompaniesUnchanged}");
        sb.AppendLine(inv, $"Postings Created:                      {summary.PostingsCreated}");
        sb.AppendLine(inv, $"Postings Updated:                      {summary.PostingsUpdated}");
        sb.AppendLine(inv, $"Postings Unchanged/Skipped:            {summary.PostingsUnchanged}");
        sb.AppendLine(inv, $"Applications Created:                  {summary.ApplicationsCreated}");
        sb.AppendLine(inv, $"Applications Unchanged/Skipped:        {summary.ApplicationsUnchanged}");
        sb.AppendLine(inv, $"Standalone Notes Created:              {summary.StandaloneNotesCreated}");
        sb.AppendLine(inv, $"Criteria Registered:                   {summary.CriteriaCreated}");
        sb.AppendLine(inv, $"Signals Created:                       {summary.SignalsCreated}");
        sb.AppendLine(inv, $"Mapped to Event Notes:                 {summary.MappedToEventNotes}");
        sb.AppendLine(inv, $"Resume Variants Defaulted:             {summary.ResumeVariantsDefaulted}");
        sb.AppendLine(inv, $"Dates Defaulted:                       {summary.DatesDefaulted}");
        sb.AppendLine(inv, $"Dates Inferred (Rollback/SameYear):    {summary.DatesInferred}");
        sb.AppendLine(inv, $"Dates Flagged (or Scores Flagged):     {summary.DatesFlagged}");
        sb.AppendLine(inv, $"Culture Summaries Truncated (>500):    {summary.CultureTruncations}");
        sb.AppendLine(inv, $"Culture Levels Unmapped:               {summary.CultureUnmapped}");
        sb.AppendLine(inv, $"Total Field Truncations:               {summary.FieldTruncations.Values.Sum()}");
        sb.AppendLine(inv, $"Unclaimed Applied Roles Remaining:     {summary.UnclaimedAppliedPostings}");
        sb.AppendLine(inv, $"Total Row Errors Encountered:          {summary.Errors.Count}");
        if (summary.FieldTruncations.Count > 0)
        {
            sb.AppendLine("  Field Truncation Breakdown:");
            foreach (var (field, count) in summary.FieldTruncations.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(inv, $"    - {field}: {count}");
            }
        }
        sb.AppendLine();

        // 2b. File Reconciliation Summary
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                     File-Level Reconciliation Summary                          ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        if (summary.FileReconciliations.Count == 0)
        {
            sb.AppendLine("No files processed.");
        }
        else
        {
            sb.AppendLine(string.Format(inv, "{0,-25} {1,8} {2,8} {3,8} {4,10} {5,8} {6,8}", "File", "Read", "Created", "Updated", "Unchanged", "Skipped", "Errored"));
            sb.AppendLine(new string('-', 81));
            foreach (var f in summary.FileReconciliations.Values.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase))
            {
                if (f.IsAbsent)
                {
                    sb.AppendLine(string.Format(inv, "{0,-25} {1,8} {2,8} {3,8} {4,10} {5,8} {6,8}", f.FileName, "absent", "-", "-", "-", "-", "-"));
                }
                else
                {
                    sb.AppendLine(string.Format(inv, "{0,-25} {1,8} {2,8} {3,8} {4,10} {5,8} {6,8}", f.FileName, f.Read, f.Created, f.Updated, f.Unchanged, f.TotalSkipped, f.Errored));
                    if (f.SkippedByReason.Count > 0)
                    {
                        sb.AppendLine("    Skipped Breakdown:");
                        foreach (var (reason, count) in f.SkippedByReason.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            sb.AppendLine(inv, $"      - {reason}: {count}");
                        }
                    }
                    if (f.UnchangedByReason.Count > 0)
                    {
                        sb.AppendLine("    Unchanged Breakdown:");
                        foreach (var (reason, count) in f.UnchangedByReason.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                        {
                            sb.AppendLine(inv, $"      - {reason}: {count}");
                        }
                    }
                }
            }
        }
        sb.AppendLine();

        if (summary.DispositionHistogram.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("                       Post-Run Disposition Histogram                           ");
            sb.AppendLine("--------------------------------------------------------------------------------");
            foreach (var (disposition, count) in summary.DispositionHistogram.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(inv, $"  - {disposition}: {count}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                              Mapping Decisions                                 ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("  - Remote Type: 'remote_mentioned' is preserved as RemoteType.Unknown with 'remote mentioned'");
        sb.AppendLine("    recorded in the posting summary/notes, avoiding premature classification as full remote.");
        sb.AppendLine("  - Resume Variant: Domain entity requires non-nullable ResumeVariant; blank values default");
        sb.AppendLine("    to ResumeVariant.Tailored (counted under Resume Variants Defaulted).");
        sb.AppendLine("  - Fit & Referral: Fit values and referral=true are mapped to applied event notes");
        sb.AppendLine("    (counted under Mapped to Event Notes).");
        sb.AppendLine();

        if (summary.UnmappedValues.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("                              Unmapped Values                                   ");
            sb.AppendLine("--------------------------------------------------------------------------------");
            foreach (var (field, count) in summary.UnmappedValues.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var samples = summary.UnmappedValueSamples.TryGetValue(field, out var list) && list.Count > 0
                    ? $" (samples: {string.Join(", ", list.Take(5))})"
                    : string.Empty;
                sb.AppendLine(inv, $"  - {field}: {count}{samples}");
            }
            sb.AppendLine();
        }

        // 3. Companies Requiring Alias Review
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                     Companies Requiring Alias Review                           ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        if (summary.AutoCreatedCompanies.Count == 0)
        {
            sb.AppendLine("None. All referenced companies existed or matched known aliases.");
        }
        else
        {
            sb.AppendLine("The following companies were auto-created with status 'neutral' and a blank description.");
            sb.AppendLine("Inspect for near-miss names and add canonical aliases to company-aliases.csv before --apply:");
            foreach (var comp in summary.AutoCreatedCompanies.Distinct())
            {
                sb.AppendLine(inv, $"  - {comp}");
            }
        }
        sb.AppendLine();

        // 4. Unclaimed "Applied" Postings
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                         Unclaimed Applied Postings                             ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        if (summary.UnclaimedAppliedPostingTitles.Count == 0)
        {
            sb.AppendLine("None. All applied postings in postings.json had matching applications.");
        }
        else
        {
            sb.AppendLine("The following postings had status 'applied' in postings.json but no matching record");
            sb.AppendLine("in applications.csv. They have been imported in disposition 'scored':");
            foreach (var title in summary.UnclaimedAppliedPostingTitles)
            {
                sb.AppendLine(inv, $"  - {title}");
            }
        }
        sb.AppendLine();

        // 5. Contacts Detected for Manual Entry
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                     Contacts Detected for Manual Entry                         ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        if (summary.PeopleDetected.Count == 0)
        {
            sb.AppendLine("None. No detected individuals or contains_people flags in applications.csv.");
        }
        else
        {
            sb.AppendLine("Per Constitution Principle I, contact records are NEVER created automatically.");
            sb.AppendLine("Use 'roleradar contact add <name>' to record interactions with the following contacts:");
            foreach (var detected in summary.PeopleDetected)
            {
                sb.AppendLine(inv, $"  - {detected}");
            }
        }
        sb.AppendLine();

        // 6. Row Validation Errors & Warnings
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("                    Row Validation Errors & Skipped Rows                        ");
        sb.AppendLine("--------------------------------------------------------------------------------");
        if (summary.Errors.Count == 0)
        {
            sb.AppendLine("None. All rows processed cleanly without validation errors.");
        }
        else
        {
            foreach (var err in summary.Errors)
            {
                sb.AppendLine(inv, $"  - [{err.SourceFile}:{err.LineNumber}] ({err.ErrorCode}) {err.Message}");
            }
        }
        sb.AppendLine();

        var dir = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(reportPath, sb.ToString(), Encoding.UTF8, cancellationToken);
    }
}
