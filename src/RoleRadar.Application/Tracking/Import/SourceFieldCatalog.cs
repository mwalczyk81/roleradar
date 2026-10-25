namespace RoleRadar.Application.Tracking.Import;

public sealed record SourceFieldDefinition(string File, string Field, string Handling);

public static class SourceFieldCatalog
{
    public static readonly IReadOnlyList<SourceFieldDefinition> AllFields = new SourceFieldDefinition[]
    {
        // appendix-rows.csv
        new("appendix-rows.csv", "source", "stored in Posting.SourceKind"),
        new("appendix-rows.csv", "proposed_disposition", "stored in Posting.Disposition"),
        new("appendix-rows.csv", "company", "stored in Company.Name"),
        new("appendix-rows.csv", "title", "stored in Posting.Title"),
        new("appendix-rows.csv", "req", "stored in Posting.RequisitionId"),
        new("appendix-rows.csv", "comp_detected", "stored in Posting.Compensation"),
        new("appendix-rows.csv", "comp_is_estimate", "merged into Posting.Compensation"),
        new("appendix-rows.csv", "remote_mentioned", "dropped by design"),
        new("appendix-rows.csv", "details_raw", "merged into Posting.DispositionReason"),
        new("appendix-rows.csv", "added_md", "stored in Posting.CreatedAtUtc"),
        new("appendix-rows.csv", "reason_primary", "stored in Posting.DispositionCriterionId"),
        new("appendix-rows.csv", "reason_all", "merged into Posting.DispositionReason"),
        new("appendix-rows.csv", "proposed_resurface", "stored in Posting.ResurfacePolicy"),
        new("appendix-rows.csv", "company_also_in_db", "dropped by design"),
        new("appendix-rows.csv", "review_flags", "dropped by design"),
        new("appendix-rows.csv", "reason_text", "stored in Posting.DispositionReason"),
        new("appendix-rows.csv", "decision", "dropped by design"),
        new("appendix-rows.csv", "corrected_notes", "dropped by design"),

        // applications.csv
        new("applications.csv", "kind", "stored in application routing"),
        new("applications.csv", "company", "stored in Company.Name / Posting.CompanyId"),
        new("applications.csv", "title", "stored in Posting.Title / Note.Title"),
        new("applications.csv", "req", "stored in Posting.RequisitionId"),
        new("applications.csv", "applied_on", "stored in Application.SubmittedDate"),
        new("applications.csv", "rejected", "stored in Application.Outcome / Posting.Disposition"),
        new("applications.csv", "rejected_date_md", "stored in ApplicationEvent.EventDate"),
        new("applications.csv", "desired_salary", "stored in Application.DesiredSalary"),
        new("applications.csv", "referral", "merged into ApplicationEvent.Notes"),
        new("applications.csv", "fit", "merged into ApplicationEvent.Notes / Posting.Score"),
        new("applications.csv", "resume_variant", "stored in Application.ResumeVariant"),
        new("applications.csv", "people_detected", "dropped by design"),
        new("applications.csv", "review_flags", "dropped by design"),
        new("applications.csv", "raw", "merged into ApplicationEvent.Notes / Note.Body"),
        new("applications.csv", "decision", "dropped by design"),
        new("applications.csv", "corrected_notes", "dropped by design"),

        // watchlist-companies.csv
        new("watchlist-companies.csv", "company", "stored in Company.Name"),
        new("watchlist-companies.csv", "proposed_status", "stored in Company.Status"),
        new("watchlist-companies.csv", "text", "stored in Company.Description"),
        new("watchlist-companies.csv", "decision", "dropped by design"),
        new("watchlist-companies.csv", "corrected_notes", "dropped by design"),

        // company-aliases.csv
        new("company-aliases.csv", "alias", "stored in CompanyAlias.Alias"),
        new("company-aliases.csv", "canonical", "stored in CompanyAlias.Canonical"),

        // postings.json
        new("postings.json", "added", "stored in Posting.CreatedAtUtc"),
        new("postings.json", "appliedLabel", "dropped by design"),
        new("postings.json", "comp", "stored in Posting.Compensation"),
        new("postings.json", "compHigh", "stored in Posting.CompHighPriority"),
        new("postings.json", "company", "stored in Company.Name / Posting.CompanyId"),
        new("postings.json", "culture", "stored in Posting.CultureSummary"),
        new("postings.json", "cultureLevel", "stored in Posting.CultureLevel"),
        new("postings.json", "first", "stored in Posting.CreatedAtUtc"),
        new("postings.json", "ghost", "stored in PostingSignals"),
        new("postings.json", "href", "stored in Posting.OriginalUrl"),
        new("postings.json", "loc", "stored in Posting.Location"),
        new("postings.json", "note", "stored in Note.Body / Posting.ScreeningNoteId"),
        new("postings.json", "order", "dropped by design"),
        new("postings.json", "parent", "stored in Posting.AtsTenant / audit"),
        new("postings.json", "posted", "stored in Posting.PostedAt"),
        new("postings.json", "score", "stored in Posting.Score"),
        new("postings.json", "status", "stored in Posting.Disposition"),
        new("postings.json", "tier", "dropped by design"),
        new("postings.json", "title", "stored in Posting.Title"),
    };

    public static readonly ILookup<string, SourceFieldDefinition> ByFile =
        AllFields.ToLookup(f => f.File, StringComparer.OrdinalIgnoreCase);

    public static string GetHandling(string file, string field)
    {
        var found = AllFields.FirstOrDefault(f =>
            string.Equals(f.File, file, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(f.Field, field, StringComparison.OrdinalIgnoreCase));

        return found?.Handling ?? throw new KeyNotFoundException($"No source field mapping found for {file}:{field}");
    }
}
