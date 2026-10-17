using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;

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

            // Eagerly resolve IMongoClient to catch invalid connection strings before command dispatch
            services.GetRequiredService<IMongoClient>();

            var rootCommand = new RootCommand("RoleRadar local CLI");
            rootCommand.Add(StatusCommand.Create(services));
            rootCommand.Add(ImportCommand.Create(services));
            rootCommand.Add(BackupCommand.Create(services));
            rootCommand.Add(RestoreCommand.Create(services));
            rootCommand.Add(WipeCommand.Create(services));

            return await rootCommand.Parse(args).InvokeAsync(cancellationToken: CancellationToken.None);
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
