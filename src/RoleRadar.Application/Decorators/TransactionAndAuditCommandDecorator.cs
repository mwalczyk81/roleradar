using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Decorators;

public sealed class TransactionAndAuditCommandDecorator<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private readonly ICommandHandler<TCommand, TResponse> _inner;
    private readonly INoteRepository _repository;
    private readonly IEnumerable<ICommandValidator<TCommand>> _validators;

    public TransactionAndAuditCommandDecorator(
        ICommandHandler<TCommand, TResponse> inner,
        INoteRepository repository,
        IEnumerable<ICommandValidator<TCommand>> validators)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(validators);

        _inner = inner;
        _repository = repository;
        _validators = validators;
    }

    public async Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var validator in _validators)
        {
            var validationError = validator.Validate(command);
            if (validationError is not null)
            {
                return Result<TResponse>.Failure(validationError.Value);
            }
        }

        var readiness = await _repository.CheckReadinessAsync(cancellationToken);
        if (!readiness.IsSuccess)
        {
            return Result<TResponse>.Failure(readiness.Error!.Value);
        }

        return await _repository.ExecuteInTransactionAsync(
            (_, ct) => _inner.HandleAsync(command, ct),
            cancellationToken);
    }
}
