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

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return Result<CompanyRecordResultDto>.Failure(ErrorCode.ValidationError, "Company name is required.");
        }

        var trimmedName = command.Name.Trim();
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

        _ = TrackingEnumExtensions.TryParseOwnershipType(command.OwnershipType, out var parsedOwnership);
        _ = TrackingEnumExtensions.TryParseCompanyStatus(command.Status, out var parsedStatus);

        if (existing is not null)
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
                description: command.Description,
                parentCompany: command.ParentCompany,
                ownershipType: parsedOwnership,
                headquarters: command.Headquarters,
                categoryTags: command.CategoryTags,
                status: parsedStatus,
                now: now);

            await _context.Companies.UpsertCompanyAsync(existing, cancellationToken);
            company = existing;
            isCreate = false;
            changedFields = ["name", "status", "version_token"];
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

        // Append audit log entry
        var auditSummary = new CompanyAuditSummary(
            CompanyId: company.Id,
            ChangedFieldNames: changedFields,
            Action: isCreate ? "company_create" : "company_update",
            Timestamp: now);

        await AppendAuditAsync(
            ownerId,
            command.Actor,
            isCreate ? "company_create" : "company_update",
            company.Id,
            company.VersionToken,
            auditSummary,
            cancellationToken);

        return Result<CompanyRecordResultDto>.Success(new CompanyRecordResultDto(
            Company: new CompanyRecordSummaryDto(
                Id: company.Id,
                Name: company.Name,
                Description: company.Description,
                OwnershipType: company.OwnershipType.ToSnakeCase(),
                Status: company.Status.ToSnakeCase(),
                VersionToken: company.VersionToken,
                CareerSourceCount: totalCount)));
    }

    private Task AppendAuditAsync(
        string ownerId,
        string actor,
        string operation,
        Guid companyId,
        Guid versionToken,
        CompanyAuditSummary summary,
        CancellationToken cancellationToken)
    {
        var isCreate = operation == "company_create";
        var beforeSummary = isCreate
            ? null
            : new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: 0,
                status: "prior",
                excerpt: string.Empty);

        var entry = AuditLogEntry.Create(
            ownerId: ownerId,
            actor: actor,
            operation: isCreate ? AuditOperation.NoteCreate : AuditOperation.NoteAppend,
            targetNoteId: companyId,
            noteRevisionId: null,
            appendedText: isCreate ? null : string.Join(",", summary.ChangedFieldNames),
            resultingVersionToken: versionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: summary.Timestamp.UtcDateTime,
            beforeSummary: beforeSummary,
            afterSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: summary.ChangedFieldNames.Count,
                status: operation,
                excerpt: string.Join(",", summary.ChangedFieldNames)));

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
