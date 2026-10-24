using System.Globalization;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class ApplicationsStep : ITrackerImportStep
{
    private const string ImportActor = "cli-import-tracker";
    private readonly ICsvParser _csvParser;
    private readonly ITrackingTransactionContext _context;
    private readonly Func<int, Task>? _faultInjectionHook;

    public ApplicationsStep(
        ICsvParser csvParser,
        ITrackingTransactionContext context,
        Func<int, Task>? faultInjectionHook = null)
    {
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _faultInjectionHook = faultInjectionHook;
    }

    public async Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var filePath = Path.Combine(context.BundleDirectory, "applications.csv");
        if (!File.Exists(filePath))
        {
            return Result<bool>.Success(true);
        }

        using var reader = new StreamReader(filePath);
        var defaultDate = context.DefaultDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var row in _csvParser.Read(reader, hasHeader: true))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decision = row["decision"]?.Trim();
            if (string.Equals(decision, "skip", StringComparison.OrdinalIgnoreCase))
            {
                context.Summary.ApplicationsUnchanged++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(decision))
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "applications.csv",
                    row.LineNumber,
                    ErrorCode.ValidationError.ToString(),
                    $"Invalid decision value '{decision}'. Expected blank or 'skip'."));
                continue;
            }

            // Flag detection for people
            var peopleDetected = row["people_detected"]?.Trim();
            var reviewFlags = row["review_flags"]?.Trim();
            var containsPeopleCol = row["contains_people"]?.Trim();
            var containsPeople = (!string.IsNullOrWhiteSpace(reviewFlags) &&
                                  reviewFlags.Contains("contains_people", StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrWhiteSpace(containsPeopleCol) &&
                                  (containsPeopleCol.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                   containsPeopleCol.Contains("contains_people", StringComparison.OrdinalIgnoreCase)));

            if (!string.IsNullOrWhiteSpace(peopleDetected) || containsPeople)
            {
                var details = !string.IsNullOrWhiteSpace(peopleDetected) ? peopleDetected : "Contains people flagged in review_flags";
                context.Summary.PeopleDetected.Add($"[applications.csv:{row.LineNumber}] {details}");
            }

            var kind = row["kind"]?.Trim();

            // 1. Standalone Note branch
            if (string.Equals(kind, "standalone_note", StringComparison.OrdinalIgnoreCase))
            {
                var noteResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
                {
                    await ProcessStandaloneNoteAsync(context, row, defaultDate, ct);
                    return Result<bool>.Success(true);
                }, cancellationToken);

                if (!noteResult.IsSuccess)
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "applications.csv",
                        row.LineNumber,
                        noteResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                        noteResult.Error?.Message ?? "Failed to save note"));
                }
                continue;
            }

            // 2. Application branch
            var appResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
            {
                await ProcessApplicationAsync(context, row, defaultDate, reviewFlags, ct);
                return Result<bool>.Success(true);
            }, cancellationToken);

            if (!appResult.IsSuccess)
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "applications.csv",
                    row.LineNumber,
                    appResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                    appResult.Error?.Message ?? "Failed to save application"));
            }
        }

        return Result<bool>.Success(true);
    }

    private async Task ProcessStandaloneNoteAsync(
        TrackerImportContext context,
        ICsvRecord row,
        DateOnly defaultDate,
        CancellationToken cancellationToken)
    {
        var company = row["company"]?.Trim();
        var title = (row["title"] ?? row["role"])?.Trim();
        var appliedOn = (row["applied_on"] ?? row["applied_date_md"])?.Trim();
        var raw = row["raw"]?.Trim();
        var correctedNotes = row["corrected_notes"]?.Trim();
        var notes = row["notes"]?.Trim();
        var body = !string.IsNullOrWhiteSpace(correctedNotes)
            ? correctedNotes
            : (!string.IsNullOrWhiteSpace(notes) ? notes : (!string.IsNullOrWhiteSpace(raw) ? raw : "Standalone note"));

        var dateOutcome = DateInferenceRule.Resolve(appliedOn, defaultDate);
        TrackDateInference(context, dateOutcome);

        var fullTitle = !string.IsNullOrWhiteSpace(company)
            ? $"{company} - {(string.IsNullOrWhiteSpace(title) ? "Note" : title)}"
            : (string.IsNullOrWhiteSpace(title) ? "Note" : title);

        var existingNoteRes = await _context.NoteExistsAsync(context.OwnerId, fullTitle, "job-search", cancellationToken);
        if (existingNoteRes.IsSuccess && existingNoteRes.Value)
        {
            return;
        }

        var noteDateStr = dateOutcome.ResolvedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var note = Note.Create(
            ownerId: context.OwnerId,
            title: fullTitle,
            body: body,
            tags: ["tracker-import"],
            area: "job-search",
            provenance: new ProvenanceMarker(RoleRadar.Domain.Notes.ProvenanceKind.Stated, noteDateStr, null),
            createdAtUtc: dateOutcome.ResolvedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        await _context.UpsertNoteAsync(note, cancellationToken);

        var audit = AuditLogEntry.CreateForImportCreate(
            ownerId: context.OwnerId,
            actor: ImportActor,
            targetNoteId: note.Id,
            resultingVersionToken: note.VersionToken,
            resolvedDefaultDate: defaultDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            occurredAtUtc: DateTime.UtcNow,
            afterSummary: AuditContentSummary.FromNote(note));

        await _context.AppendAuditEntryAsync(audit, cancellationToken);
        context.Summary.StandaloneNotesCreated++;
    }

    private async Task ProcessApplicationAsync(
        TrackerImportContext context,
        ICsvRecord row,
        DateOnly defaultDate,
        string? reviewFlags,
        CancellationToken cancellationToken)
    {
        var companyName = row["company"]?.Trim();
        var title = (row["title"] ?? row["role"])?.Trim();
        var req = (row["req"] ?? row["req_id"])?.Trim();
        var appliedOn = (row["applied_on"] ?? row["applied_date_md"])?.Trim();
        var rejectedStr = row["rejected"]?.Trim();
        var isRejected = bool.TryParse(rejectedStr, out var rej) && rej;
        var rejectedDateMd = row["rejected_date_md"]?.Trim();

        var dateOutcome = DateInferenceRule.Resolve(appliedOn, defaultDate);
        TrackDateInference(context, dateOutcome);

        ResolvedDateOutcome? rejectedDateOutcome = null;
        if (isRejected)
        {
            var res = DateInferenceRule.Resolve(rejectedDateMd, defaultDate);
            rejectedDateOutcome = res;
            TrackDateInference(context, res);
        }

        var isDateStatedFlag = !string.IsNullOrWhiteSpace(reviewFlags) &&
                               reviewFlags.Contains("date_is_stated_not_submitted", StringComparison.OrdinalIgnoreCase);

        var statedFlagText = isDateStatedFlag ? "date_is_stated_not_submitted" : null;
        var rawNotes = !string.IsNullOrWhiteSpace(row["corrected_notes"]) ? row["corrected_notes"]?.Trim() : row["raw"]?.Trim();
        var combinedNotes = string.Join("; ", new[] { statedFlagText, rawNotes }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var canonicalCompany = !string.IsNullOrWhiteSpace(companyName) && context.CompanyAliases.TryGetValue(companyName, out var alias)
            ? alias
            : companyName;

        // Resolve matching posting
        Posting? foundPosting = null;
        if (!string.IsNullOrWhiteSpace(req) && context.PostingsByReqOrUrl.TryGetValue(req, out var byReq))
        {
            foundPosting = byReq;
        }
        else if (!string.IsNullOrWhiteSpace(canonicalCompany) && !string.IsNullOrWhiteSpace(title) &&
                 context.PostingsByCompanyAndTitle.TryGetValue($"{canonicalCompany}|{TitleNormalizer.Normalize(title)}", out var byTitle))
        {
            foundPosting = byTitle;
        }

        Guid postingId;
        var appliedDateTime = dateOutcome.ResolvedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        if (foundPosting is not null)
        {
            foundPosting.TransitionToApplied(appliedDateTime);
            if (isRejected)
            {
                var rejDateTime = (rejectedDateOutcome?.ResolvedDate ?? dateOutcome.ResolvedDate)
                    .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                foundPosting.SynchronizeFromApplicationEvent(PostingDisposition.Rejected, "Rejected", rejDateTime);
            }

            await _context.Postings.UpsertPostingAsync(foundPosting, cancellationToken);

            var auditPosting = AuditLogEntry.CreateForTracking(
                ownerId: context.OwnerId,
                actor: ImportActor,
                operation: AuditOperation.ImportPostingTransition,
                entityType: "posting",
                entityId: foundPosting.Id,
                resultingVersionToken: foundPosting.VersionToken,
                occurredAtUtc: DateTime.UtcNow,
                details: new AuditDetails(
                    ChangedFields: ["disposition"],
                    After: new Dictionary<string, object?>
                    {
                        ["disposition"] = foundPosting.Disposition.ToString()
                    }));

            await _context.AppendAuditEntryAsync(auditPosting, cancellationToken);
            context.Summary.PostingsUpdated++;
            context.ClaimedPostingIds.Add(foundPosting.Id);

            if (context.Summary.UnclaimedAppliedPostingTitles.Remove(foundPosting.Title))
            {
                context.Summary.UnclaimedAppliedPostings = context.Summary.UnclaimedAppliedPostingTitles.Count;
            }

            postingId = foundPosting.Id;
        }
        else
        {
            // Auto-create company and posting if missing
            var companyId = Guid.NewGuid();
            if (!string.IsNullOrWhiteSpace(canonicalCompany))
            {
                if (!context.CanonicalCompanies.TryGetValue(canonicalCompany, out var company))
                {
                    var existingComp = await _context.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalCompany, cancellationToken);
                    if (existingComp.IsSuccess && existingComp.Value is not null)
                    {
                        company = existingComp.Value;
                    }
                    else
                    {
                        company = Company.Create(
                            ownerId: context.OwnerId,
                            name: canonicalCompany,
                            status: CompanyStatus.Neutral,
                            description: string.Empty);

                        await _context.Companies.UpsertCompanyAsync(company, cancellationToken);

                        var auditC = AuditLogEntry.CreateForTracking(
                            ownerId: context.OwnerId,
                            actor: ImportActor,
                            operation: AuditOperation.ImportCompanyCreate,
                            entityType: "company",
                            entityId: company.Id,
                            resultingVersionToken: company.VersionToken,
                            occurredAtUtc: DateTime.UtcNow,
                            details: new AuditDetails(
                                ChangedFields: ["name", "status"],
                                After: new Dictionary<string, object?>
                                {
                                    ["name"] = company.Name,
                                    ["status"] = company.Status.ToString()
                                }));

                        await _context.AppendAuditEntryAsync(auditC, cancellationToken);
                        context.Summary.CompaniesCreated++;
                        context.Summary.AutoCreatedCompanies.Add(canonicalCompany);
                    }

                    context.CanonicalCompanies[canonicalCompany] = company;
                }

                companyId = company.Id;
            }

            var newPosting = Posting.Create(
                ownerId: context.OwnerId,
                companyId: companyId,
                title: title ?? "Role",
                sourceKind: SourceKind.Aggregator,
                originalUrl: null,
                requisitionId: req,
                remoteType: RemoteType.Unknown,
                disposition: isRejected ? PostingDisposition.Rejected : PostingDisposition.Applied,
                dispositionReason: isRejected ? "Rejected" : null,
                now: appliedDateTime);

            await _context.Postings.UpsertPostingAsync(newPosting, cancellationToken);

            var auditP = AuditLogEntry.CreateForTracking(
                ownerId: context.OwnerId,
                actor: ImportActor,
                operation: AuditOperation.ImportPostingCreate,
                entityType: "posting",
                entityId: newPosting.Id,
                resultingVersionToken: newPosting.VersionToken,
                occurredAtUtc: DateTime.UtcNow,
                details: new AuditDetails(
                    ChangedFields: ["title", "disposition"],
                    After: new Dictionary<string, object?>
                    {
                        ["title"] = newPosting.Title,
                        ["disposition"] = newPosting.Disposition.ToString()
                    }));

            await _context.AppendAuditEntryAsync(auditP, cancellationToken);
            context.Summary.PostingsCreated++;
            postingId = newPosting.Id;
        }

        // Create application
        var resumeVariant = TrackingEnumExtensions.TryParseResumeVariant(row["resume_variant"], out var rv)
            ? rv
            : ResumeVariant.Tailored;

        DesiredSalary? desiredSalary = null;
        if (decimal.TryParse(row["desired_salary"], NumberStyles.Any, CultureInfo.InvariantCulture, out var sal))
        {
            desiredSalary = new DesiredSalary(Min: sal, Max: sal, Currency: "USD");
        }

        var existingAppRes = await _context.Applications.GetByPostingIdAsync(context.OwnerId, postingId, cancellationToken);
        if (existingAppRes.IsSuccess && existingAppRes.Value is not null)
        {
            context.Summary.ApplicationsUnchanged++;
            return;
        }

        var submittedDateStr = dateOutcome.ResolvedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var outcome = isRejected ? ApplicationOutcome.Rejected : ApplicationOutcome.Applied;
        var outcomeReason = isRejected ? (!string.IsNullOrWhiteSpace(combinedNotes) ? combinedNotes : "Rejected") : null;

        var application = RoleRadar.Domain.Tracking.Application.Create(
            ownerId: context.OwnerId,
            postingId: postingId,
            submittedDate: submittedDateStr,
            resumeVariant: resumeVariant,
            desiredSalary: desiredSalary,
            outcome: outcome,
            outcomeReason: outcomeReason,
            now: appliedDateTime);

        await _context.Applications.UpsertApplicationAsync(application, cancellationToken);

        if (_faultInjectionHook is not null)
        {
            await _faultInjectionHook(row.LineNumber);
        }

        var auditApp = AuditLogEntry.CreateForTracking(
            ownerId: context.OwnerId,
            actor: ImportActor,
            operation: AuditOperation.ImportApplicationCreate,
            entityType: "application",
            entityId: application.Id,
            resultingVersionToken: application.VersionToken,
            occurredAtUtc: DateTime.UtcNow,
            details: new AuditDetails(
                ChangedFields: ["posting_id", "submitted_date", "outcome"],
                After: new Dictionary<string, object?>
                {
                    ["posting_id"] = application.PostingId.ToString(),
                    ["submitted_date"] = application.SubmittedDate,
                    ["outcome"] = application.Outcome.ToString()
                }));

        await _context.AppendAuditEntryAsync(auditApp, cancellationToken);
        context.Summary.ApplicationsCreated++;

        // Add applied event
        var appliedEvent = ApplicationEvent.Create(
            ownerId: context.OwnerId,
            applicationId: application.Id,
            eventDate: submittedDateStr,
            eventType: ApplicationEventType.Applied,
            notes: !string.IsNullOrWhiteSpace(combinedNotes) ? combinedNotes : null,
            now: appliedDateTime);

        await _context.Applications.AppendEventAsync(appliedEvent, cancellationToken);

        var auditEvt1 = AuditLogEntry.CreateForTracking(
            ownerId: context.OwnerId,
            actor: ImportActor,
            operation: AuditOperation.ImportApplicationEventAppend,
            entityType: "application_event",
            entityId: appliedEvent.Id,
            resultingVersionToken: application.VersionToken,
            occurredAtUtc: DateTime.UtcNow,
            details: new AuditDetails(
                ChangedFields: ["event_type", "event_date"],
                After: new Dictionary<string, object?>
                {
                    ["event_type"] = appliedEvent.EventType.ToString(),
                    ["event_date"] = appliedEvent.EventDate
                }));

        await _context.AppendAuditEntryAsync(auditEvt1, cancellationToken);

        // Add rejected event if rejected
        if (isRejected)
        {
            var rejDateStr = (rejectedDateOutcome?.ResolvedDate ?? dateOutcome.ResolvedDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var rejDateTime = (rejectedDateOutcome?.ResolvedDate ?? dateOutcome.ResolvedDate)
                .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            var rejectedEvent = ApplicationEvent.Create(
                ownerId: context.OwnerId,
                applicationId: application.Id,
                eventDate: rejDateStr,
                eventType: ApplicationEventType.Rejected,
                notes: !string.IsNullOrWhiteSpace(rawNotes) ? rawNotes : "Rejected",
                now: rejDateTime);

            await _context.Applications.AppendEventAsync(rejectedEvent, cancellationToken);

            var auditEvt2 = AuditLogEntry.CreateForTracking(
                ownerId: context.OwnerId,
                actor: ImportActor,
                operation: AuditOperation.ImportApplicationEventAppend,
                entityType: "application_event",
                entityId: rejectedEvent.Id,
                resultingVersionToken: application.VersionToken,
                occurredAtUtc: DateTime.UtcNow,
                details: new AuditDetails(
                    ChangedFields: ["event_type", "event_date"],
                    After: new Dictionary<string, object?>
                    {
                        ["event_type"] = rejectedEvent.EventType.ToString(),
                        ["event_date"] = rejectedEvent.EventDate
                    }));

            await _context.AppendAuditEntryAsync(auditEvt2, cancellationToken);
        }
    }

    private static void TrackDateInference(TrackerImportContext context, ResolvedDateOutcome outcome)
    {
        if (outcome.Kind == DateResolutionKind.DefaultedBlank) context.Summary.DatesDefaulted++;
        if (outcome.Kind is DateResolutionKind.InferredRollback or DateResolutionKind.InferredSameYear) context.Summary.DatesInferred++;
        if (outcome.IsFlagged) context.Summary.DatesFlagged++;
    }
}
