namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Canonical MongoDB BSON field name constants for all tracking collections.
/// These constants are shared between document mappers, repositories, and the integrity verifier
/// to prevent field-name drift.
/// </summary>
public static class TrackingBsonFieldNames
{
    public static class Common
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class Postings
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string CompanyId = "company_id";
        public const string Title = "title";
        public const string NormalizedTitle = "normalized_title";
        public const string Url = "url";
        public const string NormalizedUrl = "normalized_url";
        public const string SourceKind = "source_kind";
        public const string AtsTenant = "ats_tenant";
        public const string RequisitionId = "requisition_id";
        public const string DedupKeyA = "dedup_key_a";
        public const string DedupKeyB = "dedup_key_b";
        public const string DedupKeyC = "dedup_key_c";
        public const string Location = "location";
        public const string RemoteType = "remote_type";
        public const string Compensation = "compensation";
        public const string CompHighPriority = "comp_high_priority";
        public const string PostedDate = "posted_date";
        public const string FirstSeenAt = "first_seen_at";
        public const string LastVerifiedAt = "last_verified_at";
        public const string ClosedDate = "closed_date";
        public const string Score = "score";
        public const string CultureLevel = "culture_level";
        public const string CultureSummary = "culture_summary";
        public const string Disposition = "disposition";
        public const string DispositionReason = "disposition_reason";
        public const string DispositionCriterionId = "disposition_criterion_id";
        public const string ResurfacePolicy = "resurface_policy";
        public const string DecidedAt = "decided_at";
        public const string DuplicateOfId = "duplicate_of_id";
        public const string ScreeningNoteId = "screening_note_id";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class Companies
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string Name = "name";
        public const string NormalizedName = "normalized_name";
        public const string Status = "status";
        public const string OwnershipType = "ownership_type";
        public const string CategoryTags = "category_tags";
        public const string Description = "description";
        public const string ParentCompany = "parent_company";
        public const string Headquarters = "headquarters";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class Applications
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string PostingId = "posting_id";
        public const string SubmittedDate = "submitted_date";
        public const string ResumeVariant = "resume_variant";
        public const string DesiredSalary = "desired_salary";
        public const string TargetStartDate = "target_start_date";
        public const string ResumePointer = "resume_pointer";
        public const string ReferralContactId = "referral_contact_id";
        public const string ConfirmationCode = "confirmation_code";
        public const string CurrentStage = "current_stage";
        public const string LastStageDate = "last_stage_date";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class ApplicationEvents
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string ApplicationId = "application_id";
        public const string EventDate = "event_date";
        public const string EventType = "event_type";
        public const string EventSource = "event_source";
        public const string Notes = "notes";
        public const string CreatedAt = "created_at";
    }

    public static class Notes
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string CompanyId = "company_id";
        public const string PostingId = "posting_id";
        public const string ApplicationId = "application_id";
        public const string Title = "title";
        public const string Body = "body";
        public const string Area = "area";
        public const string Status = "status";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class Signals
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string PostingId = "posting_id";
        public const string SignalType = "signal_type";
        public const string Evidence = "evidence";
        public const string ObservedAt = "observed_at";
        public const string DeadlineDate = "deadline_date";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
    }

    public static class SearchRuns
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string StartedAt = "started_at";
        public const string CompletedAt = "completed_at";
        public const string NewPostingsCount = "new_postings_count";
        public const string ReportNoteId = "report_note_id";
        public const string Notes = "notes";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
    }

    public static class Contacts
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string Name = "name";
        public const string Role = "role";
        public const string CompanyAffiliation = "company_affiliation";
        public const string CompanyId = "company_id";
        public const string Relationship = "relationship";
        public const string Notes = "notes";
        public const string IsRedacted = "is_redacted";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class Criteria
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string Key = "key";
        public const string Category = "category";
        public const string Value = "value";
        public const string Provenance = "provenance";
        public const string EffectiveFrom = "effective_from";
        public const string EffectiveTo = "effective_to";
        public const string IsActive = "is_active";
        public const string VersionToken = "version_token";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
    }

    public static class AuditLog
    {
        public const string Id = "_id";
        public const string OwnerId = "owner_id";
        public const string Actor = "actor";
        public const string Operation = "operation";
        public const string EntityType = "entity_type";
        public const string EntityId = "entity_id";
        public const string Details = "details";
        public const string TargetNoteId = "target_note_id";
        public const string NoteRevisionId = "note_revision_id";
        public const string AppendedText = "appended_text";
        public const string ResultingVersionToken = "resulting_version_token";
        public const string ResolvedDefaultDate = "resolved_default_date";
        public const string OccurredAt = "occurred_at";
        public const string BeforeSummary = "before_summary";
        public const string AfterSummary = "after_summary";
    }
}
