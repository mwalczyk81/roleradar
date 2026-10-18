using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Tracking.Commands;

public sealed class TrackerLogCommandHandler : ICommandHandler<TrackerLogCommand, TrackerLogResultDto>
{
    private readonly ITrackingTransactionContext _context;

    public TrackerLogCommandHandler(ITrackingTransactionContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<Result<TrackerLogResultDto>> HandleAsync(TrackerLogCommand command, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("TrackerLogCommandHandler not implemented yet.");
    }
}
