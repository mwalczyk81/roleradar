using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Application;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Backup;

namespace RoleRadar.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        await using var services = BuildServiceProvider();

        var rootCommand = new RootCommand("RoleRadar local CLI");
        rootCommand.Add(ImportCommand.Create(services));
        rootCommand.Add(BackupCommand.Create(services));
        rootCommand.Add(RestoreCommand.Create(services));
        rootCommand.Add(WipeCommand.Create(services));

        return await rootCommand.Parse(args).InvokeAsync(cancellationToken: CancellationToken.None);
    }

    public static ServiceProvider BuildServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
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
}
