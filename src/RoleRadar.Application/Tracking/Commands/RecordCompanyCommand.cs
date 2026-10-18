using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record CareerSourceInputDto(
    Guid? Id = null,
    string AtsType = "",
    string? Tenant = null,
    string? Slug = null,
    string FetchMethod = "api",
    string? Notes = null,
    bool? IsActive = null,
    Guid? VersionToken = null);

public sealed record CompanyRecordSummaryDto(
    Guid Id,
    string Name,
    string? Description,
    string OwnershipType,
    string Status,
    Guid VersionToken,
    int CareerSourceCount);

public sealed record CompanyRecordResultDto(
    CompanyRecordSummaryDto Company,
    string Status = "created");

public sealed record RecordCompanyCommand(
    Guid? Id = null,
    string Name = "",
    string? Description = null,
    string? ParentCompany = null,
    string OwnershipType = "unknown",
    string? Headquarters = null,
    string Status = "neutral",
    IReadOnlyList<string>? CategoryTags = null,
    IReadOnlyList<CareerSourceInputDto>? CareerSources = null,
    Guid? VersionToken = null,
    string? OwnerId = null,
    string Actor = "system") : ICommand<CompanyRecordResultDto>;

public sealed class RecordCompanyCommandHandler : ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public RecordCompanyCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<CompanyRecordResultDto>> HandleAsync(
        RecordCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;

        var now = _clock.UtcNow;

        // Lookup existing company by ID or Normalized Name
        Company? existing = null;
        if (command.Id.HasValue)
        {
            var byIdResult = await _context.Companies.GetByIdAsync(ownerId, command.Id.Value, cancellationToken);
            if (byIdResult.IsSuccess && byIdResult.Value is not null)
            {
                existing = byIdResult.Value;
            }
        }

        var trimmedName = string.IsNullOrWhiteSpace(command.Name) ? existing?.Name : command.Name.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Company name is required.");
        }

        if (trimmedName.Length > 150)
        {
            return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Company name cannot exceed 150 characters.");
        }

        if (command.Description is not null && command.Description.Length > 300)
        {
            return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Company description cannot exceed 300 characters.");
        }

        if (command.ParentCompany is not null && command.ParentCompany.Length > 150)
        {
            return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Parent company cannot exceed 150 characters.");
        }

        if (command.Headquarters is not null && command.Headquarters.Length > 150)
        {
            return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Headquarters cannot exceed 150 characters.");
        }

        if (existing is null)
        {
            var byNameResult = await _context.Companies.GetByNormalizedNameAsync(ownerId, trimmedName.ToLowerInvariant(), cancellationToken);
            if (byNameResult.IsSuccess && byNameResult.Value is not null)
            {
                existing = byNameResult.Value;
            }
        }

        Company company;
        bool isCreate;
        List<string> changedFields = [];

        var parsedOwnership = existing?.OwnershipType ?? OwnershipType.Unknown;
        if (!string.IsNullOrWhiteSpace(command.OwnershipType) && TrackingEnumExtensions.TryParseOwnershipType(command.OwnershipType, out var po))
        {
            parsedOwnership = po;
        }

        var parsedStatus = existing?.Status ?? CompanyStatus.Target;
        if (!string.IsNullOrWhiteSpace(command.Status) && TrackingEnumExtensions.TryParseCompanyStatus(command.Status, out var ps))
        {
            parsedStatus = ps;
        }

        var companyFieldNamesChanged = new List<string>();
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(trimmedName) && !string.Equals(trimmedName, existing.Name, StringComparison.Ordinal))
            {
                companyFieldNamesChanged.Add("name");
            }
            if (command.Description is not null && !string.Equals(command.Description, existing.Description, StringComparison.Ordinal))
            {
                companyFieldNamesChanged.Add("description");
            }
            if (command.ParentCompany is not null && !string.Equals(command.ParentCompany, existing.ParentCompany, StringComparison.Ordinal))
            {
                companyFieldNamesChanged.Add("parent_company");
            }
            if (command.Headquarters is not null && !string.Equals(command.Headquarters, existing.Headquarters, StringComparison.Ordinal))
            {
                companyFieldNamesChanged.Add("headquarters");
            }
            if (!string.IsNullOrWhiteSpace(command.OwnershipType) && parsedOwnership != existing.OwnershipType)
            {
                companyFieldNamesChanged.Add("ownership_type");
            }
            if (!string.IsNullOrWhiteSpace(command.Status) && parsedStatus != existing.Status)
            {
                companyFieldNamesChanged.Add("status");
            }
            if (command.CategoryTags is not null && !command.CategoryTags.SequenceEqual(existing.CategoryTags))
            {
                companyFieldNamesChanged.Add("category_tags");
            }
        }
        var hasCompanyFieldChanges = companyFieldNamesChanged.Count > 0;
        bool companyFieldsUpdated = false;

        if (existing is not null)
        {
            if (!command.Id.HasValue && !command.VersionToken.HasValue)
            {
                var existingSources = await _context.Companies.GetCareerSourcesAsync(ownerId, existing.Id, includeInactive: true, cancellationToken);
                var sourceCount = existingSources.IsSuccess && existingSources.Value is not null ? existingSources.Value.Count : 0;
                return Result<CompanyRecordResultDto>.Success(new CompanyRecordResultDto(
                    Company: new CompanyRecordSummaryDto(
                        Id: existing.Id,
                        Name: existing.Name,
                        Description: existing.Description,
                        OwnershipType: existing.OwnershipType.ToSnakeCase(),
                        Status: existing.Status.ToSnakeCase(),
                        VersionToken: existing.VersionToken,
                        CareerSourceCount: sourceCount),
                    Status: "already_exists"));
            }

            if (command.VersionToken.HasValue && command.VersionToken.Value != existing.VersionToken)
            {
                return Result<CompanyRecordResultDto>.Failure(
                    ErrorCode.StaleVersionToken,
                    "Company version token mismatch or stale.");
            }

            if (hasCompanyFieldChanges)
            {
                // Concurrency check for existing company
                if (!command.VersionToken.HasValue || command.VersionToken.Value != existing.VersionToken)
                {
                    return Result<CompanyRecordResultDto>.Failure(
                        ErrorCode.StaleVersionToken,
                        "Company version token mismatch or stale.");
                }

                existing.Update(
                    name: trimmedName,
                    description: command.Description ?? existing.Description,
                    parentCompany: command.ParentCompany ?? existing.ParentCompany,
                    ownershipType: parsedOwnership,
                    headquarters: command.Headquarters ?? existing.Headquarters,
                    categoryTags: command.CategoryTags ?? existing.CategoryTags,
                    status: parsedStatus,
                    now: now);

                await _context.Companies.UpsertCompanyAsync(existing, cancellationToken);
                companyFieldsUpdated = true;
                changedFields = [.. companyFieldNamesChanged, "version_token"];
            }

            company = existing;
            isCreate = false;
        }
        else
        {
            company = Company.Create(
                ownerId: ownerId,
                name: trimmedName,
                description: command.Description,
                parentCompany: command.ParentCompany,
                ownershipType: parsedOwnership,
                headquarters: command.Headquarters,
                categoryTags: command.CategoryTags,
                status: parsedStatus,
                id: command.Id,
                now: now);

            await _context.Companies.UpsertCompanyAsync(company, cancellationToken);
            isCreate = true;
            changedFields = ["name", "status", "created_at"];
        }

        // Upsert career sources if supplied
        if (command.CareerSources is not null)
        {
            var existingSourcesRes = await _context.Companies.GetCareerSourcesAsync(ownerId, company.Id, includeInactive: true, cancellationToken);
            var currentSources = existingSourcesRes.IsSuccess && existingSourcesRes.Value is not null
                ? existingSourcesRes.Value.ToList()
                : [];

            foreach (var csInput in command.CareerSources)
            {
                if (string.IsNullOrWhiteSpace(csInput.AtsType))
                {
                    return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Career source AtsType is required.");
                }

                CareerSource? matchedSource = null;
                if (csInput.Id.HasValue)
                {
                    matchedSource = currentSources.FirstOrDefault(s => s.Id == csInput.Id.Value);
                }

                if (matchedSource is null)
                {
                    matchedSource = currentSources.FirstOrDefault(s =>
                        string.Equals(s.AtsType, csInput.AtsType.Trim(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(s.Tenant ?? string.Empty, csInput.Tenant?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(s.Slug ?? string.Empty, csInput.Slug?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                }

                _ = TrackingEnumExtensions.TryParseFetchMethod(csInput.FetchMethod, out var fetchMethod);

                if (matchedSource is not null)
                {
                    if (csInput.VersionToken.HasValue && csInput.VersionToken.Value != matchedSource.VersionToken)
                    {
                        return Result<CompanyRecordResultDto>.Failure(
                            ErrorCode.StaleVersionToken,
                            "Career source version token mismatch or stale.");
                    }

                    matchedSource.Update(
                        atsType: csInput.AtsType,
                        tenant: csInput.Tenant,
                        slug: csInput.Slug,
                        fetchMethod: fetchMethod,
                        notes: csInput.Notes,
                        isActive: csInput.IsActive,
                        now: now);

                    await _context.Companies.UpsertCareerSourceAsync(matchedSource, cancellationToken);
                }
                else
                {
                    var newSource = CareerSource.Create(
                        ownerId: ownerId,
                        companyId: company.Id,
                        atsType: csInput.AtsType,
                        tenant: csInput.Tenant,
                        slug: csInput.Slug,
                        fetchMethod: fetchMethod,
                        notes: csInput.Notes,
                        isActive: csInput.IsActive ?? true,
                        id: csInput.Id,
                        now: now);

                    await _context.Companies.UpsertCareerSourceAsync(newSource, cancellationToken);
                    currentSources.Add(newSource);
                }
            }

            changedFields.Add("career_sources");
        }

        // Count total career sources
        var totalSourcesRes = await _context.Companies.GetCareerSourcesAsync(ownerId, company.Id, includeInactive: true, cancellationToken);
        var totalCount = totalSourcesRes.IsSuccess && totalSourcesRes.Value is not null
            ? totalSourcesRes.Value.Count
            : 0;

        // Append audit log entries
        if (isCreate)
        {
            var auditSummary = new CompanyAuditSummary(
                CompanyId: company.Id,
                ChangedFieldNames: changedFields,
                Action: AuditOperation.CompanyCreate,
                Timestamp: now);

            await AppendAuditAsync(
                ownerId,
                command.Actor,
                AuditOperation.CompanyCreate,
                company.Id,
                company.VersionToken,
                auditSummary,
                company,
                cancellationToken);
        }
        else
        {
            if (companyFieldsUpdated)
            {
                var auditSummary = new CompanyAuditSummary(
                    CompanyId: company.Id,
                    ChangedFieldNames: changedFields,
                    Action: AuditOperation.CompanyUpdate,
                    Timestamp: now);

                await AppendAuditAsync(
                    ownerId,
                    command.Actor,
                    AuditOperation.CompanyUpdate,
                    company.Id,
                    company.VersionToken,
                    auditSummary,
                    company,
                    cancellationToken);
            }

            if (command.CareerSources is not null && command.CareerSources.Count > 0)
            {
                var csAuditSummary = new CompanyAuditSummary(
                    CompanyId: company.Id,
                    ChangedFieldNames: ["career_sources"],
                    Action: AuditOperation.CareerSourceUpsert,
                    Timestamp: now);

                await AppendAuditAsync(
                    ownerId,
                    command.Actor,
                    AuditOperation.CareerSourceUpsert,
                    company.Id,
                    company.VersionToken,
                    csAuditSummary,
                    company,
                    cancellationToken);
            }
        }

        return Result<CompanyRecordResultDto>.Success(new CompanyRecordResultDto(
            Company: new CompanyRecordSummaryDto(
                Id: company.Id,
                Name: company.Name,
                Description: company.Description,
                OwnershipType: company.OwnershipType.ToSnakeCase(),
                Status: company.Status.ToSnakeCase(),
                VersionToken: company.VersionToken,
                CareerSourceCount: totalCount),
            Status: isCreate ? "created" : "updated"));
    }

    private Task AppendAuditAsync(
        string ownerId,
        string actor,
        string operation,
        Guid companyId,
        Guid versionToken,
        CompanyAuditSummary summary,
        Company company,
        CancellationToken cancellationToken)
    {
        var isCreate = operation == AuditOperation.CompanyCreate;
        var afterDict = new Dictionary<string, object?>
        {
            ["name"] = company.Name,
            ["status"] = company.Status.ToSnakeCase(),
            ["ownership_type"] = company.OwnershipType.ToSnakeCase()
        };

        var details = AuditDetails.ForEntity(
            changedFields: summary.ChangedFieldNames,
            before: isCreate ? null : new Dictionary<string, object?>(),
            after: afterDict);

        var entry = AuditLogEntry.CreateForTracking(
            ownerId: ownerId,
            actor: actor,
            operation: operation,
            entityType: "company",
            entityId: companyId,
            resultingVersionToken: versionToken,
            occurredAtUtc: summary.Timestamp.UtcDateTime,
            details: details);

        return _context.AppendAuditEntryAsync(entry, cancellationToken);
    }
}

public sealed class RecordCompanyCommandValidator : ICommandValidator<RecordCompanyCommand>
{
    public ResultError? Validate(RecordCompanyCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return new ResultError(ErrorCode.ValidationError, "Company name is required.");
        }

        if (command.Name.Trim().Length > 150)
        {
            return new ResultError(ErrorCode.ValidationError, "Company name cannot exceed 150 characters.");
        }

        if (command.Description is not null && command.Description.Length > 300)
        {
            return new ResultError(ErrorCode.ValidationError, "Company description cannot exceed 300 characters.");
        }

        if (command.ParentCompany is not null && command.ParentCompany.Length > 150)
        {
            return new ResultError(ErrorCode.ValidationError, "Parent company cannot exceed 150 characters.");
        }

        if (command.Headquarters is not null && command.Headquarters.Length > 150)
        {
            return new ResultError(ErrorCode.ValidationError, "Headquarters cannot exceed 150 characters.");
        }

        if (command.CategoryTags is not null)
        {
            if (command.CategoryTags.Count > 20)
            {
                return new ResultError(ErrorCode.ValidationError, "Category tags count cannot exceed 20.");
            }

            foreach (var tag in command.CategoryTags)
            {
                if (tag.Trim().Length > 50)
                {
                    return new ResultError(ErrorCode.ValidationError, $"Category tag '{tag}' exceeds 50 characters.");
                }
            }
        }

        return null;
    }
}
