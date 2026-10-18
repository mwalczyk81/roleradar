using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var isVerbose = args.Any(a => string.Equals(a, "--verbose", StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase));

            await using var services = BuildServiceProvider(args, isVerbose: isVerbose);

            var rootCommand = new RootCommand("RoleRadar local CLI")
            {
                StatusCommand.Create(services),
                ImportCommand.Create(services),
                BackupCommand.Create(services),
                RestoreCommand.Create(services),
                WipeCommand.Create(services)
            };

            var parseResult = rootCommand.Parse(args);

            if (parseResult.Errors.Count == 0
                && parseResult.Action is not System.CommandLine.Help.HelpAction
                && parseResult.Action?.GetType().Name != "VersionOptionAction"
                && parseResult.CommandResult.Command != rootCommand)
            {
                // Resolve IMongoClient lazily only when a command that needs it runs
                services.GetRequiredService<IMongoClient>();
            }

            return await parseResult.InvokeAsync(cancellationToken: CancellationToken.None);
        }
        catch (InvalidOperationException ex) when (ex.Message == ConnectionStringRedactor.InvalidConnectionStringErrorMessage)
        {
            Console.Error.WriteLine(ConnectionStringRedactor.InvalidConnectionStringErrorMessage);
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
            logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });
        });

        var connectionString = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING");
        var databaseName = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");
        var backupDumpCommand = Environment.GetEnvironmentVariable("ROLERADAR_BACKUP_DUMP_COMMAND");
        var backupRestoreCommand = Environment.GetEnvironmentVariable("ROLERADAR_BACKUP_RESTORE_COMMAND");

        services.AddRoleRadarApplication();
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
