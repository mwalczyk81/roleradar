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
        services.AddSingleton<ITrackingTransactionContext>(new NoOpTrackingTransactionContext());
        services.AddRoleRadarApplication();

        using var provider = services.BuildServiceProvider();

        // Verify the open/helper decorator registration pipeline wraps any ICommandHandler<,>
        // with LoggingCommandDecorator<,> (outer) -> TransactionAndAuditCommandDecorator<,> -> Inner Handler
        var testServices = new ServiceCollection();
        testServices.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        testServices.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        testServices.AddSingleton<INoteRepository>(new NoOpNoteRepository());
        testServices.AddSingleton<ITrackingTransactionContext>(new NoOpTrackingTransactionContext());
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

    [Fact]
    public void AuditOperation_Allowlist_MustIncludeAllRecognizedTrackerImportOperations()
    {
        string[] expectedImportOperations =
        [
            "import_posting_create",
            "import_posting_transition",
            "import_posting_signal_append",
            "import_company_create",
            "import_company_update",
            "import_application_create",
            "import_application_event_append",
            "import_criterion_revision"
        ];

        var auditType = typeof(RoleRadar.Domain.Audit.AuditOperation);
        var constFields = auditType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetValue(null)!);

        foreach (var expected in expectedImportOperations)
        {
            Assert.Contains(expected, constFields.Values);
            Assert.True(RoleRadar.Domain.Audit.AuditOperation.IsValid(expected), $"AuditOperation.IsValid must return true for {expected}");
        }

        Assert.False(RoleRadar.Domain.Audit.AuditOperation.IsValid("unknown_operation"));
        Assert.False(RoleRadar.Domain.Audit.AuditOperation.IsValid(""));
    }

    [Fact]
    public void Domain_MustNotContain_FileFormatOrCsvTypes()
    {
        var domainTypes = DomainAssembly.GetTypes();

        var violatingTypes = domainTypes
            .Where(t => t.Name.Contains("Csv", StringComparison.OrdinalIgnoreCase) ||
                        t.Name.Contains("Json", StringComparison.OrdinalIgnoreCase) ||
                        (t.Namespace is not null && (t.Namespace.Contains(".Csv", StringComparison.OrdinalIgnoreCase) ||
                                                    t.Namespace.Contains(".Json", StringComparison.OrdinalIgnoreCase))))
            .Select(t => t.FullName)
            .ToArray();

        Assert.Empty(violatingTypes);
    }

    [Fact]
    public void MongoNoteRepository_MustTouchRevisionsAndAuditCollections_ExclusivelyThrough_AppendOnlyMongoCollection()
    {
        // 1. Verify AppendOnlyMongoCollection exposes ONLY insert and find operations (no delete, update, replace, or bulk-write)
        var wrapperMethods = typeof(AppendOnlyMongoCollection)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToArray();

        Assert.All(wrapperMethods, name =>
        {
            Assert.False(name.Contains("Delete", StringComparison.OrdinalIgnoreCase), $"AppendOnlyMongoCollection must not expose {name}");
            Assert.False(name.Contains("Remove", StringComparison.OrdinalIgnoreCase), $"AppendOnlyMongoCollection must not expose {name}");
            Assert.False(name.Contains("Update", StringComparison.OrdinalIgnoreCase), $"AppendOnlyMongoCollection must not expose {name}");
            Assert.False(name.Contains("Replace", StringComparison.OrdinalIgnoreCase), $"AppendOnlyMongoCollection must not expose {name}");
            Assert.False(name.Contains("Bulk", StringComparison.OrdinalIgnoreCase), $"AppendOnlyMongoCollection must not expose {name}");
            Assert.False(name.Contains("Drop", StringComparison.OrdinalIgnoreCase), $"AppendOnlyMongoCollection must not expose {name}");
        });

        // 2. Verify all authored fields and properties on MongoNoteRepository (and its authored nested types) that touch revisions or audits
        // have type AppendOnlyMongoCollection, and no other IMongoCollection<BsonDocument> member exists besides NotesCollection.
        var allRepoTypes = new[] { typeof(MongoNoteRepository) }
            .Concat(typeof(MongoNoteRepository).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(nt => !nt.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)))
            .ToArray();

        var allFieldsAndProps = allRepoTypes
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(f => (DeclaringType: t, MemberName: f.Name, MemberType: f.FieldType))
                .Concat(t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(p => (DeclaringType: t, MemberName: p.Name, MemberType: p.PropertyType))))
            .ToArray();

        foreach (var member in allFieldsAndProps)
        {
            if (member.MemberName.Contains("Revision", StringComparison.OrdinalIgnoreCase) ||
                member.MemberName.Contains("Audit", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Equal(typeof(AppendOnlyMongoCollection), member.MemberType);
            }

            if (member.MemberType.IsGenericType &&
                member.MemberType.GetGenericTypeDefinition().Name.StartsWith("IMongoCollection", StringComparison.Ordinal))
            {
                Assert.Equal("NotesCollection", member.MemberName);
            }
        }

        // 3. Inspect IL of all methods (excluding constructors) in MongoNoteRepository and its nested types to ensure
        // IMongoDatabase.GetCollection is only called inside get_NotesCollection, never to obtain raw access to note_revisions or audit_log.
        var allMethods = allRepoTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .ToArray();

        foreach (var method in allMethods)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null)
            {
                continue;
            }

            var referencedStringLiterals = ExtractUserStringsFromMethodBody(method.Module, il);
            foreach (var literal in referencedStringLiterals)
            {
                Assert.NotEqual(MongoBootstrapAndIndexes.NoteRevisionsCollectionName, literal);
                Assert.NotEqual(MongoBootstrapAndIndexes.AuditLogCollectionName, literal);
            }
        }
    }

    [Fact]
    public void DependencyInjection_AddRoleRadarInfrastructure_ResolvesMongoNoteRepository_WithNoFaultInjectionHookSet()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        RoleRadar.Infrastructure.DependencyInjection.AddRoleRadarInfrastructure(
            services,
            opts =>
            {
                opts.ConnectionString = "mongodb://127.0.0.1:27017/?replicaSet=rs0";
                opts.DatabaseName = "roleradar_arch_test";
            });

        using var provider = services.BuildServiceProvider();
        var repo = Assert.IsType<MongoNoteRepository>(provider.GetRequiredService<INoteRepository>());
        Assert.False(repo.HasFaultInjectionHook, "DI-built MongoNoteRepository must not have a fault-injection hook set.");
    }

    [Fact]
    public async Task PreCommitConfig_MustExcludeTestFixturesFromMutatingHooks_AndPreserveFixtureBytes()
    {
        var repoRoot = LocateRepositoryRoot();
        var preCommitConfigPath = Path.Combine(repoRoot, ".pre-commit-config.yaml");
        Assert.True(File.Exists(preCommitConfigPath), $"Expected .pre-commit-config.yaml at {preCommitConfigPath}");

        var configContent = await File.ReadAllTextAsync(preCommitConfigPath);
        Assert.Contains("id: trailing-whitespace", configContent, StringComparison.Ordinal);
        Assert.Contains("id: end-of-file-fixer", configContent, StringComparison.Ordinal);
        Assert.Contains("id: mixed-line-ending", configContent, StringComparison.Ordinal);
        Assert.Contains("id: markdownlint-cli2", configContent, StringComparison.Ordinal);
        Assert.Contains("exclude: (^|/)fixtures/", configContent, StringComparison.Ordinal);

        var markdownLintConfigPath = Path.Combine(repoRoot, ".markdownlint-cli2.yaml");
        Assert.True(File.Exists(markdownLintConfigPath), $"Expected .markdownlint-cli2.yaml at {markdownLintConfigPath}");
        var markdownLintConfig = await File.ReadAllTextAsync(markdownLintConfigPath);
        Assert.Contains("tests/**/fixtures/**", markdownLintConfig, StringComparison.Ordinal);

        var fixturesDir = Path.Combine(repoRoot, "tests", "fixtures");
        var primaryFixturePath = Path.Combine(fixturesDir, "fictional-memory-primary.md");
        var archiveFixturePath = Path.Combine(fixturesDir, "fictional-memory-archive.md");
        Assert.True(File.Exists(primaryFixturePath));
        Assert.True(File.Exists(archiveFixturePath));

        var primaryBytes = await File.ReadAllBytesAsync(primaryFixturePath);
        Assert.Contains((byte)'\r', primaryBytes);
    }

    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")) &&
                File.Exists(Path.Combine(dir.FullName, ".pre-commit-config.yaml")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Could not locate repository root from {AppContext.BaseDirectory}.");
    }

    private static List<string> ExtractUserStringsFromMethodBody(Module module, byte[] il)
    {
        var strings = new List<string>();
        for (var i = 0; i < il.Length - 4; i++)
        {
            // OpCodes.Ldstr == 0x72 followed by 4-byte metadata token (0x70xxxxxx for UserString)
            if (il[i] == 0x72 && il[i + 4] == 0x70)
            {
                var token = BitConverter.ToInt32(il, i + 1);
                try
                {
                    strings.Add(module.ResolveString(token));
                    i += 4;
                }
                catch
                {
                    // Not a valid string token at this alignment; continue scanning.
                }
            }
        }

        return strings;
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

        public Task<Result<IReadOnlyList<Domain.Audit.AuditLogEntry>>> GetAuditEntriesAsync(string ownerId, Guid? noteId = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpTrackingTransactionContext : ITrackingTransactionContext
    {
        public ICompanyRepository Companies => throw new NotSupportedException();
        public IPostingRepository Postings => throw new NotSupportedException();
        public ICriterionRepository Criteria => throw new NotSupportedException();
        public IApplicationRepository Applications => throw new NotSupportedException();
        public IContactRepository Contacts => throw new NotSupportedException();
        public ISearchRunRepository SearchRuns => throw new NotSupportedException();
        public Task AppendAuditEntryAsync(Domain.Audit.AuditLogEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
