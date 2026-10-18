using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record ApplicationEventInputDto(
    string EventType,
    string EventDate,
    string? EventSource = null,
    string? Notes = null);

public sealed record RecordApplicationCommand(
    Guid PostingId,
    string Action,
    string? SubmittedDate = null,
    decimal? DesiredSalaryMin = null,
    decimal? DesiredSalaryMax = null,
    string? DesiredSalaryCurrency = null,
    string? TargetStartDate = null,
    string? ResumeVariant = null,
    string? ResumePointer = null,
    Guid? ReferralContactId = null,
    string? ConfirmationCode = null,
    bool OverrideDecided = false,
    string? OverrideReason = null,
    ApplicationEventInputDto? Event = null,
    string Actor = "agent",
    string? OwnerId = null) : ICommand<ApplicationRecordResultDto>;

public sealed record ApplicationRecordResultDto(
    Guid ApplicationId,
    Guid PostingId,
    string CurrentOutcome,
    bool OverrideApplied,
    int EventCount,
    DateTimeOffset UpdatedAtUtc);

public sealed class RecordApplicationCommandValidator : ICommandValidator<RecordApplicationCommand>
{
    public ResultError? Validate(RecordApplicationCommand command)
    {
        return null;
    }
}

public sealed class RecordApplicationCommandHandler : ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>
{
    private readonly ITrackingTransactionContext _context;

    public RecordApplicationCommandHandler(ITrackingTransactionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Result<ApplicationRecordResultDto>> HandleAsync(
        RecordApplicationCommand command,
        CancellationToken cancellationToken = default)
    {
        // Stub implementation returning failure so tests fail on assertions before implementation phase
        return Task.FromResult(Result<ApplicationRecordResultDto>.Failure(
            ErrorCode.ValidationError,
            "Stub not implemented yet."));
    }
}
