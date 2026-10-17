using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RoleRadar.Application;
using RoleRadar.Infrastructure;
using RoleRadar.Mcp.Stdio.Tools;

namespace RoleRadar.Mcp.Stdio;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // Route all diagnostic logging strictly to stderr so stdout carries exclusively JSON-RPC 2.0 protocol frames (FR-020).
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options =>
        {
            options.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        var connectionString = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING");
        var databaseName = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");

        builder.Services.AddRoleRadarApplication();
        builder.Services.AddRoleRadarInfrastructure(options =>
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

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<NoteMcpTools>();

        var app = builder.Build();
        await app.RunAsync();
        return 0;
    }
}
