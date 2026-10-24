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

            // 1. Resolve or auto-create company
            if (!context.CanonicalCompanies.TryGetValue(canonicalName, out var company))
            {
                var existing = await _context.Companies.GetByNormalizedNameAsync(context.OwnerId, canonicalName, cancellationToken);
                if (existing.IsSuccess && existing.Value is not null)
                {
                    company = existing.Value;
                    context.CanonicalCompanies[canonicalName] = company;
                }
                else
                {
                    var parentText = element.TryGetProperty("parent", out var parentProp) ? parentProp.GetString()?.Trim() : null;
                    var description = parentText ?? string.Empty;

                    company = Company.Create(
                        ownerId: context.OwnerId,
                        name: canonicalName,
                        status: CompanyStatus.Neutral,
                        description: description);

                    var saveCompany = await _context.Companies.UpsertCompanyAsync(company, cancellationToken);
                    if (saveCompany.IsSuccess)
                    {
                        var audit = AuditLogEntry.CreateForTracking(
                            ownerId: context.OwnerId,
                            actor: ImportActor,
                            operation: AuditOperation.ImportCompanyCreate,
                            entityType: "company",
                            entityId: company.Id,
                            resultingVersionToken: company.VersionToken,
                            occurredAtUtc: DateTime.UtcNow,
                            details: new AuditDetails(
                                ChangedFields: ["name", "status", "description"],
                                After: new Dictionary<string, object?>
                                {
                                    ["name"] = company.Name,
                                    ["status"] = company.Status.ToString()
                                }));
                        await _context.AppendAuditEntryAsync(audit, cancellationToken);
                        context.CanonicalCompanies[canonicalName] = company;
                        context.Summary.CompaniesCreated++;
                        context.Summary.AutoCreatedCompanies.Add(canonicalName);
                    }
                }
            }

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

            // Check if posting already exists in repository or context
            Posting? existingPosting = null;
            if (!string.IsNullOrWhiteSpace(url))
            {
                var normUrl = UrlNormalizer.Normalize(url);
                if (normUrl is not null)
                {
                    var byUrlRes = await _context.Postings.GetByDedupKeyBAsync(context.OwnerId, normUrl, cancellationToken);
                    if (byUrlRes.IsSuccess && byUrlRes.Value is not null)
                    {
                        existingPosting = byUrlRes.Value;
                    }
                }
            }

            if (existingPosting is null && !string.IsNullOrWhiteSpace(reqId) && company is not null)
            {
                var dedupC = $"{company.Id:D}|{reqId.Trim()}";
                var byCRes = await _context.Postings.GetByDedupKeyCAsync(context.OwnerId, dedupC, cancellationToken);
                if (byCRes.IsSuccess && byCRes.Value is not null)
                {
                    existingPosting = byCRes.Value;
                }
            }

            if (existingPosting is null && company is not null)
            {
                var candRes = await _context.Postings.FindCandidatesByCompanyAndTitleAsync(context.OwnerId, company.Id, TitleNormalizer.Normalize(title), cancellationToken);
                if (candRes.IsSuccess && candRes.Value is { Count: > 0 } candidates)
                {
                    var byIdRes = await _context.Postings.GetByIdAsync(context.OwnerId, candidates[0].Id, cancellationToken);
                    if (byIdRes.IsSuccess && byIdRes.Value is not null)
                    {
                        existingPosting = byIdRes.Value;
                    }
                }
            }

            if (existingPosting is not null)
            {
                if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = existingPosting;
                if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = existingPosting;
                context.PostingsByCompanyAndTitle[$"{canonicalName}|{existingPosting.NormalizedTitle}"] = existingPosting;
                context.Summary.PostingsUnchanged++;
                continue;
            }

            var posting = Posting.Create(
                ownerId: context.OwnerId,
                companyId: company?.Id ?? Guid.NewGuid(),
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

            var savePosting = await _context.Postings.UpsertPostingAsync(posting, cancellationToken);
            if (savePosting.IsSuccess)
            {
                var audit = AuditLogEntry.CreateForTracking(
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
                await _context.AppendAuditEntryAsync(audit, cancellationToken);

                context.Summary.PostingsCreated++;

                // Track in context lookups for later stages
                if (!string.IsNullOrWhiteSpace(reqId)) context.PostingsByReqOrUrl[reqId] = posting;
                if (!string.IsNullOrWhiteSpace(url)) context.PostingsByReqOrUrl[url] = posting;
                context.PostingsByCompanyAndTitle[$"{canonicalName}|{posting.NormalizedTitle}"] = posting;

                if (originalWasApplied)
                {
                    context.Summary.UnclaimedAppliedPostingTitles.Add(posting.Title);
                    context.Summary.UnclaimedAppliedPostings++;
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
