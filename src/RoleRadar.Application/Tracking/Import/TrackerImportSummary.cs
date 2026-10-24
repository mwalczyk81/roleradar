namespace RoleRadar.Application.Tracking.Import;

public sealed record TrackerImportRowError(
    string SourceFile,
    int LineNumber,
    string ErrorCode,
    string Message);

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
