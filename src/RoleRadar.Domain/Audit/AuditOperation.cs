namespace RoleRadar.Domain.Audit;

public static class AuditOperation
{
    // Note operations (001)
    public const string NoteCreate = "note_create";
    public const string NoteAppend = "note_append";
    public const string NoteReplaceSection = "note_replace_section";
    public const string NoteArchive = "note_archive";
    public const string ImportCreate = "import_create";
    public const string ImportOverwrite = "import_overwrite";

    // Tracking operations (002)
    public const string CompanyCreate = "company_create";
    public const string CompanyUpdate = "company_update";
    public const string CareerSourceUpsert = "career_source_upsert";

    public const string PostingCreate = "posting_create";
    public const string PostingUpdate = "posting_update";
    public const string PostingVerifiedBump = "posting_verified_bump";
    public const string PostingTransition = "posting_transition";
    public const string PostingSignalAppend = "posting_signal_append";

    public const string ApplicationCreate = "application_create";
    public const string ApplicationEventAppend = "application_event_append";

    public const string CriterionRevision = "criterion_revision";
    public const string CriterionDeactivate = "criterion_deactivate";

    public const string SearchRunLog = "search_run_log";

    public const string ContactAdd = "contact_add";
    public const string ContactRedact = "contact_redact";

    public static bool IsValid(string operation) =>
        operation is NoteCreate or NoteAppend or NoteReplaceSection or NoteArchive or ImportCreate or ImportOverwrite
            or CompanyCreate or CompanyUpdate or CareerSourceUpsert
            or PostingCreate or PostingUpdate or PostingVerifiedBump or PostingTransition or PostingSignalAppend
            or ApplicationCreate or ApplicationEventAppend
            or CriterionRevision or CriterionDeactivate
            or SearchRunLog
            or ContactAdd or ContactRedact;
}
