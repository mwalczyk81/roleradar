using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Integrity;

public sealed record IntegrityAnomalyDto(
    string Kind,
    string Description,
    Guid SourceId,
    Guid? TargetId = null);

public sealed record VerifyIntegrityResultDto(
    int AnomalyCount,
    IReadOnlyList<IntegrityAnomalyDto> Anomalies);

public sealed record VerifyIntegrityQuery(string? OwnerId = null) : IQuery<VerifyIntegrityResultDto>;

public interface IIntegrityRepository
{
    Task<Result<VerifyIntegrityResultDto>> VerifyIntegrityAsync(string? ownerId = null, CancellationToken cancellationToken = default);
}

public sealed class VerifyIntegrityQueryHandler : IQueryHandler<VerifyIntegrityQuery, VerifyIntegrityResultDto>
{
    private readonly IIntegrityRepository _integrityRepository;

    public VerifyIntegrityQueryHandler(IIntegrityRepository integrityRepository)
    {
        _integrityRepository = integrityRepository ?? throw new ArgumentNullException(nameof(integrityRepository));
    }

    public Task<Result<VerifyIntegrityResultDto>> HandleAsync(VerifyIntegrityQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _integrityRepository.VerifyIntegrityAsync(query.OwnerId, cancellationToken);
    }
}
