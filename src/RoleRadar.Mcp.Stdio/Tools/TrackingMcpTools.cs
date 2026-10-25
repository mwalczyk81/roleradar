using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Application.Tracking.Queries;

namespace RoleRadar.Mcp.Stdio.Tools;

[McpServerToolType]
public sealed class TrackingMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ICommandHandler<RecordPostingCommand, PostingRecordResultDto> _recordPostingHandler;
    private readonly ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> _transitionPostingHandler;
    private readonly ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto> _recordCompanyHandler;
    private readonly ICommandHandler<TrackerLogCommand, TrackerLogResultDto> _trackerLogHandler;
    private readonly IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto> _trackerQueryHandler;
    private readonly ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto> _recordApplicationHandler;

    public TrackingMcpTools(
        ICommandHandler<RecordPostingCommand, PostingRecordResultDto> recordPostingHandler,
        ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> transitionPostingHandler,
        ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto> recordCompanyHandler,
        ICommandHandler<TrackerLogCommand, TrackerLogResultDto> trackerLogHandler,
        IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto> trackerQueryHandler,
        ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto> recordApplicationHandler)
    {
        _recordPostingHandler = recordPostingHandler ?? throw new ArgumentNullException(nameof(recordPostingHandler));
        _transitionPostingHandler = transitionPostingHandler ?? throw new ArgumentNullException(nameof(transitionPostingHandler));
        _recordCompanyHandler = recordCompanyHandler ?? throw new ArgumentNullException(nameof(recordCompanyHandler));
        _trackerLogHandler = trackerLogHandler ?? throw new ArgumentNullException(nameof(trackerLogHandler));
        _trackerQueryHandler = trackerQueryHandler ?? throw new ArgumentNullException(nameof(trackerQueryHandler));
        _recordApplicationHandler = recordApplicationHandler ?? throw new ArgumentNullException(nameof(recordApplicationHandler));
    }

    [McpServerTool(Name = "company_record"), Description("Creates or updates a tracked company profile with ownership type, category tags, description, and career sources (ATS endpoints) with upsert by id or natural key without accidental source deletion.")]
    public async Task<string> CompanyRecordAsync(
        [Description("Unique company identifier (generated if omitted).")] Guid? id = null,
        [Description("Unique company name (1..150 characters). Required for create.")] string? name = null,
        [Description("Summary of industry, ownership context, and headquarters (max 300 characters).")] string? description = null,
        [Description("Parent company name (max 150 characters).")] string? parentCompany = null,
        [Description("Ownership type ('public', 'private', 'pe_backed', 'vc_backed', 'mutual', 'subsidiary', 'nonprofit', 'government', 'unknown').")] string? ownershipType = null,
        [Description("Primary headquarters location (max 150 characters).")] string? headquarters = null,
        [Description("Tracking status ('target', 'watch', 'avoid', 'neutral').")] string? status = null,
        [Description("Category or industry tags (max 20 tags, each max 50 characters).")] string[]? categoryTags = null,
        [Description("Career source ATS endpoints to upsert.")] CareerSourceInputDto[]? careerSources = null,
        [Description("Optimistic concurrency token required when updating an existing company.")] Guid? versionToken = null,
        CancellationToken cancellationToken = default)
    {
        var command = new RecordCompanyCommand(
            Id: id,
            Name: name,
            Description: description,
            ParentCompany: parentCompany,
            OwnershipType: ownershipType,
            Headquarters: headquarters,
            Status: status,
            CategoryTags: categoryTags,
            CareerSources: careerSources,
            VersionToken: versionToken);

        var result = await _recordCompanyHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "posting_record"), Description("Creates, ingests, or explicitly updates a tracked job posting with company, title, URL, ATS identifiers, compensation, location, score, culture rating, screening note link, signals, and resurface policy, enforcing tiered deduplication (tiers A-D).")]
    public async Task<string> PostingRecordAsync(
        [Description("Optional posting GUID for explicit update mode.")] Guid? postingId = null,
        [Description("Optimistic concurrency token required when postingId is supplied.")] Guid? versionToken = null,
        [Description("Target company identifier (required for create/ingest).")] Guid? companyId = null,
        [Description("Job posting title (1..200 characters).")] string? title = null,
        [Description("Original job posting URL (max 1000 characters).")] string? url = null,
        [Description("Source kind ('ats' or 'aggregator').")] string? sourceKind = null,
        [Description("String ATS tenant identifier (e.g. 'acmesoftware').")] string? atsTenant = null,
        [Description("String requisition or job ID within the ATS (e.g. '19505', 'REQ-01').")] string? requisitionId = null,
        [Description("Geographic location or office specification.")] string? location = null,
        [Description("Remote work type ('remote', 'hybrid', 'onsite', 'unknown').")] string? remoteType = null,
        [Description("Minimum base compensation.")] decimal? compensationMin = null,
        [Description("Maximum base compensation.")] decimal? compensationMax = null,
        [Description("Compensation currency code (default USD).")] string? compensationCurrency = null,
        [Description("Compensation band or verbatim snippet text.")] string? compensationText = null,
        [Description("Posted date (YYYY-MM-DD).")] string? postedDate = null,
        [Description("Fit score between 0.0 and 10.0.")] decimal? score = null,
        [Description("Culture risk rating ('stable', 'moderate', 'high_risk', 'unknown').")] string? cultureLevel = null,
        [Description("Culture observation summary text.")] string? cultureSummary = null,
        [Description("Resurface policy ('never' or 'if_reopened').")] string? resurfacePolicy = null,
        [Description("Linked narrative screening note identifier.")] Guid? screeningNoteId = null,
        [Description("Pointer to canonical posting if this is a duplicate.")] Guid? duplicateOfId = null,
        [Description("Optional flag indicating whether compensation is high priority (null = not judged).")] bool? compHighPriority = null,
        [Description("Array of observed signals. Accepts string shorthand (e.g. 'no_salary', 'repost', 'parallel_posting', 'gone', 'phantom', 'deadline:YYYY-MM-DD') or object form with properties 'signalType' (string, e.g. 'no_salary', 'repost', 'parallel_posting', 'gone', 'phantom', 'deadline'), optional 'evidence' (string, defaults to 'reported by agent'), and optional 'deadlineDate' (string YYYY-MM-DD).")] SignalInputDto[]? signals = null,
        CancellationToken cancellationToken = default)
    {
        var command = new RecordPostingCommand(
            PostingId: postingId,
            VersionToken: versionToken,
            CompanyId: companyId,
            Title: title,
            Url: url,
            SourceKind: sourceKind,
            AtsTenant: atsTenant,
            RequisitionId: requisitionId,
            Location: location,
            RemoteType: remoteType,
            CompensationMin: compensationMin,
            CompensationMax: compensationMax,
            CompensationCurrency: compensationCurrency,
            CompensationText: compensationText,
            PostedDate: postedDate,
            Score: score,
            CultureLevel: cultureLevel,
            CultureSummary: cultureSummary,
            ResurfacePolicy: resurfacePolicy,
            ScreeningNoteId: screeningNoteId,
            DuplicateOfId: duplicateOfId,
            Signals: signals,
            CompHighPriority: compHighPriority);

        var result = await _recordPostingHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "posting_transition"), Description("Transitions a posting between dispositions (lead, scored, unscored, screened_out, closed, rejected) or reopens a decided posting, validating against the pure domain transition matrix. Note: target 'applied' is disallowed (applications must be created via application_record).")]
    public async Task<string> PostingTransitionAsync(
        [Description("Unique identifier of the posting to transition.")] Guid postingId,
        [Description("Target disposition ('lead', 'scored', 'unscored', 'screened_out', 'closed', 'rejected').")] string targetDisposition,
        [Description("Optimistic concurrency version token.")] Guid versionToken,
        [Description("Reason explaining the transition (mandatory for screened_out, closed, rejected).")] string? reason = null,
        [Description("Referenced screening criterion justifying the transition.")] Guid? criterionId = null,
        [Description("Resurface policy set upon transition to screened_out or closed ('never' or 'if_reopened').")] string? resurfacePolicy = null,
        [Description("Required override reason if explicitly reopening a rejected posting to lead.")] string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TransitionPostingCommand(
            PostingId: postingId,
            TargetDisposition: targetDisposition,
            VersionToken: versionToken,
            Reason: reason,
            CriterionId: criterionId,
            ResurfacePolicy: resurfacePolicy,
            OverrideReason: overrideReason);

        var result = await _transitionPostingHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "tracker_query"), Description("Unified read-only query tool for job search tracking data (postings, decided_postings, posting_dedup_lookup, companies, criteria, applications) with bounded paging (default 50, max 200).")]
    public async Task<string> TrackerQueryAsync(
        [Description("Entity discriminator ('postings', 'decided_postings', 'posting_dedup_lookup', 'companies', 'criteria', 'applications').")] string entity,
        [Description("Single posting lookup identifier for entity 'postings'.")] Guid? postingId = null,
        [Description("Filter by company identifier.")] Guid? companyId = null,
        [Description("Filter by posting disposition.")] string? disposition = null,
        [Description("Filter by company status ('target', 'watch', 'avoid', 'neutral').")] string? status = null,
        [Description("Filter by company category tag.")] string? tag = null,
        [Description("Filter by company name substring.")] string? name = null,
        [Description("Include retired career sources for entity 'companies'.")] bool includeRetired = false,
        [Description("Include stale companies overdue for check.")] bool includeStale = false,
        [Description("Stale threshold days.")] int staleAfterDays = 7,
        [Description("Point-in-time effective date for criteria (YYYY-MM-DD).")] string? effectiveDate = null,
        [Description("String deduplication key lookup value for entity 'posting_dedup_lookup' (e.g. '<companyId>|<req>' or URL).")] string? dedupKey = null,
        [Description("1-based page index (default: 1).")] int page = 1,
        [Description("Bounded page size (default: 50, max: 200).")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var request = new TrackerQueryRequest(
            Entity: entity,
            PostingId: postingId,
            CompanyId: companyId,
            Disposition: disposition,
            Status: status,
            Tag: tag,
            Name: name,
            IncludeRetired: includeRetired,
            IncludeStale: includeStale,
            StaleAfterDays: staleAfterDays,
            EffectiveDate: effectiveDate,
            DedupKey: dedupKey,
            Page: page,
            PageSize: pageSize);

        var result = await _trackerQueryHandler.HandleAsync(request, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "tracker_log"), Description("Appends immutable operational tracking records: creates/revises/deactivates screening criteria or logs search run execution batches linked to report notes.")]
    public async Task<string> TrackerLogAsync(
        [Description("Action discriminator ('criterion_revision', 'criterion_deactivate', 'search_run').")] string action,
        [Description("Screening criterion key (max 100 characters).")] string? key = null,
        [Description("String screening criterion category (e.g. 'hard_skip', 'weight', 'preference').")] string? category = null,
        [Description("Screening criterion rule specification or threshold (max 500 characters).")] string? value = null,
        [Description("Criterion provenance ('stated', 'inferred').")] string? provenance = null,
        [Description("UTC timestamp when rule becomes effective (ISO 8601).")] string? effectiveFrom = null,
        [Description("Optimistic concurrency token of the active criterion being superseded or deactivated.")] Guid? versionToken = null,
        [Description("Start time of search run (ISO 8601).")] string? startedAt = null,
        [Description("Count of newly discovered postings in search run.")] int? newPostingsCount = null,
        [Description("Linked narrative report note ID.")] Guid? reportNoteId = null,
        [Description("Summary notes (max 500 characters).")] string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TrackerLogCommand(
            Action: action,
            Key: key,
            Category: category,
            Value: value,
            Provenance: provenance,
            EffectiveFrom: effectiveFrom,
            VersionToken: versionToken,
            StartedAt: startedAt,
            NewPostingsCount: newPostingsCount,
            ReportNoteId: reportNoteId,
            Notes: notes);

        var result = await _trackerLogHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    [McpServerTool(Name = "application_record"), Description("Creates or updates a job application, appends timeline events, records resume variant pointers and desired salary, and supports atomic reopen-and-apply override for decided postings.")]
    public async Task<string> ApplicationRecordAsync(
        [Description("Unique posting identifier being applied to.")] Guid postingId,
        [Description("Application action ('create' or 'append_event').")] string action = "",
        [Description("Submission date (YYYY-MM-DD, required for 'create').")] string? submittedDate = null,
        [Description("Desired minimum base salary.")] decimal? desiredSalaryMin = null,
        [Description("Desired maximum base salary.")] decimal? desiredSalaryMax = null,
        [Description("Desired salary currency code (default 'USD').")] string? desiredSalaryCurrency = null,
        [Description("Target start date (YYYY-MM-DD).")] string? targetStartDate = null,
        [Description("Resume variant ('tailored', 'as_is', 'ic_framed').")] string? resumeVariant = null,
        [Description("External resume pointer/identifier (file content is strictly forbidden).")] string? resumePointer = null,
        [Description("Optional referral contact identifier.")] Guid? referralContactId = null,
        [Description("Submission confirmation reference code.")] string? confirmationCode = null,
        [Description("Override flag required to apply to screened_out or closed posting.")] bool overrideDecided = false,
        [Description("Justification required when overrideDecided is true.")] string? overrideReason = null,
        [Description("Timeline event payload (required if action is 'append_event').")] ApplicationEventInputDto? @event = null,
        CancellationToken cancellationToken = default)
    {
        var command = new RecordApplicationCommand(
            PostingId: postingId,
            Action: action,
            SubmittedDate: submittedDate,
            DesiredSalaryMin: desiredSalaryMin,
            DesiredSalaryMax: desiredSalaryMax,
            DesiredSalaryCurrency: desiredSalaryCurrency,
            TargetStartDate: targetStartDate,
            ResumeVariant: resumeVariant,
            ResumePointer: resumePointer,
            ReferralContactId: referralContactId,
            ConfirmationCode: confirmationCode,
            OverrideDecided: overrideDecided,
            OverrideReason: overrideReason,
            Event: @event);

        var result = await _recordApplicationHandler.HandleAsync(command, cancellationToken);
        return SerializeResult(result);
    }

    private static string SerializeResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return JsonSerializer.Serialize(
                new
                {
                    ok = true,
                    data = result.Value
                },
                JsonOptions);
        }

        var error = result.Error!.Value;
        return JsonSerializer.Serialize(
            new
            {
                ok = false,
                error = new
                {
                    code = error.Code.ToString(),
                    message = error.Message,
                    metadata = error.Metadata
                }
            },
            JsonOptions);
    }
}
