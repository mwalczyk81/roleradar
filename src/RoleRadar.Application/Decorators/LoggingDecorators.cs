using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RoleRadar.Application.Abstractions;

namespace RoleRadar.Application.Decorators;

public sealed class LoggingCommandDecorator<TCommand, TResponse> : ICommandHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>
{
    private static readonly Action<ILogger, string, Exception?> LogStarting =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1001, "CommandStarting"),
            "Executing command {CommandName}");

    private static readonly Action<ILogger, string, double, Exception?> LogSucceeded =
        LoggerMessage.Define<string, double>(
            LogLevel.Information,
            new EventId(1002, "CommandSucceeded"),
            "Command {CommandName} succeeded in {ElapsedMs:F2} ms");

    private static readonly Action<ILogger, string, ErrorCode, double, Exception?> LogFailed =
        LoggerMessage.Define<string, ErrorCode, double>(
            LogLevel.Warning,
            new EventId(1003, "CommandFailed"),
            "Command {CommandName} returned failure {ErrorCode} in {ElapsedMs:F2} ms");

    private readonly ICommandHandler<TCommand, TResponse> _inner;
    private readonly ILogger<LoggingCommandDecorator<TCommand, TResponse>> _logger;

    public LoggingCommandDecorator(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<LoggingCommandDecorator<TCommand, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _logger = logger;
    }

    public async Task<Result<TResponse>> HandleAsync(
        TCommand command,
        CancellationToken cancellationToken = default)
    {
        var commandName = typeof(TCommand).Name;
        LogStarting(_logger, commandName, null);
        var sw = Stopwatch.StartNew();

        var result = await _inner.HandleAsync(command, cancellationToken);
        sw.Stop();

        if (result.IsSuccess)
        {
            LogSucceeded(_logger, commandName, sw.Elapsed.TotalMilliseconds, null);
        }
        else
        {
            LogFailed(_logger, commandName, result.Error?.Code ?? ErrorCode.ValidationError, sw.Elapsed.TotalMilliseconds, null);
        }

        return result;
    }
}

public sealed class LoggingQueryDecorator<TQuery, TResponse> : IQueryHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    private static readonly Action<ILogger, string, Exception?> LogStarting =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(2001, "QueryStarting"),
            "Executing query {QueryName}");

    private static readonly Action<ILogger, string, double, Exception?> LogSucceeded =
        LoggerMessage.Define<string, double>(
            LogLevel.Debug,
            new EventId(2002, "QuerySucceeded"),
            "Query {QueryName} succeeded in {ElapsedMs:F2} ms");

    private static readonly Action<ILogger, string, ErrorCode, double, Exception?> LogFailed =
        LoggerMessage.Define<string, ErrorCode, double>(
            LogLevel.Warning,
            new EventId(2003, "QueryFailed"),
            "Query {QueryName} returned failure {ErrorCode} in {ElapsedMs:F2} ms");

    private readonly IQueryHandler<TQuery, TResponse> _inner;
    private readonly ILogger<LoggingQueryDecorator<TQuery, TResponse>> _logger;

    public LoggingQueryDecorator(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<LoggingQueryDecorator<TQuery, TResponse>> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = inner;
        _logger = logger;
    }

    public async Task<Result<TResponse>> HandleAsync(
        TQuery query,
        CancellationToken cancellationToken = default)
    {
        var queryName = typeof(TQuery).Name;
        LogStarting(_logger, queryName, null);
        var sw = Stopwatch.StartNew();

        var result = await _inner.HandleAsync(query, cancellationToken);
        sw.Stop();

        if (result.IsSuccess)
        {
            LogSucceeded(_logger, queryName, sw.Elapsed.TotalMilliseconds, null);
        }
        else
        {
            LogFailed(_logger, queryName, result.Error?.Code ?? ErrorCode.ValidationError, sw.Elapsed.TotalMilliseconds, null);
        }

        return result;
    }
}
