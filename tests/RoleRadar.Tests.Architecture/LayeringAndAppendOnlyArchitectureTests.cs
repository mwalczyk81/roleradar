using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetArchTest.Rules;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Decorators;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class LayeringAndAppendOnlyArchitectureTests
{
    private static readonly Assembly DomainAssembly = typeof(Note).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(INoteRepository).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(MongoNoteRepository).Assembly;

    [Fact]
    public void Domain_MustNotDependOn_Application_Infrastructure_Hosts_Or_ExternalDrivers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RoleRadar.Application",
                "RoleRadar.Infrastructure",
                "RoleRadar.Mcp.Stdio",
                "RoleRadar.Cli",
                "MongoDB.Driver",
                "MongoDB.Bson",
                "ModelContextProtocol")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Domain inward dependency violation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_MustNotDependOn_Infrastructure_Hosts_Or_MongoDriver()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RoleRadar.Infrastructure",
                "RoleRadar.Mcp.Stdio",
                "RoleRadar.Cli",
                "MongoDB.Driver",
                "MongoDB.Bson",
                "ModelContextProtocol")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Application inward dependency violation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Infrastructure_MustNotDependOn_DeliveryHostAssemblies()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "RoleRadar.Mcp.Stdio",
                "RoleRadar.Cli",
                "ModelContextProtocol",
                "System.CommandLine")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Infrastructure dependency violation: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Solution_MustNotContain_GenericIRepositoryOrMediatR()
    {
        var allTypes = DomainAssembly.GetTypes()
            .Concat(ApplicationAssembly.GetTypes())
            .Concat(InfrastructureAssembly.GetTypes())
            .ToArray();

        var genericRepositories = allTypes
            .Where(t => t.IsInterface && t.IsGenericTypeDefinition && t.Name.StartsWith("IRepository", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(genericRepositories);

        var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies()
            .Concat(InfrastructureAssembly.GetReferencedAssemblies())
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name.Contains("MediatR", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryCommandHandler_MustBeRegisteredAndWrappedBy_TransactionAndAuditCommandDecorator()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<INoteRepository>(new NoOpNoteRepository());
        services.AddRoleRadarApplication();

        using var provider = services.BuildServiceProvider();

        // Verify the open/helper decorator registration pipeline wraps any ICommandHandler<,>
        // with LoggingCommandDecorator<,> (outer) -> TransactionAndAuditCommandDecorator<,> -> Inner Handler
        var testServices = new ServiceCollection();
        testServices.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        testServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        testServices.AddSingleton<INoteRepository>(new NoOpNoteRepository());
        testServices.AddRoleRadarApplication();
        testServices.AddCommandHandler<SampleArchitectureCommand, string, SampleArchitectureCommandHandler>();

        using var testProvider = testServices.BuildServiceProvider();
        var resolvedSample = testProvider.GetRequiredService<ICommandHandler<SampleArchitectureCommand, string>>();
        Assert.True(
            ContainsDecoratorInChain(resolvedSample, typeof(TransactionAndAuditCommandDecorator<,>)),
            "AddCommandHandler must wrap handlers with TransactionAndAuditCommandDecorator.");

        // Also inspect every concrete ICommandHandler<,> in RoleRadar.Application as each handler lands across phases
        var concreteCommandHandlers = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && !t.IsGenericTypeDefinition)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
                .Select(i => (HandlerType: t, ServiceInterface: i)))
            .ToArray();

        foreach (var (handlerType, serviceInterface) in concreteCommandHandlers)
        {
            var resolved = provider.GetService(serviceInterface);
            Assert.True(
                resolved is not null,
                $"Command handler {handlerType.FullName} implementing {serviceInterface.Name} is not registered in AddRoleRadarApplication().");

            Assert.True(
                ContainsDecoratorInChain(resolved!, typeof(TransactionAndAuditCommandDecorator<,>)),
                $"Command handler {handlerType.FullName} is not wrapped by TransactionAndAuditCommandDecorator<,>.");
        }
    }

    [Fact]
    public void MongoNoteRepository_And_INoteRepository_MustNotExpose_UpdateOrDeleteOnRevisionsOrAudit_OrHardDeleteOnNotes()
    {
        var repoInterfaceMethods = typeof(INoteRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Concat(typeof(INoteTransactionContext).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain(repoInterfaceMethods, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(repoInterfaceMethods, name => name.Contains("Remove", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(repoInterfaceMethods, name =>
            (name.Contains("Update", StringComparison.OrdinalIgnoreCase) || name.Contains("Replace", StringComparison.OrdinalIgnoreCase)) &&
            (name.Contains("Revision", StringComparison.OrdinalIgnoreCase) || name.Contains("Audit", StringComparison.OrdinalIgnoreCase)));

        var mongoRepoMethods = typeof(MongoNoteRepository)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Concat(typeof(MongoNoteRepository).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(nt => nt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain(mongoRepoMethods, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(mongoRepoMethods, name =>
            (name.Contains("Update", StringComparison.OrdinalIgnoreCase) || name.Contains("Replace", StringComparison.OrdinalIgnoreCase)) &&
            (name.Contains("Revision", StringComparison.OrdinalIgnoreCase) || name.Contains("Audit", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ContainsDecoratorInChain(object current, Type expectedOpenGenericDecorator)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var cursor = current;
        while (cursor is not null && visited.Add(cursor))
        {
            var type = cursor.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == expectedOpenGenericDecorator)
            {
                return true;
            }

            var innerField = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(f => f.FieldType.IsGenericType &&
                                     f.FieldType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));

            cursor = innerField?.GetValue(cursor);
        }

        return false;
    }

    private sealed record SampleArchitectureCommand : ICommand<string>;

    private sealed class SampleArchitectureCommandHandler : ICommandHandler<SampleArchitectureCommand, string>
    {
        public Task<Result<string>> HandleAsync(SampleArchitectureCommand command, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<string>.Success("ok"));
    }

    private sealed class NoOpNoteRepository : INoteRepository
    {
        public Task<Result<StoreReadinessStatus>> CheckReadinessAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result<StoreReadinessStatus>.Success(new StoreReadinessStatus(true, "mongodb://127.0.0.1:27017", "rs0", 1.0)));

        public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
            Func<INoteTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<Note>> GetByIdAsync(string ownerId, Guid noteId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<NoteSearchHit>>> SearchAsync(NoteSearchCriteria criteria, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<Note>>> GetImportedBySourceFileAsync(string ownerId, string sourceFileId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<NoteRevision>>> GetRevisionsAsync(string ownerId, Guid noteId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<RoleRadar.Domain.Audit.AuditLogEntry>>> GetAuditEntriesAsync(string ownerId, Guid? noteId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
