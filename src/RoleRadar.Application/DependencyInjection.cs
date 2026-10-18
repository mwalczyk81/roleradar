using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Decorators;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddRoleRadarApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUtcClock>(SystemUtcClock.Instance);

        // US1 & US4 Validators
        services.AddSingleton<ICommandValidator<CreateNoteCommand>, CreateNoteCommandValidator>();
        services.AddSingleton<ICommandValidator<AppendToNoteCommand>, AppendToNoteCommandValidator>();
        services.AddSingleton<ICommandValidator<ReplaceNoteSectionCommand>, ReplaceNoteSectionCommandValidator>();
        services.AddSingleton<ICommandValidator<ArchiveNoteCommand>, ArchiveNoteCommandValidator>();
        services.AddSingleton<ICommandValidator<RoleRadar.Application.Import.Commands.ImportMarkdownCommand>, RoleRadar.Application.Import.Commands.ImportMarkdownCommandValidator>();

        // US1, US2 & US4 Command Handlers (wrapped by TransactionAndAuditCommandDecorator + LoggingCommandDecorator)
        services.AddCommandHandler<CreateNoteCommand, NoteCreatedDto, CreateNoteCommandHandler>();
        services.AddCommandHandler<AppendToNoteCommand, NoteUpdatedDto, AppendToNoteCommandHandler>();
        services.AddCommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto, ReplaceNoteSectionCommandHandler>();
        services.AddCommandHandler<ArchiveNoteCommand, NoteArchivedDto, ArchiveNoteCommandHandler>();
        services.AddCommandHandler<RoleRadar.Application.Import.Commands.ImportMarkdownCommand, RoleRadar.Application.Import.Commands.ImportMarkdownSummaryDto, RoleRadar.Application.Import.Commands.ImportMarkdownCommandHandler>();

        // US1 Query Handlers (wrapped by LoggingQueryDecorator)
        services.AddQueryHandler<GetNoteQuery, NoteDetailDto, GetNoteQueryHandler>();
        services.AddQueryHandler<SearchNotesQuery, NoteSearchResponseDto, SearchNotesQueryHandler>();

        // US1 Tracking Commands and Queries
        services.AddSingleton<ICommandValidator<RoleRadar.Application.Tracking.Commands.RecordPostingCommand>, RoleRadar.Application.Tracking.Commands.RecordPostingCommandValidator>();
        services.AddSingleton<ICommandValidator<RoleRadar.Application.Tracking.Commands.TransitionPostingCommand>, RoleRadar.Application.Tracking.Commands.TransitionPostingCommandValidator>();
        services.AddSingleton<ICommandValidator<RoleRadar.Application.Tracking.Commands.RecordCompanyCommand>, RoleRadar.Application.Tracking.Commands.RecordCompanyCommandValidator>();
        services.AddCommandHandler<RoleRadar.Application.Tracking.Commands.RecordPostingCommand, RoleRadar.Application.Tracking.Commands.PostingRecordResultDto, RoleRadar.Application.Tracking.Commands.RecordPostingCommandHandler>();
        services.AddCommandHandler<RoleRadar.Application.Tracking.Commands.TransitionPostingCommand, RoleRadar.Application.Tracking.Commands.PostingTransitionResultDto, RoleRadar.Application.Tracking.Commands.TransitionPostingCommandHandler>();
        services.AddCommandHandler<RoleRadar.Application.Tracking.Commands.RecordCompanyCommand, RoleRadar.Application.Tracking.Commands.CompanyRecordResultDto, RoleRadar.Application.Tracking.Commands.RecordCompanyCommandHandler>();
        services.AddCommandHandler<RoleRadar.Application.Tracking.Commands.TrackerLogCommand, RoleRadar.Application.Tracking.Commands.TrackerLogResultDto, RoleRadar.Application.Tracking.Commands.TrackerLogCommandHandler>();
        services.AddQueryHandler<RoleRadar.Application.Tracking.Queries.TrackerQueryRequest, RoleRadar.Application.Tracking.Queries.TrackerQueryResultDto, RoleRadar.Application.Tracking.Queries.TrackerQueryHandler>();

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
