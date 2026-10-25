using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure;
using RoleRadar.Mcp.Stdio.Tools;
using Xunit;

namespace RoleRadar.Tests.Architecture;

public sealed class TrackingArchitectureTests
{
    [Fact]
    public void McpToolSurface_TotalToolsMustBeExactly12()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var toolMethods = mcpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToArray();

        Assert.Equal(12, toolMethods.Length);
    }

    [Fact]
    public void RepositoriesAndCliAndTools_MustNotExpose_DeleteOrRemoveNames()
    {
        var trackingRepoInterfaces = new[]
        {
            typeof(ICompanyRepository),
            typeof(IPostingRepository),
            typeof(ICriterionRepository),
            typeof(IApplicationRepository),
            typeof(IContactRepository),
            typeof(ISearchRunRepository),
            typeof(ITrackingTransactionContext)
        };

        foreach (var repoType in trackingRepoInterfaces)
        {
            var methods = repoType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var m in methods)
            {
                Assert.False(
                    m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Contains("Remove", StringComparison.OrdinalIgnoreCase) ||
                    m.Name.Contains("Drop", StringComparison.OrdinalIgnoreCase),
                    $"Tracking repository interface '{repoType.Name}' must not expose delete method '{m.Name}'.");
            }
        }

        // Assert 0 delete/remove MCP tool names
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var toolNames = mcpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(n => n is not null)
            .ToArray();

        foreach (var name in toolNames)
        {
            Assert.DoesNotContain("delete", name!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("remove", name!, StringComparison.OrdinalIgnoreCase);
        }

        // Assert 0 delete/remove CLI command names across tracking
        var cliAssembly = typeof(RoleRadar.Cli.Program).Assembly;
        var cliTypes = cliAssembly.GetTypes();
        foreach (var t in cliTypes)
        {
            if (t.Name.EndsWith("Command", StringComparison.Ordinal) && !t.Name.Equals("WipeCommand", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("delete", t.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("remove", t.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Application_CommandHandlers_AreAllDecoratedWithTransactionAndAudit()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = "mongodb://127.0.0.1:27017/?replicaSet=rs0";
            opts.DatabaseName = "arch_test_db";
        });

        using var provider = services.BuildServiceProvider();

        // Check each command handler registration
        var appAssembly = typeof(RoleRadar.Application.DependencyInjection).Assembly;
        var commandTypes = appAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
            .ToArray();

        foreach (var cmdType in commandTypes)
        {
            var commandInterface = cmdType.GetInterfaces().First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
            var responseType = commandInterface.GetGenericArguments()[0];
            var handlerServiceType = typeof(ICommandHandler<,>).MakeGenericType(cmdType, responseType);

            var resolvedHandler = provider.GetService(handlerServiceType);
            Assert.NotNull(resolvedHandler);

            // Handler should be wrapped by LoggingCommandDecorator wrapping TransactionAndAuditCommandDecorator
            var handlerType = resolvedHandler.GetType();
            Assert.Equal(typeof(RoleRadar.Application.Decorators.LoggingCommandDecorator<,>), handlerType.GetGenericTypeDefinition());

            var innerField = handlerType.GetField("_inner", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(innerField);
            var innerHandler = innerField.GetValue(resolvedHandler);
            Assert.NotNull(innerHandler);
            Assert.Equal(typeof(RoleRadar.Application.Decorators.TransactionAndAuditCommandDecorator<,>), innerHandler.GetType().GetGenericTypeDefinition());
        }
    }

    [Fact]
    public void McpTools_MustZeroExpose_ContactTypesOrParameters()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var mcpTypes = mcpAssembly.GetTypes();

        foreach (var t in mcpTypes)
        {
            Assert.DoesNotContain("Contact", t.Name, StringComparison.OrdinalIgnoreCase);

            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var m in methods)
            {
                if (m.GetCustomAttribute<McpServerToolAttribute>() is not null)
                {
                    Assert.DoesNotContain("contact", m.Name, StringComparison.OrdinalIgnoreCase);
                    var parameters = m.GetParameters();
                    foreach (var p in parameters)
                    {
                        Assert.False(
                            p.ParameterType.Name.Contains("Contact", StringComparison.OrdinalIgnoreCase) &&
                            !p.ParameterType.Name.Contains("Context", StringComparison.OrdinalIgnoreCase),
                            $"MCP tool '{m.Name}' must not accept contact parameter '{p.Name}'.");
                    }
                }
            }
        }
    }

    [Fact]
    public void Infrastructure_AddRoleRadarInfrastructure_RegistersICompanyRepository()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = "mongodb://127.0.0.1:27017/?replicaSet=rs0";
            opts.DatabaseName = "roleradar_arch_test";
        });

        using var provider = services.BuildServiceProvider();
        var companyRepo = provider.GetService<ICompanyRepository>();
        var postingRepo = provider.GetService<IPostingRepository>();
        var criterionRepo = provider.GetService<ICriterionRepository>();
        var applicationRepo = provider.GetService<IApplicationRepository>();
        var contactRepo = provider.GetService<IContactRepository>();
        var searchRunRepo = provider.GetService<ISearchRunRepository>();
        var integrityRepo = provider.GetService<RoleRadar.Application.Integrity.IIntegrityRepository>();
        Assert.NotNull(companyRepo);
        Assert.NotNull(postingRepo);
        Assert.NotNull(criterionRepo);
        Assert.NotNull(applicationRepo);
        Assert.NotNull(contactRepo);
        Assert.NotNull(searchRunRepo);
        Assert.NotNull(integrityRepo);
    }

    [Fact]
    public void McpTools_PostingTools_AreExposedOnServer()
    {
        var mcpAssembly = typeof(NoteMcpTools).Assembly;
        var toolNames = mcpAssembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.Contains("company_record", toolNames);
        Assert.Contains("posting_record", toolNames);
        Assert.Contains("posting_transition", toolNames);
        Assert.Contains("tracker_query", toolNames);
        Assert.Contains("tracker_log", toolNames);
        Assert.Contains("application_record", toolNames);
    }

    [Fact]
    public void TrackingHandlers_MustNotCallNoteAuditFactoryMethods()
    {
        var appAssembly = typeof(RoleRadar.Application.Tracking.Commands.RecordCompanyCommand).Assembly;
        var trackingTypes = appAssembly.GetTypes()
            .Where(t => t.Namespace is not null &&
                        t.Namespace.StartsWith("RoleRadar.Application.Tracking", StringComparison.Ordinal) &&
                        t.FullName is not null &&
                        !t.FullName.Contains("ApplicationsStep", StringComparison.Ordinal) &&
                        !t.FullName.Contains("PostingsJsonStep", StringComparison.Ordinal))
            .ToArray();

        var noteFactoryNames = new HashSet<string>
        {
            "Create",
            "CreateForCreate",
            "CreateForAppend",
            "CreateForReplaceSection",
            "CreateForArchive",
            "CreateForImportCreate",
            "CreateForImportOverwrite"
        };

        foreach (var type in trackingTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var m in methods)
            {
                var body = m.GetMethodBody();
                if (body is null) continue;

                // Check method calls in IL instructions
                var il = body.GetILAsByteArray();
                if (il is null) continue;

                // Also check through reflection tokens if method calls exist
                // A clean way using NetArchTest or reflection:
                // Verify that no method in type invokes AuditLogEntry note factory methods
            }
        }

        var trackingMethods = trackingTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Concat(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(nt => nt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))))
            .ToArray();

        var failingCallers = new List<string>();

        foreach (var tm in trackingMethods)
        {
            MethodBody? body;
            try
            {
                body = tm.GetMethodBody();
            }
            catch
            {
                continue;
            }
            if (body is null) continue;
            var il = body.GetILAsByteArray();
            if (il is null) continue;

            var module = tm.Module;
            for (int i = 0; i < il.Length; i++)
            {
                // Call (0x28), Callvirt (0x6F), newobj (0x73)
                if (il[i] is 0x28 or 0x6F or 0x73)
                {
                    if (i + 4 < il.Length)
                    {
                        int token = BitConverter.ToInt32(il, i + 1);
                        try
                        {
                            var resolvedMethod = module.ResolveMethod(token);
                            if (resolvedMethod is not null &&
                                resolvedMethod.DeclaringType == typeof(RoleRadar.Domain.Audit.AuditLogEntry) &&
                                noteFactoryNames.Contains(resolvedMethod.Name))
                            {
                                failingCallers.Add($"{tm.DeclaringType?.Name}.{tm.Name} -> {resolvedMethod.Name}");
                            }
                        }
                        catch
                        {
                            // Ignored if token cannot be resolved
                        }
                    }
                }
            }
        }

        Assert.True(failingCallers.Count == 0,
            $"Tracking handlers directly call forbidden note audit methods:\n{string.Join("\n", failingCallers.Distinct())}\nNon-note entities must use dedicated tracking audit operations.");
    }

    [Fact]
    public void TrackerImportSteps_MustNotPassUnboundedTextToDomainFactories()
    {
        var appAssembly = typeof(RoleRadar.Application.Tracking.Import.TrackerImportFieldLimiter).Assembly;
        var stepTypes = appAssembly.GetTypes()
            .Where(t => t.Namespace == "RoleRadar.Application.Tracking.Import.Pipeline" &&
                        !t.IsAbstract &&
                        !t.IsInterface &&
                        !t.Name.StartsWith('<') &&
                        !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))
            .ToArray();

        var domainFactoryTypes = new[]
        {
            typeof(RoleRadar.Domain.Tracking.Company),
            typeof(RoleRadar.Domain.Tracking.Posting),
            typeof(RoleRadar.Domain.Tracking.Application),
            typeof(RoleRadar.Domain.Tracking.ApplicationEvent),
            typeof(RoleRadar.Domain.Tracking.Criterion),
            typeof(RoleRadar.Domain.Notes.Note)
        };

        var limiterType = typeof(RoleRadar.Application.Tracking.Import.TrackerImportFieldLimiter);

        foreach (var step in stepTypes)
        {
            var methods = step.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Concat(step.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
                    .SelectMany(nt => nt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)));

            bool callsDomainFactory = false;
            bool callsLimiter = false;

            foreach (var m in methods)
            {
                MethodBody? body;
                try { body = m.GetMethodBody(); } catch { continue; }
                if (body is null) continue;
                var il = body.GetILAsByteArray();
                if (il is null) continue;

                var module = m.Module;
                for (int i = 0; i < il.Length; i++)
                {
                    if (il[i] is 0x28 or 0x6F or 0x73 && i + 4 < il.Length)
                    {
                        int token = BitConverter.ToInt32(il, i + 1);
                        try
                        {
                            var resolved = module.ResolveMethod(token);
                            if (resolved is not null)
                            {
                                if (domainFactoryTypes.Contains(resolved.DeclaringType) && resolved.Name.Equals("Create", StringComparison.Ordinal))
                                {
                                    callsDomainFactory = true;
                                }
                                if (resolved.DeclaringType == limiterType)
                                {
                                    callsLimiter = true;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            if (callsDomainFactory)
            {
                Assert.True(callsLimiter,
                    $"Step type '{step.Name}' constructs domain entities via factory methods but does not invoke TrackerImportFieldLimiter to enforce domain length limits.");
            }
        }

        // Also check source files to ensure no raw indexer expressions are passed directly to Create methods
        var srcDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RoleRadar.Application", "Tracking", "Import", "Pipeline"));
        if (Directory.Exists(srcDir))
        {
            var csFiles = Directory.GetFiles(srcDir, "*Step.cs");
            foreach (var file in csFiles)
            {
                var content = File.ReadAllText(file);
                var lines = content.Split('\n');
                bool inCreateCall = false;
                for (int lineNum = 0; lineNum < lines.Length; lineNum++)
                {
                    var line = lines[lineNum].Trim();
                    if (line.Contains(".Create(") && (line.Contains("Company") || line.Contains("Posting") || line.Contains("Application") || line.Contains("Criterion") || line.Contains("Note")))
                    {
                        inCreateCall = true;
                    }

                    if (inCreateCall)
                    {
                        Assert.False(line.Contains("row["),
                            $"File '{Path.GetFileName(file)}' line {lineNum + 1} passes raw CSV indexer expression 'row[...]' directly into domain factory. Mapped free-text fields must be bounded by TrackerImportFieldLimiter first.");
                        Assert.False(line.Contains(".GetString()"),
                            $"File '{Path.GetFileName(file)}' line {lineNum + 1} passes raw JSON property 'GetString()' directly into domain factory. Mapped free-text fields must be bounded by TrackerImportFieldLimiter first.");

                        if (line.Contains(");"))
                        {
                            inCreateCall = false;
                        }
                    }
                }
            }
        }
    }
}
