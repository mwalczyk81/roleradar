using System.Text.Json;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Import.Pipeline;

public sealed class PostingsJsonStep : ITrackerImportStep
{
    private const string ImportActor = "cli-import-tracker";
    private readonly ITrackingTransactionContext _context;
    private readonly CriteriaSeedingStep _criteriaStep;

    public PostingsJsonStep(ITrackingTransactionContext context, CriteriaSeedingStep criteriaStep)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _criteriaStep = criteriaStep ?? throw new ArgumentNullException(nameof(criteriaStep));
    }

    public async Task<Result<bool>> ExecuteAsync(TrackerImportContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var filePath = Path.Combine(context.BundleDirectory, "postings.json");
        var recon = context.Summary.GetOrCreateFileReconciliation("postings.json");

        if (!File.Exists(filePath))
        {
            recon.IsAbsent = true;
            return Result<bool>.Success(true);
        }

        recon.IsAbsent = false;

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "postings.json",
                1,
                ErrorCode.ValidationError.ToString(),
                "File is present but empty."));
            return Result<bool>.Failure(ErrorCode.ValidationError, "postings.json is present but empty.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            recon.RecordErrored();
            context.Summary.Errors.Add(new TrackerImportRowError(
                "postings.json",
                1,
                ErrorCode.ValidationError.ToString(),
                "Malformed JSON: " + ex.Message));
            return Result<bool>.Failure(ErrorCode.ValidationError, "postings.json contains malformed JSON.");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object && doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                recon.RecordErrored();
                context.Summary.Errors.Add(new TrackerImportRowError(
                    "postings.json",
                    1,
                    ErrorCode.ValidationError.ToString(),
                    "Root must be a JSON object keyed by slug (or JSON array)."));
                return Result<bool>.Failure(ErrorCode.ValidationError, "postings.json root must be a JSON object keyed by slug (or JSON array).");
            }

            IEnumerable<(string? Slug, JsonElement Element)> entries;
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                entries = doc.RootElement.EnumerateObject().Select(p => ((string?)p.Name, p.Value));
            }
            else
            {
                entries = doc.RootElement.EnumerateArray().Select(e =>
                {
                    string? slug = e.TryGetProperty("slug", out var sProp) ? sProp.GetString() : null;
                    return (slug, e);
                });
            }

            var defaultDate = context.DefaultDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var entryOrdinal = 0;

            foreach (var (slug, element) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entryOrdinal++;
                recon.RecordRead();

                if (element.ValueKind != JsonValueKind.Object)
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "postings.json",
                        entryOrdinal,
                        ErrorCode.ValidationError.ToString(),
                        "Posting entry must be a JSON object"));
                    recon.RecordErrored();
                    continue;
                }

                try
                {
                    var rawTitle = element.TryGetProperty("title", out var titleProp) ? titleProp.GetString()?.Trim() : null;
                    var rawCompanyName = element.TryGetProperty("company", out var compProp) ? compProp.GetString()?.Trim() : null;

                    if (string.IsNullOrWhiteSpace(rawTitle) || string.IsNullOrWhiteSpace(rawCompanyName))
                    {
                        context.Summary.Errors.Add(new TrackerImportRowError(
                            "postings.json",
                            entryOrdinal,
                            ErrorCode.ValidationError.ToString(),
                            "Missing required title or company"));
                        recon.RecordErrored();
                        continue;
                    }

                    var title = TrackerImportFieldLimiter.LimitTitle(rawTitle, context.Summary)!;
                    var canonicalName = context.CompanyAliases.TryGetValue(rawCompanyName, out var alias) ? alias : rawCompanyName;
                    canonicalName = TrackerImportFieldLimiter.LimitCompanyName(canonicalName, context.Summary)!;

                    // 2. Parse Culture and CultureLevel
                    string? cultureSummary = null;
                    if (element.TryGetProperty("culture", out var cultProp) && cultProp.GetString() is { } cultStr)
                    {
                        cultureSummary = TrackerImportFieldLimiter.LimitCultureSummary(cultStr, context.Summary);
                    }

                    var cultureLevel = CultureLevel.Unknown;
                    if (element.TryGetProperty("cultureLevel", out var cultLvlProp) && cultLvlProp.GetString() is { } cultLvlStr)
                    {
                        var s = cultLvlStr.Trim().ToLowerInvariant();
                        cultureLevel = s switch
                        {
                            "stable" => CultureLevel.Stable,
                            "moderate" => CultureLevel.Moderate,
                            "high_risk" or "highrisk" => CultureLevel.HighRisk,
                            "unknown" => CultureLevel.Unknown,
                            _ => FallbackCultureLevel(context, cultLvlStr)
                        };
                    }

                    var remoteType = RemoteType.Unknown;
                    if (element.TryGetProperty("remoteType", out var remProp) && remProp.GetString() is { } remStr)
                    {
                        var s = remStr.Trim().ToLowerInvariant();
                        remoteType = s switch
                        {
                            "remote" => RemoteType.Remote,
                            "hybrid" => RemoteType.Hybrid,
                            "onsite" => RemoteType.Onsite,
                            "unknown" => RemoteType.Unknown,
                            _ => FallbackRemoteType(context, remStr)
                        };
                    }

                    // 3. Parse Score (flag and store empty if out of range, do not clamp)
                    double? score = null;
                    if (element.TryGetProperty("score", out var scoreProp) && scoreProp.TryGetDouble(out var rawScore))
                    {
                        if (rawScore is < 0.0 or > 10.0)
                        {
                            score = null;
                            context.Summary.DatesFlagged++; // Track out of range score flag
                        }
                        else
                        {
                            score = rawScore;
                        }
                    }

                    // 4. Parse Dates
                    var addedRaw = element.TryGetProperty("added", out var addedProp)
                        ? addedProp.GetString()
                        : (element.TryGetProperty("first", out var firstProp) ? firstProp.GetString() : null);

                    var dateOutcome = DateInferenceRule.Resolve(addedRaw, defaultDate);
                    if (dateOutcome.Kind == DateResolutionKind.DefaultedBlank) context.Summary.DatesDefaulted++;
                    if (dateOutcome.Kind is DateResolutionKind.InferredRollback or DateResolutionKind.InferredSameYear) context.Summary.DatesInferred++;
                    if (dateOutcome.IsFlagged) context.Summary.DatesFlagged++;

                    var rawCompText = element.TryGetProperty("comp", out var cProp) ? cProp.GetString() : null;
                    var compText = TrackerImportFieldLimiter.LimitCompensationText(rawCompText, context.Summary);

                    bool? compHigh = null;
                    if (element.TryGetProperty("compHigh", out var chProp) &&
                        chProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        compHigh = chProp.GetBoolean();
                    }

                    var rawReqId = element.TryGetProperty("req_id", out var reqProp) ? reqProp.GetString()?.Trim()
                        : (element.TryGetProperty("requisitionId", out var reqProp2) ? reqProp2.GetString()?.Trim() : null);
                    var reqId = TrackerImportFieldLimiter.LimitRequisitionId(rawReqId, context.Summary);

                    var rawUrl = element.TryGetProperty("href", out var hrefProp) ? hrefProp.GetString()?.Trim()
                        : (element.TryGetProperty("url", out var urlProp) ? urlProp.GetString()?.Trim() : null);
                    var url = TrackerImportFieldLimiter.LimitUrl(rawUrl, context.Summary);

                    var rawLoc = element.TryGetProperty("loc", out var locProp) ? locProp.GetString()?.Trim()
                        : (element.TryGetProperty("location", out var locProp2) ? locProp2.GetString()?.Trim() : null);
                    var location = TrackerImportFieldLimiter.LimitLocation(rawLoc, context.Summary);

                    var rawPosted = element.TryGetProperty("posted", out var pProp) ? pProp.GetString()?.Trim() : null;

                    var isGhost = element.TryGetProperty("ghost", out var ghostProp) &&
                                  ghostProp.ValueKind == JsonValueKind.True &&
                                  ghostProp.GetBoolean();

                    // Postings with status "applied" are imported with disposition "scored"
                    var rawStatus = element.TryGetProperty("status", out var stProp) ? stProp.GetString()?.Trim() : null;
                    var originalWasApplied = string.Equals(rawStatus, "applied", StringComparison.OrdinalIgnoreCase);

                    // 1. Resolve or auto-create company, and 2. Save posting atomically inside one transaction per row
                    var autoCreatedCompany = false;
                    var isNewlyCreated = false;
                    Company? newCompany = null;
                    Posting? newPosting = null;

                    var rowResult = await _context.ExecuteInTransactionAsync(async (txCtx, ct) =>
                    {
                        Company? resolvedCompany = null;
                        if (!context.CanonicalCompanies.TryGetValue(canonicalName, out resolvedCompany))
                        {
                            var existing = await txCtx.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalName, ct);
                            if (existing.IsSuccess && existing.Value is not null)
                            {
                                resolvedCompany = existing.Value;
                            }
                            else
                            {
                                var parentText = element.TryGetProperty("parent", out var parentProp) ? parentProp.GetString()?.Trim() : null;
                                var rawDescription = parentText ?? string.Empty;
                                var description = TrackerImportFieldLimiter.LimitCompanyDescription(rawDescription, context.Summary) ?? string.Empty;

                                resolvedCompany = Company.Create(
                                    ownerId: context.OwnerId,
                                    name: canonicalName,
                                    status: CompanyStatus.Neutral,
                                    description: description);

                                var saveCompany = await txCtx.Companies.UpsertCompanyAsync(resolvedCompany, ct);
                                if (!saveCompany.IsSuccess)
                                {
                                    return Result<bool>.Failure(saveCompany.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to save company"));
                                }

                                var audit = AuditLogEntry.CreateForTracking(
                                    ownerId: context.OwnerId,
                                    actor: ImportActor,
                                    operation: AuditOperation.ImportCompanyCreate,
                                    entityType: "company",
                                    entityId: resolvedCompany.Id,
                                    resultingVersionToken: resolvedCompany.VersionToken,
                                    occurredAtUtc: DateTime.UtcNow,
                                    details: new AuditDetails(
                                        ChangedFields: ["name", "status", "description"],
                                        After: new Dictionary<string, object?>
                                        {
                                            ["name"] = resolvedCompany.Name,
                                            ["status"] = resolvedCompany.Status.ToString()
                                        }));
                                await txCtx.AppendAuditEntryAsync(audit, ct);
                                autoCreatedCompany = true;
                                newCompany = resolvedCompany;
                            }
                        }

                        // Check existing posting
                        Posting? existingPosting = null;
                        if (!string.IsNullOrWhiteSpace(url))
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

                        if (existingPosting is null && !string.IsNullOrWhiteSpace(reqId) && resolvedCompany is not null)
                        {
                            var dedupC = $"{resolvedCompany.Id:D}|{reqId.Trim()}";
                            var byCRes = await txCtx.Postings.GetByDedupKeyCAsync(context.OwnerId, dedupC, ct);
                            if (byCRes.IsSuccess && byCRes.Value is not null)
                            {
                                existingPosting = byCRes.Value;
                            }
                        }

                        if (existingPosting is null && resolvedCompany is not null)
                        {
                            var candRes = await txCtx.Postings.FindCandidatesByCompanyAndTitleAsync(context.OwnerId, resolvedCompany.Id, TitleNormalizer.Normalize(title), ct);
                            if (candRes.IsSuccess && candRes.Value is { Count: > 0 } candidates)
                            {
                                var byIdRes = await txCtx.Postings.GetByIdAsync(context.OwnerId, candidates[0].Id, ct);
                                if (byIdRes.IsSuccess && byIdRes.Value is not null)
                                {
                                    existingPosting = byIdRes.Value;
                                }
                            }
                        }

                        if (existingPosting is not null)
                        {
                            newPosting = existingPosting;
                            isNewlyCreated = false;
                            return Result<bool>.Success(true);
                        }

                        var safeSlug = !string.IsNullOrWhiteSpace(slug)
                            ? (slug.Length > Posting.MaxAtsTenantLength ? slug[..Posting.MaxAtsTenantLength] : slug)
                            : null;

                        var posting = Posting.Create(
                            ownerId: context.OwnerId,
                            companyId: resolvedCompany?.Id ?? Guid.NewGuid(),
                            title: title,
                            sourceKind: SourceKind.Aggregator,
                            originalUrl: url,
                            atsTenant: safeSlug,
                            requisitionId: reqId,
                            location: location,
                            remoteType: remoteType,
                            compensation: compText != null ? new Compensation(Text: compText) : null,
                            postedDate: rawPosted,
                            score: (decimal?)score,
                            cultureLevel: cultureLevel,
                            cultureSummary: cultureSummary,
                            disposition: PostingDisposition.Scored,
                            now: dateOutcome.ResolvedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                            compHighPriority: compHigh);

                        var savePosting = await txCtx.Postings.UpsertPostingAsync(posting, ct);
                        if (!savePosting.IsSuccess)
                        {
                            return Result<bool>.Failure(savePosting.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to save posting"));
                        }

                        var changedFields = new List<string> { "title", "company_id", "disposition" };
                        var afterDetails = new Dictionary<string, object?>
                        {
                            ["title"] = posting.Title,
                            ["disposition"] = posting.Disposition.ToString()
                        };
                        if (!string.IsNullOrWhiteSpace(slug))
                        {
                            changedFields.Add("slug");
                            afterDetails["slug"] = slug;
                        }

                        var auditP = AuditLogEntry.CreateForTracking(
                            ownerId: context.OwnerId,
                            actor: ImportActor,
                            operation: AuditOperation.ImportPostingCreate,
                            entityType: "posting",
                            entityId: posting.Id,
                            resultingVersionToken: posting.VersionToken,
                            occurredAtUtc: DateTime.UtcNow,
                            details: new AuditDetails(
                                ChangedFields: changedFields,
                                After: afterDetails));
                        await txCtx.AppendAuditEntryAsync(auditP, ct);

                        if (isGhost)
                        {
                            var signalEntity = new PostingSignal(
                                id: Guid.CreateVersion7(),
                                ownerId: context.OwnerId,
                                postingId: posting.Id,
                                signalType: SignalType.Phantom,
                                evidence: "Imported ghost flag",
                                observedAt: dateOutcome.ResolvedDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
                            await txCtx.Postings.AppendSignalAsync(signalEntity, ct);

                            var signalAudit = AuditLogEntry.CreateForTracking(
                                ownerId: context.OwnerId,
                                actor: ImportActor,
                                operation: AuditOperation.ImportPostingSignalAppend,
                                entityType: "posting",
                                entityId: posting.Id,
                                resultingVersionToken: posting.VersionToken,
                                occurredAtUtc: DateTime.UtcNow,
                                details: new AuditDetails(
                                    ChangedFields: ["signals"],
                                    After: new Dictionary<string, object?>
                                    {
                                        ["signal_type"] = SignalType.Phantom.ToString()
                                    }));
                            await txCtx.AppendAuditEntryAsync(signalAudit, ct);
                        }

                        newPosting = posting;
                        isNewlyCreated = true;
                        return Result<bool>.Success(true);
                    }, cancellationToken);

                    if (!rowResult.IsSuccess)
                    {
                        context.Summary.Errors.Add(new TrackerImportRowError(
                            "postings.json",
                            entryOrdinal,
                            rowResult.Error?.Code.ToString() ?? ErrorCode.ValidationError.ToString(),
                            rowResult.Error?.Message ?? "Failed to save posting"));
                        recon.RecordErrored();
                        continue;
                    }

                    if (autoCreatedCompany && newCompany is not null)
                    {
                        context.CanonicalCompanies[canonicalName] = newCompany;
                        context.Summary.CompaniesCreated++;
                        context.Summary.AutoCreatedCompanies.Add(canonicalName);
                    }

                    if (newPosting is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = newPosting;
                        if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = newPosting;
                        context.PostingsByCompanyAndTitle[$"{canonicalName}|{newPosting.NormalizedTitle}"] = newPosting;

                        if (isNewlyCreated)
                        {
                            context.Summary.PostingsCreated++;
                            recon.RecordCreated();

                            if (originalWasApplied)
                            {
                                context.Summary.UnclaimedAppliedPostingTitles.Add(newPosting.Title);
                                context.Summary.UnclaimedAppliedPostings++;
                            }
                        }
                        else
                        {
                            context.Summary.PostingsUnchanged++;
                            recon.RecordUnchanged();
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.Summary.Errors.Add(new TrackerImportRowError(
                        "postings.json",
                        entryOrdinal,
                        ErrorCode.ValidationError.ToString(),
                        ex.Message));
                    recon.RecordErrored();
                }
            }
        }

        return Result<bool>.Success(true);
    }

    private static CultureLevel FallbackCultureLevel(TrackerImportContext context, string rawValue)
    {
        context.Summary.RecordUnmappedValue("culture_level", rawValue);
        return CultureLevel.Unknown;
    }

    private static RemoteType FallbackRemoteType(TrackerImportContext context, string rawValue)
    {
        context.Summary.RecordUnmappedValue("remote_type", rawValue);
        return RemoteType.Unknown;
    }
}
