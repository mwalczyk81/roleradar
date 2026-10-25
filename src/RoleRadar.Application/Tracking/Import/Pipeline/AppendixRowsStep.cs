using System.Globalization;
using System.Net;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class AppendixRowsStep : ITrackerImportStep
{
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
            var reasonPrimary = Decode(row["reason_primary"])?.Trim();
            var reasonAll = Decode(row["reason_all"])?.Trim();
            var isDuplicate = string.Equals(rawProposedDisp, "duplicate", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(reasonPrimary, "duplicate", StringComparison.OrdinalIgnoreCase) ||
                              (reasonAll is not null && reasonAll.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

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

                var rawReqId = Decode(row["req_id"])?.Trim();
                var reqId = TrackerImportFieldLimiter.LimitRequisitionId(rawReqId, context.Summary);
                var rawUrl = Decode(row["url"])?.Trim();
                var url = TrackerImportFieldLimiter.LimitUrl(rawUrl, context.Summary);
                var rawReasonPrimary = Decode(row["reason_primary"])?.Trim();
                var reasonPrimary = TrackerImportFieldLimiter.LimitDispositionReason(rawReasonPrimary, context.Summary);
                var reasonAll = Decode(row["reason_all"])?.Trim();
                var rawProposedDisp = (Decode(row["proposed_disposition"]) ?? Decode(row["disposition"]))?.Trim();

                var isDuplicateRow = string.Equals(rawProposedDisp, "duplicate", StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(reasonPrimary, "duplicate", StringComparison.OrdinalIgnoreCase) ||
                                     (reasonAll is not null && reasonAll.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

                PostingDisposition targetDisposition;
                string? finalReason;
                var normalizedDisp = rawProposedDisp?.ToLowerInvariant().Replace("-", "_").Replace(" ", "_");

                if (isDuplicateRow)
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                    finalReason = reasonPrimary ?? "duplicate";
                }
                else if (normalizedDisp is "screened_out" or "screenedout")
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                    finalReason = reasonPrimary ?? "screened_out";
                }
                else if (normalizedDisp is "closed")
                {
                    targetDisposition = PostingDisposition.Closed;
                    finalReason = reasonPrimary ?? "closed";
                }
                else if (normalizedDisp is "unscored")
                {
                    targetDisposition = PostingDisposition.Unscored;
                    finalReason = reasonPrimary;
                }
                else if (normalizedDisp is "dismissed")
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                    finalReason = "dismissed";
                }
                else if (string.IsNullOrWhiteSpace(normalizedDisp))
                {
                    targetDisposition = PostingDisposition.ScreenedOut;
                    finalReason = reasonPrimary ?? "screened_out";
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

                    // 3. Resolve or create criterion from finalReason
                    Guid? criterionId = null;
                    if (!string.IsNullOrWhiteSpace(finalReason))
                    {
                        var critResult = await _criteriaStep.GetOrCreateReasonCriterionAsync(
                            context.OwnerId, finalReason, effectiveDate, context.Summary, ct);
                        if (critResult.IsSuccess && critResult.Value is not null)
                        {
                            criterionId = critResult.Value.Id;
                        }
                    }

                    var remoteRaw = (Decode(row["remote_mentioned"]) ?? Decode(row["remote_type"]))?.Trim();
                    var remoteType = RemoteType.Unknown;
                    if (!string.IsNullOrWhiteSpace(remoteRaw))
                    {
                        var s = remoteRaw.Trim().ToLowerInvariant();
                        if (s is "true" or "yes" or "1" or "remote")
                        {
                            remoteType = RemoteType.Remote;
                        }
                        else if (s is "false" or "no" or "0" or "none" or "unknown")
                        {
                            remoteType = RemoteType.Unknown;
                        }
                        else if (s is "hybrid")
                        {
                            remoteType = RemoteType.Hybrid;
                        }
                        else if (s is "onsite" or "in_office" or "inoffice")
                        {
                            remoteType = RemoteType.Onsite;
                        }
                        else
                        {
                            context.Summary.RecordUnmappedValue("remote_type", remoteRaw);
                            remoteType = RemoteType.Unknown;
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
                    if (!string.IsNullOrWhiteSpace(reqId) && context.PostingsByReqOrUrl.TryGetValue(reqId, out var byReq))
                    {
                        existingPosting = byReq;
                    }
                    else if (!string.IsNullOrWhiteSpace(url) && context.PostingsByReqOrUrl.TryGetValue(url, out var byUrl))
                    {
                        existingPosting = byUrl;
                    }
                    else if (!isDuplicateRow)
                    {
                        var titleKey = $"{canonicalCompany}|{TitleNormalizer.Normalize(title)}";
                        context.PostingsByCompanyAndTitle.TryGetValue(titleKey, out existingPosting);
                    }

                    if (existingPosting is null && !string.IsNullOrWhiteSpace(url))
                    {
                        var normUrl = UrlNormalizer.Normalize(url);
                        if (normUrl is not null)
                        {
                            var byUrlRes = await txCtx.Postings.GetByDedupKeyBAsync(context.OwnerId, normUrl, ct);
                            if (byUrlRes.IsSuccess && byUrlRes.Value is not null)
                            {
                                existingPosting = byUrlRes.Value;
                            }
                        }
                    }

                    if (existingPosting is null && !string.IsNullOrWhiteSpace(reqId) && company is not null)
                    {
                        var dedupC = $"{company.Id:D}|{reqId.Trim()}";
                        var byCRes = await txCtx.Postings.GetByDedupKeyCAsync(context.OwnerId, dedupC, ct);
                        if (byCRes.IsSuccess && byCRes.Value is not null)
                        {
                            existingPosting = byCRes.Value;
                        }
                    }

                    if (existingPosting is null && !isDuplicateRow && company is not null)
                    {
                        var candRes = await txCtx.Postings.FindCandidatesByCompanyAndTitleAsync(context.OwnerId, company.Id, TitleNormalizer.Normalize(title), ct);
                        if (candRes.IsSuccess && candRes.Value is { Count: > 0 } candidates)
                        {
                            var byIdRes = await txCtx.Postings.GetByIdAsync(context.OwnerId, candidates[0].Id, ct);
                            if (byIdRes.IsSuccess && byIdRes.Value is not null)
                            {
                                existingPosting = byIdRes.Value;
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
                        if (existingPosting.Disposition == targetDisposition &&
                            string.Equals(existingPosting.DispositionReason, finalReason, StringComparison.OrdinalIgnoreCase) &&
                            existingPosting.ResurfacePolicy == resurfacePolicy &&
                            string.Equals(existingPosting.ClosedDate, closedDate, StringComparison.OrdinalIgnoreCase))
                        {
                            isUnchanged = true;
                            updatedPosting = existingPosting;
                            return Result<bool>.Success(true);
                        }

                        if (!DispositionMatrix.CanTransition(existingPosting.Disposition, targetDisposition, existingPosting.ResurfacePolicy))
                        {
                            return Result<bool>.Failure(
                                ErrorCode.ValidationError,
                                $"Cannot transition posting from {existingPosting.Disposition.ToSnakeCase()} to {targetDisposition.ToSnakeCase()} with policy {existingPosting.ResurfacePolicy.ToSnakeCase()}.");
                        }

                        existingPosting.Transition(
                            targetDisposition,
                            reason: finalReason,
                            criterionId: criterionId,
                            resurfacePolicy: resurfacePolicy,
                            timestamp: effectiveDate,
                            closedDate: closedDate);

                        var saveExisting = await txCtx.Postings.UpsertPostingAsync(existingPosting, ct);
                        if (!saveExisting.IsSuccess)
                        {
                            return Result<bool>.Failure(saveExisting.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to transition posting"));
                        }

                        var changedFields = new List<string> { "disposition", "disposition_reason" };
                        var afterDetails = new Dictionary<string, object?>
                        {
                            ["disposition"] = existingPosting.Disposition.ToString(),
                            ["reason"] = existingPosting.DispositionReason
                        };
                        if (criterionId.HasValue)
                        {
                            changedFields.Add("disposition_criterion_id");
                            afterDetails["disposition_criterion_id"] = criterionId.Value;
                        }
                        if (closedDate is not null)
                        {
                            changedFields.Add("closed_date");
                            afterDetails["closed_date"] = closedDate;
                        }

                        var audit = AuditLogEntry.CreateForTracking(
                            ownerId: context.OwnerId,
                            actor: ImportActor,
                            operation: AuditOperation.ImportPostingTransition,
                            entityType: "posting",
                            entityId: existingPosting.Id,
                            resultingVersionToken: existingPosting.VersionToken,
                            occurredAtUtc: DateTime.UtcNow,
                            details: new AuditDetails(
                                ChangedFields: changedFields,
                                After: afterDetails));
                        await txCtx.AppendAuditEntryAsync(audit, ct);
                        updatedPosting = existingPosting;
                        return Result<bool>.Success(true);
                    }

                    // Create new posting directly with target disposition
                    var newPosting = Posting.Create(
                        ownerId: context.OwnerId,
                        companyId: company?.Id ?? Guid.NewGuid(),
                        title: title,
                        sourceKind: sourceKind,
                        originalUrl: url,
                        requisitionId: reqId,
                        remoteType: remoteType,
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

                if (isUnchanged && updatedPosting is not null)
                {
                    if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = updatedPosting;
                    if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = updatedPosting;
                    context.PostingsByCompanyAndTitle[$"{canonicalCompany}|{updatedPosting.NormalizedTitle}"] = updatedPosting;
                    context.Summary.PostingsUnchanged++;
                    recon.RecordUnchanged("merged");
                }
                else if (updatedPosting is not null)
                {
                    if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = updatedPosting;
                    if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = updatedPosting;
                    context.PostingsByCompanyAndTitle[$"{canonicalCompany}|{updatedPosting.NormalizedTitle}"] = updatedPosting;
                    context.Summary.PostingsUpdated++;
                    recon.RecordUpdated();
                }
                else if (createdPosting is not null)
                {
                    if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = createdPosting;
                    if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = createdPosting;
                    context.PostingsByCompanyAndTitle[$"{canonicalCompany}|{createdPosting.NormalizedTitle}"] = createdPosting;
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
