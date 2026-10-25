using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Infrastructure;
using RoleRadar.Mcp.Stdio.Filters;
using RoleRadar.Mcp.Stdio.Tools;

namespace RoleRadar.Mcp.Stdio;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Route all diagnostic logging strictly to stderr so stdout carries exclusively JSON-RPC 2.0 protocol frames (FR-020).
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            var connectionString = RoleRadar.Infrastructure.Mongo.MongoConnectionStringSource.Resolve(Environment.GetEnvironmentVariable);
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
                .WithTools<NoteMcpTools>()
                .WithTools<TrackingMcpTools>();

            builder.Services.Configure<McpServerOptions>(options =>
            {
                options.Filters.Request.CallToolFilters.Add(UnknownArgumentValidationFilter.Create());
                options.Filters.Request.ListToolsFilters.Add(ToolSchemaNormalizationFilter.Create());
            });

            var app = builder.Build();

            // Eagerly resolve IMongoClient before starting transport to catch invalid connection strings without partial JSON-RPC
            app.Services.GetRequiredService<IMongoClient>();

            await app.RunAsync();
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message == RoleRadar.Infrastructure.Mongo.ConnectionStringRedactor.InvalidConnectionStringErrorMessage)
        {
            await Console.Error.WriteLineAsync(RoleRadar.Infrastructure.Mongo.ConnectionStringRedactor.InvalidConnectionStringErrorMessage);
            return 1;
        }
    }
}
