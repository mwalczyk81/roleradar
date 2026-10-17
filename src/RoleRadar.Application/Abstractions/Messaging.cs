namespace RoleRadar.Application.Abstractions;

public interface ICommand<TResponse>;

public interface IQuery<TResponse>;

public interface ICommandHandler<in TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default);
}

public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken = default);
}

public interface ICommandValidator<in TCommand>
{
    ResultError? Validate(TCommand command);
}
