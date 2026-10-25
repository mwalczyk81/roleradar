using System.Globalization;
using System.Net;
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
        var recon = context.Summary.GetOrCreateFileReconciliation("applications.csv");

        if (!File.Exists(filePath))
        {
            recon.IsAbsent = true;
            return Result<bool>.Success(true);
        }

        recon.IsAbsent = false;

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "applications.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                "File is present but empty."));
            return Result<bool>.Failure(ErrorCode.ValidationError, "applications.csv is present but empty.");
        }

        using var headerReader = new StringReader(content);
        var headers = _csvParser.ReadHeader(headerReader);
        var headerValidation = CsvHeaderValidator.Validate("applications.csv", headers, CsvHeaderValidator.ApplicationsHeaders);
        if (!headerValidation.IsSuccess)
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "applications.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                headerValidation.Error!.Value.Message));
            return Result<bool>.Failure(headerValidation.Error!.Value);
        }

        _standaloneNoteOrdinal = 0;

        using var reader = new StringReader(content);
        var defaultDate = context.DefaultDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var row in _csvParser.Read(reader, hasHeader: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recon.RecordRead();

            try
            {
                if (!row.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    recon.RecordSkipped("empty_row");
                    continue;
                }

                var decision = row["decision"]?.Trim();
                if (string.Equals(decision, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    context.Summary.ApplicationsUnchanged++;
                    recon.RecordSkipped("decision_skip");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(decision))
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "applications.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        $"Invalid decision value '{decision}'. Expected blank or 'skip'."));
                    recon.RecordErrored();
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
                    var noteCreated = false;
                    var noteResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
                    {
                        noteCreated = await ProcessStandaloneNoteAsync(context, row, defaultDate, ct);
                        return Result<bool>.Success(true);
                    }, cancellationToken);

                    if (!noteResult.IsSuccess)
                    {
                        context.Summary.Errors.Add(new TrackerImportRowError(
                            "applications.csv",
                            row.LineNumber,
                            noteResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                            noteResult.Error?.Message ?? "Failed to save note"));
                        recon.RecordErrored();
                    }
                    else if (noteCreated)
                    {
                        recon.RecordCreated();
                    }
                    else
                    {
                        recon.RecordUnchanged();
                    }
                    continue;
                }

                // 2. Application branch validation
                var rawCompany = Decode(row["company"])?.Trim();
                var rawTitle = (Decode(row["title"]) ?? Decode(row["role"]))?.Trim();
                if (string.IsNullOrWhiteSpace(rawCompany) || string.IsNullOrWhiteSpace(rawTitle))
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "applications.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        "Missing required company or title."));
                    recon.RecordErrored();
                    continue;
                }

                var appCreated = false;
                var appResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
                {
                    appCreated = await ProcessApplicationAsync(context, row, defaultDate, reviewFlags, ct);
                    return Result<bool>.Success(true);
                }, cancellationToken);

                if (!appResult.IsSuccess)
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "applications.csv",
                        row.LineNumber,
                        appResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                        appResult.Error?.Message ?? "Failed to save application"));
                    recon.RecordErrored();
                }
                else if (appCreated)
                {
                    recon.RecordCreated();
                }
                else
                {
                    recon.RecordUnchanged();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "applications.csv",
                    row.LineNumber,
                    ErrorCode.ValidationError.ToString(),
                    ex.Message));
                recon.RecordErrored();
            }
        }

        return Result<bool>.Success(true);
    }

    private int _standaloneNoteOrdinal;

    private async Task<bool> ProcessStandaloneNoteAsync(
        TrackerImportContext context,
        ICsvRecord row,
        DateOnly defaultDate,
        CancellationToken cancellationToken)
    {
        _standaloneNoteOrdinal++;
        var company = Decode(row["company"])?.Trim();
        var title = (Decode(row["title"]) ?? Decode(row["role"]))?.Trim();
        var appliedOn = (Decode(row["applied_on"]) ?? Decode(row["applied_date_md"]))?.Trim();
        var raw = Decode(row["raw"])?.Trim();
        var correctedNotes = Decode(row["corrected_notes"])?.Trim();
        var notes = Decode(row["notes"])?.Trim();
        var rawBody = !string.IsNullOrWhiteSpace(correctedNotes)
            ? correctedNotes
            : (!string.IsNullOrWhiteSpace(notes) ? notes : (!string.IsNullOrWhiteSpace(raw) ? raw : "Standalone note"));

        var body = TrackerImportFieldLimiter.LimitNoteBody(rawBody, context.Summary) ?? "Standalone note";

        var dateOutcome = DateInferenceRule.Resolve(appliedOn, defaultDate);
        TrackDateInference(context, dateOutcome);

        string fullTitle;
        if (!string.IsNullOrWhiteSpace(company) && !string.IsNullOrWhiteSpace(title))
        {
            fullTitle = $"{company} - {title}";
        }
        else if (!string.IsNullOrWhiteSpace(company))
        {
            var rawSnippet = !string.IsNullOrWhiteSpace(raw) ? raw : (!string.IsNullOrWhiteSpace(notes) ? notes : "Note");
            var snippet = rawSnippet.Length > 40 ? rawSnippet[..40].Trim() : rawSnippet.Trim();
            fullTitle = $"{company} - Imported note {_standaloneNoteOrdinal}: {snippet}";
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            fullTitle = title;
        }
        else
        {
            var rawSnippet = !string.IsNullOrWhiteSpace(raw) ? raw : (!string.IsNullOrWhiteSpace(notes) ? notes : (!string.IsNullOrWhiteSpace(correctedNotes) ? correctedNotes : "General"));
            var snippet = rawSnippet.Length > 40 ? rawSnippet[..40].Trim() : rawSnippet.Trim();
            fullTitle = $"Imported note {_standaloneNoteOrdinal}: {snippet}";
        }

        fullTitle = TrackerImportFieldLimiter.LimitNoteTitle(fullTitle, context.Summary) ?? $"Imported note {_standaloneNoteOrdinal}";

        var existingNoteRes = await _context.NoteExistsAsync(context.OwnerId, fullTitle, "job-search", cancellationToken);
        if (existingNoteRes.IsSuccess && existingNoteRes.Value)
        {
            return false;
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
        return true;
    }

    private async Task<bool> ProcessApplicationAsync(
        TrackerImportContext context,
        ICsvRecord row,
        DateOnly defaultDate,
        string? reviewFlags,
        CancellationToken cancellationToken)
    {
        var rawCompanyName = Decode(row["company"])?.Trim();
        var rawTitle = (Decode(row["title"]) ?? Decode(row["role"]))?.Trim();
        var rawReq = (Decode(row["req"]) ?? Decode(row["req_id"]))?.Trim();
        var appliedOn = (Decode(row["applied_on"]) ?? Decode(row["applied_date_md"]))?.Trim();
        var rejectedStr = Decode(row["rejected"])?.Trim();
        var isRejected = false;
        if (!string.IsNullOrWhiteSpace(rejectedStr))
        {
            var s = rejectedStr.Trim().ToLowerInvariant();
            if (s is "true" or "yes" or "1" or "t" or "y")
            {
                isRejected = true;
            }
            else if (s is "false" or "no" or "0" or "f" or "n")
            {
                isRejected = false;
            }
            else
            {
                context.Summary.RecordUnmappedValue("rejected", rejectedStr);
            }
        }
        var rejectedDateMd = Decode(row["rejected_date_md"])?.Trim();

        var companyName = TrackerImportFieldLimiter.LimitCompanyName(rawCompanyName, context.Summary);
        var title = TrackerImportFieldLimiter.LimitTitle(rawTitle, context.Summary) ?? "Role";
        var req = TrackerImportFieldLimiter.LimitRequisitionId(rawReq, context.Summary);

        // Also limit optional free-text inputs for referral, fit, resume_variant
        var referralRaw = Decode(row["referral"])?.Trim();
        TrackerImportFieldLimiter.LimitReferral(referralRaw, context.Summary);
        var isReferral = false;
        if (!string.IsNullOrWhiteSpace(referralRaw))
        {
            var s = referralRaw.Trim().ToLowerInvariant();
            if (s is "true" or "yes" or "1" or "t" or "y")
            {
                isReferral = true;
            }
            else if (s is "false" or "no" or "0" or "f" or "n")
            {
                isReferral = false;
            }
            else
            {
                context.Summary.RecordUnmappedValue("referral", referralRaw);
            }
        }

        var fitRaw = Decode(row["fit"])?.Trim();
        TrackerImportFieldLimiter.LimitFit(fitRaw, context.Summary);
        decimal? fitScore = null;
        string? fitNoteText = null;
        if (!string.IsNullOrWhiteSpace(fitRaw))
        {
            var cleanFit = fitRaw.TrimEnd('.');
            if (decimal.TryParse(cleanFit, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedScore) &&
                parsedScore is >= 0.0m and <= 10.0m)
            {
                fitScore = parsedScore;
            }
            else
            {
                context.Summary.RecordUnmappedValue("fit", fitRaw);
            }
            fitNoteText = $"fit: {cleanFit}";
            context.Summary.MappedToEventNotes++;
        }

        string? referralNoteText = null;
        if (isReferral)
        {
            referralNoteText = "referral=true";
            context.Summary.MappedToEventNotes++;
        }

        TrackerImportFieldLimiter.LimitResumeVariant(Decode(row["resume_variant"])?.Trim(), context.Summary);

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
        var rawNotes = Decode(!string.IsNullOrWhiteSpace(row["corrected_notes"]) ? row["corrected_notes"]?.Trim() : row["raw"]?.Trim());
        var combinedNotes = string.Join("; ", new[] { statedFlagText, fitNoteText, referralNoteText, rawNotes }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var canonicalCompany = !string.IsNullOrWhiteSpace(companyName) && context.CompanyAliases.TryGetValue(companyName, out var alias)
            ? alias
            : companyName;

        // Resolve matching posting: req first, then company + title (only if candidate has no req or matching req)
        Posting? foundPosting = null;
        if (!string.IsNullOrWhiteSpace(req))
        {
            if (!string.IsNullOrWhiteSpace(canonicalCompany) && context.PostingsByCompanyAndReq.TryGetValue($"{canonicalCompany}|{req}", out var byReq))
            {
                foundPosting = byReq;
            }
            else if (context.PostingsByReqOrUrl.TryGetValue(req, out var byLegacyReq))
            {
                foundPosting = byLegacyReq;
            }
            else if (!string.IsNullOrWhiteSpace(canonicalCompany))
            {
                Company? cObj = null;
                if (context.CanonicalCompanies.TryGetValue(canonicalCompany, out var foundComp))
                {
                    cObj = foundComp;
                }
                else
                {
                    var existingCompRes = await _context.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalCompany, cancellationToken);
                    if (existingCompRes.IsSuccess && existingCompRes.Value is not null)
                    {
                        cObj = existingCompRes.Value;
                    }
                }

                if (cObj is not null)
                {
                    var dedupC = $"{cObj.Id:D}|{req.Trim()}";
                    var byCRes = await _context.Postings.GetByDedupKeyCAsync(context.OwnerId, dedupC, cancellationToken);
                    if (byCRes.IsSuccess && byCRes.Value is not null)
                    {
                        foundPosting = byCRes.Value;
                    }
                }
            }
        }

        if (foundPosting is null && !string.IsNullOrWhiteSpace(canonicalCompany) && !string.IsNullOrWhiteSpace(title))
        {
            var titleKey = $"{canonicalCompany}|{TitleNormalizer.Normalize(title)}";
            if (context.PostingsByCompanyAndTitle.TryGetValue(titleKey, out var byTitle))
            {
                if (string.IsNullOrWhiteSpace(byTitle.RequisitionId) || string.Equals(byTitle.RequisitionId, req, StringComparison.OrdinalIgnoreCase))
                {
                    foundPosting = byTitle;
                }
            }
            else
            {
                Company? cObj = null;
                if (context.CanonicalCompanies.TryGetValue(canonicalCompany, out var foundComp))
                {
                    cObj = foundComp;
                }
                else
                {
                    var existingCompRes = await _context.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalCompany, cancellationToken);
                    if (existingCompRes.IsSuccess && existingCompRes.Value is not null)
                    {
                        cObj = existingCompRes.Value;
                    }
                }

                if (cObj is not null)
                {
                    var candRes = await _context.Postings.FindCandidatesByCompanyAndTitleAsync(context.OwnerId, cObj.Id, TitleNormalizer.Normalize(title), cancellationToken);
                    if (candRes.IsSuccess && candRes.Value is { Count: > 0 } candidates)
                    {
                        foreach (var c in candidates)
                        {
                            var byIdRes = await _context.Postings.GetByIdAsync(context.OwnerId, c.Id, cancellationToken);
                            if (byIdRes.IsSuccess && byIdRes.Value is not null)
                            {
                                if (string.IsNullOrWhiteSpace(byIdRes.Value.RequisitionId) || string.Equals(byIdRes.Value.RequisitionId, req, StringComparison.OrdinalIgnoreCase))
                                {
                                    foundPosting = byIdRes.Value;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
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
            context.AllPostings[foundPosting.Id] = foundPosting;
            if (!string.IsNullOrWhiteSpace(canonicalCompany))
            {
                context.PostingsByCompanyAndTitle[$"{canonicalCompany}|{foundPosting.NormalizedTitle}"] = foundPosting;
                if (!string.IsNullOrWhiteSpace(foundPosting.RequisitionId))
                {
                    context.PostingsByCompanyAndReq[$"{canonicalCompany}|{foundPosting.RequisitionId}"] = foundPosting;
                    context.PostingsByReqOrUrl[foundPosting.RequisitionId] = foundPosting;
                }
            }
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
                title: title,
                sourceKind: SourceKind.Aggregator,
                originalUrl: null,
                requisitionId: req,
                remoteType: RemoteType.Unknown,
                score: fitScore,
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
            context.AllPostings[newPosting.Id] = newPosting;
            if (!string.IsNullOrWhiteSpace(canonicalCompany))
            {
                context.PostingsByCompanyAndTitle[$"{canonicalCompany}|{newPosting.NormalizedTitle}"] = newPosting;
                if (!string.IsNullOrWhiteSpace(newPosting.RequisitionId))
                {
                    context.PostingsByCompanyAndReq[$"{canonicalCompany}|{newPosting.RequisitionId}"] = newPosting;
                    context.PostingsByReqOrUrl[newPosting.RequisitionId] = newPosting;
                }
            }
        }

        // Parse resume variant with validation
        var rvRaw = Decode(row["resume_variant"])?.Trim();
        var resumeVariant = ResumeVariant.Tailored;
        if (string.IsNullOrWhiteSpace(rvRaw))
        {
            resumeVariant = ResumeVariant.Tailored;
            context.Summary.ResumeVariantsDefaulted++;
        }
        else if (TrackingEnumExtensions.TryParseResumeVariant(rvRaw, out var parsedRv))
        {
            resumeVariant = parsedRv;
        }
        else
        {
            context.Summary.RecordUnmappedValue("resume_variant", rvRaw);
            resumeVariant = ResumeVariant.Tailored;
            context.Summary.ResumeVariantsDefaulted++;
        }

        DesiredSalary? desiredSalary = null;
        if (decimal.TryParse(row["desired_salary"], NumberStyles.Any, CultureInfo.InvariantCulture, out var sal))
        {
            if (sal < 0)
            {
                throw new ArgumentException($"Desired salary minimum must be >= 0: '{sal}'.");
            }
            desiredSalary = new DesiredSalary(Min: sal, Max: sal, Currency: "USD");
        }

        var existingAppRes = await _context.Applications.GetByPostingIdAsync(context.OwnerId, postingId, cancellationToken);
        if (existingAppRes.IsSuccess && existingAppRes.Value is not null)
        {
            context.Summary.ApplicationsUnchanged++;
            return false;
        }

        var submittedDateStr = dateOutcome.ResolvedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var outcome = isRejected ? ApplicationOutcome.Rejected : ApplicationOutcome.Applied;
        var outcomeReasonRaw = isRejected ? (!string.IsNullOrWhiteSpace(combinedNotes) ? combinedNotes : "Rejected") : null;
        var outcomeReason = TrackerImportFieldLimiter.LimitOutcomeReason(outcomeReasonRaw, context.Summary);

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
        var appliedNotes = TrackerImportFieldLimiter.LimitEventNotes(
            !string.IsNullOrWhiteSpace(combinedNotes) ? combinedNotes : null,
            context.Summary);

        var appliedEvent = ApplicationEvent.Create(
            ownerId: context.OwnerId,
            applicationId: application.Id,
            eventDate: submittedDateStr,
            eventType: ApplicationEventType.Applied,
            notes: appliedNotes,
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

            var rejectedNotes = TrackerImportFieldLimiter.LimitEventNotes(
                !string.IsNullOrWhiteSpace(rawNotes) ? rawNotes : "Rejected",
                context.Summary);

            var rejectedEvent = ApplicationEvent.Create(
                ownerId: context.OwnerId,
                applicationId: application.Id,
                eventDate: rejDateStr,
                eventType: ApplicationEventType.Rejected,
                notes: rejectedNotes,
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

        return true;
    }

    private static string? Decode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : WebUtility.HtmlDecode(value);

    private static void TrackDateInference(TrackerImportContext context, ResolvedDateOutcome outcome)
    {
        if (outcome.Kind == DateResolutionKind.DefaultedBlank) context.Summary.DatesDefaulted++;
        if (outcome.Kind is DateResolutionKind.InferredRollback or DateResolutionKind.InferredSameYear) context.Summary.DatesInferred++;
        if (outcome.IsFlagged) context.Summary.DatesFlagged++;
    }
}
