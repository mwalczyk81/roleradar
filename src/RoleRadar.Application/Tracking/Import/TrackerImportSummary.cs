namespace RoleRadar.Application.Tracking.Import;

public sealed record TrackerImportRowError(
    string SourceFile,
    int LineNumber,
    string ErrorCode,
    string Message);

public sealed class FileReconciliationSummary
{
    public required string FileName { get; init; }
    public int Read { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Unchanged { get; set; }
    public int Errored { get; set; }
    public Dictionary<string, int> SkippedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> UnchangedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int TotalSkipped => SkippedByReason.Values.Sum();
    public int TotalAccountedFor => Created + Updated + Unchanged + TotalSkipped + Errored;

    public void RecordRead() => Read++;
    public void RecordCreated() => Created++;
    public void RecordUpdated() => Updated++;
    public void RecordErrored() => Errored++;

    public void RecordUnchanged(string? reason = null)
    {
        Unchanged++;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            UnchangedByReason.TryGetValue(reason, out var count);
            UnchangedByReason[reason] = count + 1;
        }
    }

    public void RecordSkipped(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        SkippedByReason.TryGetValue(reason, out var count);
        SkippedByReason[reason] = count + 1;
    }
}

public sealed class TrackerImportSummary
{
    public bool IsDryRun { get; set; }
    public int CompaniesCreated { get; set; }
    public int CompaniesUpdated { get; set; }
    public int CompaniesUnchanged { get; set; }
    public int PostingsCreated { get; set; }
    public int PostingsUpdated { get; set; }
    public int PostingsUnchanged { get; set; }
    public int ApplicationsCreated { get; set; }
    public int ApplicationsUnchanged { get; set; }
    public int StandaloneNotesCreated { get; set; }
    public int CriteriaCreated { get; set; }
    public int DatesDefaulted { get; set; }
    public int DatesInferred { get; set; }
    public int DatesFlagged { get; set; }
    public int CultureTruncations { get; set; }
    public int CultureUnmapped { get; set; }
    public int UnclaimedAppliedPostings { get; set; }

    public Dictionary<string, FileReconciliationSummary> FileReconciliations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FileReconciliationSummary GetOrCreateFileReconciliation(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!FileReconciliations.TryGetValue(fileName, out var recon))
        {
            recon = new FileReconciliationSummary { FileName = fileName };
            FileReconciliations[fileName] = recon;
        }
        return recon;
    }

    public Dictionary<string, int> UnmappedValues { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> UnmappedValueSamples { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void RecordUnmappedValue(string fieldName, string? rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        UnmappedValues.TryGetValue(fieldName, out var count);
        UnmappedValues[fieldName] = count + 1;

        if (string.Equals(fieldName, "culture_level", StringComparison.OrdinalIgnoreCase))
        {
            CultureUnmapped++;
        }

        if (!string.IsNullOrWhiteSpace(rawValue))
        {
            if (!UnmappedValueSamples.TryGetValue(fieldName, out var list))
            {
                list = new List<string>();
                UnmappedValueSamples[fieldName] = list;
            }

            if (!list.Contains(rawValue) && list.Count < 50)
            {
                list.Add(rawValue);
            }
        }
    }

    public Dictionary<string, int> FieldTruncations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void RecordFieldTruncation(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        FieldTruncations.TryGetValue(fieldName, out var count);
        FieldTruncations[fieldName] = count + 1;
        if (string.Equals(fieldName, "culture_summary", StringComparison.OrdinalIgnoreCase))
        {
            CultureTruncations++;
        }
    }

    public List<string> AutoCreatedCompanies { get; } = new();
    public List<string> UnclaimedAppliedPostingTitles { get; } = new();
    public List<string> PeopleDetected { get; } = new();
    public List<TrackerImportRowError> Errors { get; } = new();

    public bool IsIdempotentNoOp =>
        CompaniesCreated == 0 &&
        PostingsCreated == 0 &&
        ApplicationsCreated == 0 &&
        StandaloneNotesCreated == 0 &&
        CriteriaCreated == 0 &&
        Errors.Count == 0;
}
