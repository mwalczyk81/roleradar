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
        if (!File.Exists(filePath))
        {
            return Result<bool>.Success(true);
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Result<bool>.Success(true);
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Result<bool>.Success(true);
        }

        var defaultDate = context.DefaultDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var title = element.TryGetProperty("title", out var titleProp) ? titleProp.GetString()?.Trim() : null;
            var companyName = element.TryGetProperty("company", out var compProp) ? compProp.GetString()?.Trim() : null;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(companyName))
            {
                continue;
            }

            var canonicalName = context.CompanyAliases.TryGetValue(companyName, out var alias) ? alias : companyName;

            // 2. Parse Culture and CultureLevel
            string? cultureSummary = null;
            if (element.TryGetProperty("culture", out var cultProp) && cultProp.GetString() is { } cultStr)
            {
                if (cultStr.Length > 500)
                {
                    cultureSummary = cultStr[..500];
                    context.Summary.CultureTruncations++;
                }
                else
                {
                    cultureSummary = cultStr;
                }
            }

            var cultureLevel = CultureLevel.Unknown;
            if (element.TryGetProperty("cultureLevel", out var cultLvlProp) && cultLvlProp.GetString() is { } cultLvlStr)
            {
                cultureLevel = cultLvlStr.Trim().ToLowerInvariant() switch
                {
                    "stable" => CultureLevel.Stable,
                    "moderate" => CultureLevel.Moderate,
                    "high_risk" => CultureLevel.HighRisk,
                    "unknown" => CultureLevel.Unknown,
                    _ => FallbackCultureLevel(context)
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

            var compText = element.TryGetProperty("comp", out var cProp) ? cProp.GetString() : null;
            var compHigh = element.TryGetProperty("compHigh", out var chProp) && chProp.GetBoolean();
            var reqId = element.TryGetProperty("req_id", out var reqProp) ? reqProp.GetString()?.Trim() : null;
            var url = element.TryGetProperty("url", out var urlProp) ? urlProp.GetString()?.Trim() : null;

            // Postings with status "applied" are imported with disposition "scored"
            var rawStatus = element.TryGetProperty("status", out var stProp) ? stProp.GetString()?.Trim() : null;
            var originalWasApplied = string.Equals(rawStatus, "applied", StringComparison.OrdinalIgnoreCase);

            // 1. Resolve or auto-create company, and 2. Save posting atomically inside one transaction per row
            var autoCreatedCompany = false;
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
                        var description = parentText ?? string.Empty;

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
                    return Result<bool>.Success(true);
                }

                var posting = Posting.Create(
                    ownerId: context.OwnerId,
                    companyId: resolvedCompany?.Id ?? Guid.NewGuid(),
                    title: title,
                    sourceKind: SourceKind.Aggregator,
                    originalUrl: url,
                    requisitionId: reqId,
                    remoteType: RemoteType.Unknown,
                    compensation: compText != null ? new Compensation(Text: compText) : null,
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

                var auditP = AuditLogEntry.CreateForTracking(
                    ownerId: context.OwnerId,
                    actor: ImportActor,
                    operation: AuditOperation.ImportPostingCreate,
                    entityType: "posting",
                    entityId: posting.Id,
                    resultingVersionToken: posting.VersionToken,
                    occurredAtUtc: DateTime.UtcNow,
                    details: new AuditDetails(
                        ChangedFields: ["title", "company_id", "disposition"],
                        After: new Dictionary<string, object?>
                        {
                            ["title"] = posting.Title,
                            ["disposition"] = posting.Disposition.ToString()
                        }));
                await txCtx.AppendAuditEntryAsync(auditP, ct);

                newPosting = posting;
                return Result<bool>.Success(true);
            }, cancellationToken);

            if (!rowResult.IsSuccess)
            {
                context.Summary.Errors.Add(new TrackerImportRowError("postings.json", 0, rowResult.Error?.Message ?? "Failed to save posting"));
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
                if (newPosting.VersionToken != Guid.Empty && !context.PostingsByCompanyAndTitle.ContainsKey($"{canonicalName}|{newPosting.NormalizedTitle}"))
                {
                    if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = newPosting;
                    if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = newPosting;
                    context.PostingsByCompanyAndTitle[$"{canonicalName}|{newPosting.NormalizedTitle}"] = newPosting;
                    context.Summary.PostingsCreated++;

                    if (originalWasApplied)
                    {
                        context.Summary.UnclaimedAppliedPostingTitles.Add(newPosting.Title);
                        context.Summary.UnclaimedAppliedPostings++;
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = newPosting;
                    if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = newPosting;
                    context.PostingsByCompanyAndTitle[$"{canonicalName}|{newPosting.NormalizedTitle}"] = newPosting;
                    context.Summary.PostingsUnchanged++;
                }
            }
        }

        return Result<bool>.Success(true);
    }

    private static CultureLevel FallbackCultureLevel(TrackerImportContext context)
    {
        context.Summary.CultureUnmapped++;
        return CultureLevel.Unknown;
    }
}
