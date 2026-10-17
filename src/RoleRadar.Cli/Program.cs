using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;

namespace RoleRadar.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        await using var services = BuildServiceProvider();

        var rootCommand = new RootCommand("RoleRadar local CLI");
        rootCommand.Add(ImportCommand.Create(services));

        return await rootCommand.Parse(args).InvokeAsync(cancellationToken: CancellationToken.None);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        var connectionString = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING");
        var databaseName = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");

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

        return services.BuildServiceProvider();
    }
}
