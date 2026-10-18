using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;

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
    CompanyRecordSummaryDto Company);

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

    public Task<Result<CompanyRecordResultDto>> HandleAsync(
        RecordCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        // Stub for test-first phase: will fail assertions in tests
        return Task.FromResult(Result<CompanyRecordResultDto>.Failure(
            ErrorCode.ValidationError,
            "Stub: not implemented yet."));
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
