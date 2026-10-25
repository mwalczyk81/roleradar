using System.Globalization;
using System.Net;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class AppendixRowsStep : ITrackerImportStep
{
    private static readonly char[] ReasonSeparators = [',', ';'];
    private const string ImportActor = "cli-import-tracker";
    private readonly ICsvParser _csvParser;
    private readonly ITrackingTransactionContext _context;
    private readonly CriteriaSeedingStep _criteriaStep;

    public AppendixRowsStep(ICsvParser csvParser, ITrackingTransactionContext context, CriteriaSeedingStep criteriaStep)
    {
        _csvParser = csvParser ?? throw new ArgumentNullException(nameof(csvParser));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _criteriaStep = criteriaStep ?? throw new ArgumentNullException(nameof(criteriaStep));
    }

    public async Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var filePath = Path.Combine(context.BundleDirectory, "appendix-rows.csv");
        var recon = context.Summary.GetOrCreateFileReconciliation("appendix-rows.csv");

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
                "appendix-rows.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                "File is present but empty."));
            return Result<bool>.Failure(ErrorCode.ValidationError, "appendix-rows.csv is present but empty.");
        }

        using var headerReader = new StringReader(content);
        var headers = _csvParser.ReadHeader(headerReader);
        var headerValidation = CsvHeaderValidator.Validate("appendix-rows.csv", headers, CsvHeaderValidator.AppendixHeaders);
        if (!headerValidation.IsSuccess)
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "appendix-rows.csv",
                1,
                ErrorCode.ValidationError.ToString(),
                headerValidation.Error!.Value.Message));
            return Result<bool>.Failure(headerValidation.Error!.Value);
        }

        var records = new List<ICsvRecord>();
        using (var reader = new StringReader(content))
        {
            records.AddRange(_csvParser.Read(reader, hasHeader: true));
        }

        // Partition records: non-duplicates before duplicates so duplicate_of_id can resolve
        var nonDuplicates = new List<ICsvRecord>();
        var duplicates = new List<ICsvRecord>();

        foreach (var row in records)
        {
            var rawProposedDisp = (Decode(row["proposed_disposition"]) ?? Decode(row["disposition"]))?.Trim();
            var isDuplicate = string.Equals(rawProposedDisp, "duplicate", StringComparison.OrdinalIgnoreCase);

            if (isDuplicate)
            {
                duplicates.Add(row);
            }
            else
            {
                nonDuplicates.Add(row);
            }
        }

        var defaultDate = context.DefaultDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // Process non-duplicates first, then duplicates
        foreach (var row in nonDuplicates.Concat(duplicates))
        {
            cancellationToken.ThrowIfCancellationRequested();
            recon.RecordRead();

            foreach (var def in SourceFieldCatalog.ByFile["appendix-rows.csv"])
            {
                var val = Decode(row[def.Field])?.Trim();
                context.Summary.RecordFieldRead("appendix-rows.csv", def.Field, !string.IsNullOrWhiteSpace(val));
            }

            try
            {
                if (!row.Fields.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    recon.RecordSkipped("empty_row");
                    continue;
                }

                var companyName = Decode(row["company"])?.Trim();
                var title = Decode(row["title"])?.Trim();

                if (string.IsNullOrWhiteSpace(companyName) || string.IsNullOrWhiteSpace(title))
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "appendix-rows.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        "Missing required company or title."));
                    recon.RecordErrored();
                    continue;
                }

                var decision = Decode(row["decision"])?.Trim();
                if (string.Equals(decision, "skip", StringComparison.OrdinalIgnoreCase))
                {
                    context.Summary.PostingsUnchanged++;
                    recon.RecordSkipped("decision_skip");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(decision))
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "appendix-rows.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        $"Invalid decision value '{decision}'. Expected blank or 'skip'."));
                    recon.RecordErrored();
                    continue;
                }

                var canonicalCompany = context.CompanyAliases.TryGetValue(companyName, out var alias) ? alias : companyName;

                // Enforce domain limits
                canonicalCompany = TrackerImportFieldLimiter.LimitCompanyName(canonicalCompany, context.Summary)!;
                title = TrackerImportFieldLimiter.LimitTitle(title, context.Summary)!;

                // 2. Parse date
                var addedRaw = Decode(row["added_md"])?.Trim();
                var dateOutcome = DateInferenceRule.Resolve(addedRaw, defaultDate);
                if (dateOutcome.Kind == DateResolutionKind.DefaultedBlank) context.Summary.DatesDefaulted++;
                if (dateOutcome.Kind is DateResolutionKind.InferredRollback or DateResolutionKind.InferredSameYear) context.Summary.DatesInferred++;
                if (dateOutcome.IsFlagged) context.Summary.DatesFlagged++;

                var effectiveDate = dateOutcome.ResolvedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

                var autoCreatedCompany = false;
                Company? newCompany = null;
                Posting? updatedPosting = null;
                Posting? createdPosting = null;
                var isUnchanged = false;

                var rawReqId = (Decode(row["req"]) ?? Decode(row["req_id"]))?.Trim();
                var reqId = TrackerImportFieldLimiter.LimitRequisitionId(rawReqId, context.Summary);
                var rawReasonPrimary = Decode(row["reason_primary"])?.Trim();
                var reasonPrimary = TrackerImportFieldLimiter.LimitDispositionReason(rawReasonPrimary, context.Summary);
                var reasonAll = Decode(row["reason_all"])?.Trim();
                var rawProposedDisp = (Decode(row["proposed_disposition"]) ?? Decode(row["disposition"]))?.Trim();

                var rawComp = Decode(row["comp_detected"])?.Trim();
                var rawCompEstimate = Decode(row["comp_is_estimate"])?.Trim();
                Compensation? compensation = null;
                if (!string.IsNullOrWhiteSpace(rawComp))
                {
                    if (string.Equals(rawCompEstimate, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(rawCompEstimate, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!rawComp.Contains("estimate", StringComparison.OrdinalIgnoreCase))
                        {
                            rawComp = $"{rawComp} (estimate)";
                        }
                    }
                    var compText = TrackerImportFieldLimiter.LimitCompensationText(rawComp, context.Summary);
                    var (compMin, compMax) = PostingsJsonStep.TryParseCompensationRange(compText);
                    compensation = compText != null ? new Compensation(compMin, compMax, "USD", compText) : null;
                }

                var remoteMentioned = Decode(row["remote_mentioned"])?.Trim();
                var isRemoteMentioned = string.Equals(remoteMentioned, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(remoteMentioned, "true", StringComparison.OrdinalIgnoreCase);
                if (isRemoteMentioned || (!string.IsNullOrWhiteSpace(remoteMentioned) && !string.Equals(remoteMentioned, "no", StringComparison.OrdinalIgnoreCase) && !string.Equals(remoteMentioned, "false", StringComparison.OrdinalIgnoreCase)))
                {
                    context.Summary.RecordUnmappedValue("remote_mentioned", "not stored");
                }
                var remoteType = RemoteType.Unknown;
                string? cultureSummary = null;

                var isDuplicateRow = string.Equals(rawProposedDisp, "duplicate", StringComparison.OrdinalIgnoreCase);

                PostingDisposition targetDisposition;
                var normalizedDisp = rawProposedDisp?.ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

                if (isDuplicateRow)
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                }
                else if (normalizedDisp is "screened_out" or "screenedout")
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                }
                else if (normalizedDisp is "closed")
                {
                    targetDisposition = PostingDisposition.Closed;
                }
                else if (normalizedDisp is "unscored")
                {
                    targetDisposition = PostingDisposition.Unscored;
                }
                else if (normalizedDisp is "dismissed")
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                }
                else if (string.IsNullOrWhiteSpace(normalizedDisp))
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                }
                else
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "appendix-rows.csv",
                        row.LineNumber,
                        ErrorCode.ValidationError.ToString(),
                        $"Invalid proposed_disposition '{rawProposedDisp}'."));
                    recon.RecordErrored();
                    continue;
                }

                var rawReasonText = Decode(row["reason_text"])?.Trim();
                var rawDetails = Decode(row["details_raw"])?.Trim();

                var baseReason = !string.IsNullOrWhiteSpace(rawReasonText)
                    ? rawReasonText
                    : rawReasonPrimary;

                var combinedReason = baseReason ?? (isDuplicateRow ? "duplicate" : (normalizedDisp == "dismissed" ? "dismissed" : (normalizedDisp ?? "screened_out")));

                if (!string.IsNullOrWhiteSpace(rawDetails))
                {
                    combinedReason += " | details: " + rawDetails;
                }

                if (!string.IsNullOrWhiteSpace(reasonAll))
                {
                    var otherCodes = reasonAll
                        .Split(ReasonSeparators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, rawReasonPrimary, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (otherCodes.Count > 0)
                    {
                        combinedReason += " | also: " + string.Join(", ", otherCodes);
                    }
                }

                var finalReason = TrackerImportFieldLimiter.LimitDispositionReason(combinedReason, context.Summary);

                var closedDate = targetDisposition == PostingDisposition.Closed
                    ? dateOutcome.ResolvedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null;

                var rawSource = Decode(row["source"])?.Trim();
                var sourceKind = TrackingEnumExtensions.TryParseSourceKind(rawSource, out var parsedSrc) ? parsedSrc : SourceKind.Aggregator;

                var rowResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
                {
                    Company? company = null;
                    if (!context.CanonicalCompanies.TryGetValue(canonicalCompany, out company))
                    {
                        var existing = await txCtx.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalCompany, ct);
                        if (existing.IsSuccess && existing.Value is not null)
                        {
                            company = existing.Value;
                        }
                        else
                        {
                            company = Company.Create(
                                ownerId: context.OwnerId,
                                name: canonicalCompany,
                                status: CompanyStatus.Neutral,
                                description: string.Empty);

                            var saveCompany = await txCtx.Companies.UpsertCompanyAsync(company, ct);
                            if (!saveCompany.IsSuccess)
                            {
                                return Result<bool>.Failure(saveCompany.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to save company"));
                            }

                            var audit = AuditLogEntry.CreateForTracking(
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
                            await txCtx.AppendAuditEntryAsync(audit, ct);
                            autoCreatedCompany = true;
                            newCompany = company;
                        }
                    }

                    // 3. Resolve or create criterion from reason_primary
                    Guid? criterionId = null;
                    var criterionReason = !string.IsNullOrWhiteSpace(rawReasonPrimary)
                        ? rawReasonPrimary
                        : (normalizedDisp == "dismissed" ? "dismissed" : (isDuplicateRow ? "duplicate" : null));

                    if (!string.IsNullOrWhiteSpace(criterionReason))
                    {
                        var critResult = await _criteriaStep.GetOrCreateReasonCriterionAsync(
                            context.OwnerId, criterionReason, effectiveDate, context.Summary, ct);
                        if (critResult.IsSuccess && critResult.Value is not null)
                        {
                            criterionId = critResult.Value.Id;
                        }
                    }

                    // Duplicate link resolution
                    Guid? duplicateOfId = null;
                    if (isDuplicateRow)
                    {
                        var lookupKey = $"{canonicalCompany}|{TitleNormalizer.Normalize(title)}";
                        if (context.PostingsByCompanyAndTitle.TryGetValue(lookupKey, out var targetPosting))
                        {
                            duplicateOfId = targetPosting.Id;
                        }
                        else if (company is not null)
                        {
                            var candRes = await txCtx.Postings.FindCandidatesByCompanyAndTitleAsync(context.OwnerId, company.Id, TitleNormalizer.Normalize(title), ct);
                            if (candRes.IsSuccess && candRes.Value is { Count: > 0 } candidates)
                            {
                                duplicateOfId = candidates[0].Id;
                            }
                        }
                    }

                    // Match against existing posting in context or repository
                    Posting? existingPosting = null;
                    if (!string.IsNullOrWhiteSpace(reqId))
                    {
                        if (context.PostingsByCompanyAndReq.TryGetValue($"{canonicalCompany}|{reqId}", out var byReq))
                        {
                            existingPosting = byReq;
                        }
                        else if (company is not null)
                        {
                            var dedupC = $"{company.Id:D}|{reqId.Trim()}";
                            var byCRes = await txCtx.Postings.GetByDedupKeyCAsync(context.OwnerId, dedupC, ct);
                            if (byCRes.IsSuccess && byCRes.Value is not null)
                            {
                                existingPosting = byCRes.Value;
                            }
                        }
                    }

                    // If not matched by req, match by company + title ONLY IF candidate has no requisitionId (or same req)
                    if (existingPosting is null && !isDuplicateRow)
                    {
                        var titleKey = $"{canonicalCompany}|{TitleNormalizer.Normalize(title)}";
                        if (context.PostingsByCompanyAndTitle.TryGetValue(titleKey, out var candByTitle))
                        {
                            if (string.IsNullOrWhiteSpace(candByTitle.RequisitionId) || string.Equals(candByTitle.RequisitionId, reqId, StringComparison.OrdinalIgnoreCase))
                            {
                                existingPosting = candByTitle;
                            }
                        }
                        else if (company is not null)
                        {
                            var candRes = await txCtx.Postings.FindCandidatesByCompanyAndTitleAsync(context.OwnerId, company.Id, TitleNormalizer.Normalize(title), ct);
                            if (candRes.IsSuccess && candRes.Value is { Count: > 0 } candidates)
                            {
                                foreach (var c in candidates)
                                {
                                    var byIdRes = await txCtx.Postings.GetByIdAsync(context.OwnerId, c.Id, ct);
                                    if (byIdRes.IsSuccess && byIdRes.Value is not null)
                                    {
                                        if (string.IsNullOrWhiteSpace(byIdRes.Value.RequisitionId) || string.Equals(byIdRes.Value.RequisitionId, reqId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            existingPosting = byIdRes.Value;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Resurface policy
                    var proposedResurface = Decode(row["proposed_resurface"])?.Trim();
                    ResurfacePolicy resurfacePolicy;
                    if (string.IsNullOrWhiteSpace(proposedResurface))
                    {
                        resurfacePolicy = existingPosting?.ResurfacePolicy ?? ResurfacePolicy.Never;
                    }
                    else
                    {
                        var s = proposedResurface.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
                        if (s is "if_reopened" or "ifreopened")
                        {
                            resurfacePolicy = ResurfacePolicy.IfReopened;
                        }
                        else if (s is "never")
                        {
                            resurfacePolicy = ResurfacePolicy.Never;
                        }
                        else
                        {
                            context.Summary.RecordUnmappedValue("proposed_resurface", proposedResurface);
                            resurfacePolicy = existingPosting?.ResurfacePolicy ?? ResurfacePolicy.Never;
                        }
                    }

                    if (existingPosting is not null)
                    {
                        isUnchanged = true;
                        updatedPosting = existingPosting;
                        return Result<bool>.Success(true);
                    }

                    // Create new posting directly with target disposition
                    var newPosting = Posting.Create(
                        ownerId: context.OwnerId,
                        companyId: company?.Id ?? Guid.NewGuid(),
                        title: title,
                        sourceKind: sourceKind,
                        originalUrl: null,
                        requisitionId: reqId,
                        remoteType: remoteType,
                        compensation: compensation,
                        cultureSummary: cultureSummary,
                        disposition: targetDisposition,
                        dispositionReason: finalReason,
                        dispositionCriterionId: criterionId,
                        resurfacePolicy: resurfacePolicy,
                        decidedAt: targetDisposition is PostingDisposition.ScreenedOut or PostingDisposition.Closed ? effectiveDate : null,
                        duplicateOfId: duplicateOfId,
                        now: effectiveDate,
                        closedDate: closedDate);

                    var saveNew = await txCtx.Postings.UpsertPostingAsync(newPosting, ct);
                    if (!saveNew.IsSuccess)
                    {
                        return Result<bool>.Failure(saveNew.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to create posting"));
                    }

                    var newChangedFields = new List<string> { "title", "company_id", "disposition" };
                    var newAfterDetails = new Dictionary<string, object?>
                    {
                        ["title"] = newPosting.Title,
                        ["disposition"] = newPosting.Disposition.ToString()
                    };
                    if (closedDate is not null)
                    {
                        newChangedFields.Add("closed_date");
                        newAfterDetails["closed_date"] = closedDate;
                    }

                    var auditNew = AuditLogEntry.CreateForTracking(
                        ownerId: context.OwnerId,
                        actor: ImportActor,
                        operation: AuditOperation.ImportPostingCreate,
                        entityType: "posting",
                        entityId: newPosting.Id,
                        resultingVersionToken: newPosting.VersionToken,
                        occurredAtUtc: DateTime.UtcNow,
                        details: new AuditDetails(
                            ChangedFields: newChangedFields,
                            After: newAfterDetails));
                    await txCtx.AppendAuditEntryAsync(auditNew, ct);
                    createdPosting = newPosting;
                    return Result<bool>.Success(true);
                }, cancellationToken);

                if (!rowResult.IsSuccess)
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "appendix-rows.csv",
                        row.LineNumber,
                        rowResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                        rowResult.Error?.Message ?? "Failed to save appendix row"));
                    recon.RecordErrored();
                    continue;
                }

                if (autoCreatedCompany && newCompany is not null)
                {
                    context.CanonicalCompanies[canonicalCompany] = newCompany;
                    context.Summary.CompaniesCreated++;
                    context.Summary.AutoCreatedCompanies.Add(canonicalCompany);
                }

                var affectedPosting = updatedPosting ?? createdPosting;
                if (affectedPosting is not null)
                {
                    context.IndexPosting(affectedPosting, canonicalCompany);
                }

                if (isUnchanged && updatedPosting is not null)
                {
                    context.Summary.PostingsUnchanged++;
                    recon.RecordUnchanged("merged");
                }
                else if (updatedPosting is not null)
                {
                    context.Summary.PostingsUpdated++;
                    recon.RecordUpdated();
                }
                else if (createdPosting is not null)
                {
                    context.Summary.PostingsCreated++;
                    recon.RecordCreated();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "appendix-rows.csv",
                    row.LineNumber,
                    ErrorCode.ValidationError.ToString(),
                    ex.Message));
                recon.RecordErrored();
            }
        }

        return Result<bool>.Success(true);
    }

    private static string? Decode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : WebUtility.HtmlDecode(value);
}
