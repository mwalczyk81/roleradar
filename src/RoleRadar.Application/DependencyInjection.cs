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
        services.AddSingleton<ICommandValidator<Import.Commands.ImportMarkdownCommand>, Import.Commands.ImportMarkdownCommandValidator>();

        // US1, US2 & US4 Command Handlers (wrapped by TransactionAndAuditCommandDecorator + LoggingCommandDecorator)
        services.AddCommandHandler<CreateNoteCommand, NoteCreatedDto, CreateNoteCommandHandler>();
        services.AddCommandHandler<AppendToNoteCommand, NoteUpdatedDto, AppendToNoteCommandHandler>();
        services.AddCommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto, ReplaceNoteSectionCommandHandler>();
        services.AddCommandHandler<ArchiveNoteCommand, NoteArchivedDto, ArchiveNoteCommandHandler>();
        services.AddCommandHandler<Import.Commands.ImportMarkdownCommand, Import.Commands.ImportMarkdownSummaryDto, Import.Commands.ImportMarkdownCommandHandler>();

        // US1 Query Handlers (wrapped by LoggingQueryDecorator)
        services.AddQueryHandler<GetNoteQuery, NoteDetailDto, GetNoteQueryHandler>();
        services.AddQueryHandler<SearchNotesQuery, NoteSearchResponseDto, SearchNotesQueryHandler>();

        // US1 Tracking Commands and Queries
        services.AddSingleton<ICommandValidator<Tracking.Commands.RecordPostingCommand>, Tracking.Commands.RecordPostingCommandValidator>();
        services.AddSingleton<ICommandValidator<Tracking.Commands.TransitionPostingCommand>, Tracking.Commands.TransitionPostingCommandValidator>();
        services.AddSingleton<ICommandValidator<Tracking.Commands.RecordCompanyCommand>, Tracking.Commands.RecordCompanyCommandValidator>();
        services.AddSingleton<ICommandValidator<Tracking.Commands.RecordApplicationCommand>, Tracking.Commands.RecordApplicationCommandValidator>();
        services.AddCommandHandler<Tracking.Commands.RecordPostingCommand, Tracking.Commands.PostingRecordResultDto, Tracking.Commands.RecordPostingCommandHandler>();
        services.AddCommandHandler<Tracking.Commands.TransitionPostingCommand, Tracking.Commands.PostingTransitionResultDto, Tracking.Commands.TransitionPostingCommandHandler>();
        services.AddCommandHandler<Tracking.Commands.RecordCompanyCommand, Tracking.Commands.CompanyRecordResultDto, Tracking.Commands.RecordCompanyCommandHandler>();
        services.AddCommandHandler<Tracking.Commands.TrackerLogCommand, Tracking.Commands.TrackerLogResultDto, Tracking.Commands.TrackerLogCommandHandler>();
        services.AddCommandHandler<Tracking.Commands.RecordApplicationCommand, Tracking.Commands.ApplicationRecordResultDto, Tracking.Commands.RecordApplicationCommandHandler>();
        services.AddQueryHandler<Tracking.Queries.TrackerQueryRequest, Tracking.Queries.TrackerQueryResultDto, Tracking.Queries.TrackerQueryHandler>();

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
