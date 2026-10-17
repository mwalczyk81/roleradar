# ADR 0004: Verified `ModelContextProtocol` 2.2.0 SDK Usage

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

Constitution Principle VIII requires pinning all NuGet package versions and recording library decisions in MADR format. In `spikes/StdioEcho`, we verified the official C# MCP SDK on .NET 10. We need to record the exact package version and API signatures to prevent version drift or hallucinated SDK APIs during implementation.

## Decision Outcome

Pin `ModelContextProtocol` at version **`2.2.0`** and `Microsoft.Extensions.Hosting` at **`10.0.0`** in `Directory.Packages.props`, using the verified hosting and attribute signatures from `spikes/StdioEcho`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<NoteMcpTools>();

await builder.Build().RunAsync();
```

And tool definitions use:
- `[McpServerToolType]` on the tool class (`NoteMcpTools`)
- `[McpServerTool(Name = "note_create"), Description("...")]` on each tool method (with DI services injected as method parameters or constructor parameters)
- `[Description("...")]` on tool parameters.

### Consequences

- **Good**: Proven compatibility with `net10.0` and zero ambiguity around tool registration or `stderr` logging configuration.
- **Bad / Trade-off**: Upgrading `ModelContextProtocol` beyond `2.2.0` requires an explicit Dependabot/PR update and re-running the stdout-hygiene and protocol smoke tests.
