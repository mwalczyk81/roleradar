using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Observability;

namespace RoleRadar.Cli;

public static class Program
{
    /// <summary>
    /// The trace id of this CLI run (feature 013 run id, feature 014 W3C trace id: 32 lower-case hex characters), shared by every
    /// log record and audit entry the run writes.
    /// </summary>
    public static string RunId { get; } = TraceIds.NewRunId();

    public static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        using var sigtermReg = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGTERM, _ => cts.Cancel())
            : null;
        using var sigintReg = OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? System.Runtime.InteropServices.PosixSignalRegistration.Create(System.Runtime.InteropServices.PosixSignal.SIGINT, _ => cts.Cancel())
            : null;

        try
        {
            var isVerbose = args.Any(a => string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase));

            await using var services = BuildServiceProvider(args, isVerbose: isVerbose);

            // One trace id for the whole run: an activity that joins the trace RunId names, so every record this process writes
            // carries it (the console formatter reads Activity.Current).
            using var runActivity = TraceIds.StartActivity("roleradar.cli.run", RunId);

            var rootCommand = new RootCommand("RoleRadar local CLI");
            rootCommand.Add(StatusCommand.Create(services));
            rootCommand.Add(ImportCommand.Create(services));
            rootCommand.Add(BackupCommand.Create(services));
            rootCommand.Add(RestoreCommand.Create(services));
            rootCommand.Add(WipeCommand.Create(services));
            rootCommand.Add(ContactCommand.Create(services));
            rootCommand.Add(AuditCommand.Create(services));
            rootCommand.Add(TrackerImportCommand.Create(services));
            rootCommand.Add(VerifyCommand.Create(services));
            rootCommand.Add(ReconcileReadModelsCommand.Create(services));
            rootCommand.Add(RoleRadar.Cli.Commands.Identity.IdentityCommand.Create(services));
            rootCommand.Add(RoleRadar.Cli.Demo.DemoCommand.Create(services));

            var parseResult = rootCommand.Parse(args);

            if (parseResult.Errors.Count == 0
                && parseResult.Action is not System.CommandLine.Help.HelpAction
                && parseResult.Action?.GetType().Name != "VersionOptionAction"
                && parseResult.CommandResult.Command != rootCommand)
            {
                // Resolve IMongoClient lazily only when a command that needs it runs
                services.GetRequiredService<IMongoClient>();
            }

            return await parseResult.InvokeAsync(cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return 143;
        }
        catch (InvalidOperationException ex) when (ex.Message == RoleRadar.Infrastructure.Mongo.ConnectionStringRedactor.InvalidConnectionStringErrorMessage)
        {
            Console.Error.WriteLine(RoleRadar.Infrastructure.Mongo.ConnectionStringRedactor.InvalidConnectionStringErrorMessage);
            return 1;
        }
    }

    public static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null) =>
        BuildServiceProvider(args: null, isVerbose: false, configure: configure);

    public static ServiceProvider BuildServiceProvider(
        string[]? args,
        bool isVerbose = false,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        var logLevel = ResolveLogLevel(args, isVerbose);

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(logLevel);
            logging.AddRoleRadarLogging("cli", LogStream.StdErr);
        });

        var connectionString = RoleRadar.Infrastructure.Mongo.MongoConnectionStringSource.Resolve(Environment.GetEnvironmentVariable);
        var databaseName = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");
        var backupDumpCommand = Environment.GetEnvironmentVariable("ROLERADAR_BACKUP_DUMP_COMMAND");
        var backupRestoreCommand = Environment.GetEnvironmentVariable("ROLERADAR_BACKUP_RESTORE_COMMAND");

        services.AddRoleRadarApplication();
        services.Replace(ServiceDescriptor.Scoped<RoleRadar.Application.Abstractions.IRequestContext, RunRequestContext>());
        services.AddRoleRadarInfrastructure(options =>
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.ConnectionString = connectionString.Trim();
            }

            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                options.DatabaseName = databaseName.Trim();
            }
        });

        services.Configure<BackupOptions>(options =>
        {
            if (!string.IsNullOrWhiteSpace(backupDumpCommand))
            {
                options.DumpCommand = backupDumpCommand.Trim();
            }

            if (!string.IsNullOrWhiteSpace(backupRestoreCommand))
            {
                options.RestoreCommand = backupRestoreCommand.Trim();
            }
        });

        var identityDb = Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_DB");
        var identityConnectionString = !string.IsNullOrWhiteSpace(identityDb)
            ? identityDb.Trim()
            : "Data Source=identity.db";

        services.AddDbContext<RoleRadar.Identity.Data.IdentityAppDbContext>(options =>
            Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(options, identityConnectionString));

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static LogLevel ResolveLogLevel(string[]? args, bool isVerbose)
    {
        // 1. Explicit --verbose / -v flag raises log level to Information (or Debug)
        if (isVerbose || (args != null && args.Any(a => string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
                                                     || string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase))))
        {
            return LogLevel.Information;
        }

        // 2. ROLERADAR_LOG_LEVEL environment variable override
        var envLevel = Environment.GetEnvironmentVariable("ROLERADAR_LOG_LEVEL");
        if (!string.IsNullOrWhiteSpace(envLevel) && Enum.TryParse<LogLevel>(envLevel.Trim(), ignoreCase: true, out var parsedLevel))
        {
            return parsedLevel;
        }

        // 3. Default to Warning so status and import --dry-run do not print info logs
        return LogLevel.Warning;
    }
}
