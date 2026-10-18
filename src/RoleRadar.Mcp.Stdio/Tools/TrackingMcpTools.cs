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
    private readonly IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto> _trackerQueryHandler;

    public TrackingMcpTools(
        ICommandHandler<RecordPostingCommand, PostingRecordResultDto> recordPostingHandler,
        ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto> transitionPostingHandler,
        IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto> trackerQueryHandler)
    {
        _recordPostingHandler = recordPostingHandler ?? throw new ArgumentNullException(nameof(recordPostingHandler));
        _transitionPostingHandler = transitionPostingHandler ?? throw new ArgumentNullException(nameof(transitionPostingHandler));
        _trackerQueryHandler = trackerQueryHandler ?? throw new ArgumentNullException(nameof(trackerQueryHandler));
    }

    [McpServerTool(Name = "posting_record"), Description("Creates, ingests, or explicitly updates a tracked job posting with company, title, URL, ATS identifiers, compensation, location, score, culture rating, screening note link, signals, and resurface policy, enforcing tiered deduplication (tiers A-D).")]
    public async Task<string> PostingRecordAsync(
        [Description("Optional posting GUID for explicit update mode.")] Guid? postingId = null,
        [Description("Optimistic concurrency token required when postingId is supplied.")] Guid? versionToken = null,
        [Description("Target company identifier (required for create/ingest).")] Guid? companyId = null,
        [Description("Job posting title (1..200 characters).")] string? title = null,
        [Description("Original job posting URL (max 1000 characters).")] string? url = null,
        [Description("Source kind ('ats' or 'aggregator').")] string? sourceKind = null,
        [Description("ATS tenant identifier (e.g. 'acmesoftware').")] string? atsTenant = null,
        [Description("Requisition or job ID within the ATS.")] string? requisitionId = null,
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
            DuplicateOfId: duplicateOfId);

        var result = await _recordPostingHandler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            var err = result.Error!.Value;
            return JsonSerializer.Serialize(new { error = new { code = err.Code.ToString(), message = err.Message } }, JsonOptions);
        }

        return JsonSerializer.Serialize(result.Value, JsonOptions);
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
        if (!result.IsSuccess)
        {
            var err = result.Error!.Value;
            return JsonSerializer.Serialize(new { error = new { code = err.Code.ToString(), message = err.Message } }, JsonOptions);
        }

        return JsonSerializer.Serialize(result.Value, JsonOptions);
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
        [Description("Deduplication key lookup value for entity 'posting_dedup_lookup'.")] string? dedupKey = null,
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
        if (!result.IsSuccess)
        {
            var err = result.Error!.Value;
            return JsonSerializer.Serialize(new { error = new { code = err.Code.ToString(), message = err.Message } }, JsonOptions);
        }

        return JsonSerializer.Serialize(result.Value, JsonOptions);
    }
}
