using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Decorators;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddRoleRadarApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUtcClock>(SystemUtcClock.Instance);

        // Handlers are registered incrementally via AddCommandHandler / AddQueryHandler as they land in T028, T033, and T043.
        return services;
    }

    public static IServiceCollection AddCommandHandler<TCommand, TResponse, THandler>(this IServiceCollection services)
        where TCommand : ICommand<TResponse>
        where THandler : class, ICommandHandler<TCommand, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResponse>>(sp =>
        {
            var inner = sp.GetRequiredService<THandler>();
            var repository = sp.GetRequiredService<INoteRepository>();
            var validators = sp.GetServices<ICommandValidator<TCommand>>();
            var logger = sp.GetRequiredService<ILogger<LoggingCommandDecorator<TCommand, TResponse>>>();

            var transactional = new TransactionAndAuditCommandDecorator<TCommand, TResponse>(
                inner,
                repository,
                validators);

            return new LoggingCommandDecorator<TCommand, TResponse>(transactional, logger);
        });

        return services;
    }

    public static IServiceCollection AddQueryHandler<TQuery, TResponse, THandler>(this IServiceCollection services)
        where TQuery : IQuery<TResponse>
        where THandler : class, IQueryHandler<TQuery, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<THandler>();
        services.AddScoped<IQueryHandler<TQuery, TResponse>>(sp =>
        {
            var inner = sp.GetRequiredService<THandler>();
            var logger = sp.GetRequiredService<ILogger<LoggingQueryDecorator<TQuery, TResponse>>>();
            return new LoggingQueryDecorator<TQuery, TResponse>(inner, logger);
        });

        return services;
    }
}
