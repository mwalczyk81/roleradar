using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Decorators;

/// <summary>
/// Runs every registered <see cref="ICommandValidator{TCommand}"/>, checks store readiness, then runs the inner
/// handler inside one repository transaction. It does not write audit entries itself: the handler or the
/// repository write port appends them inside this transaction, so a write and its audit entry commit or roll
/// back together (ADR 0002, ADR 0006).
/// </summary>
public sealed class TransactionCommandDecorator<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private readonly ICommandHandler<TCommand, TResponse> _inner;
    private readonly INoteRepository _repository;
    private readonly IEnumerable<ICommandValidator<TCommand>> _validators;

    public TransactionCommandDecorator(
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
