using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Spike: can a Claude client (Desktop, Cowork scheduled task, Claude Code) launch this
// stdio MCP server and call a tool? stdout is reserved for the protocol, so all logs go to stderr.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<EchoTools>();

await builder.Build().RunAsync();

[McpServerToolType]
public sealed class EchoTools
{
    [McpServerTool(Name = "echo"), Description("Returns the message plus a UTC timestamp and the process id. Used to prove a client can launch this server.")]
    public static string Echo([Description("Any text")] string message) =>
        $"roleradar echo: {message} | utc={DateTime.UtcNow:O} | pid={Environment.ProcessId} | user={Environment.UserName}";
}
