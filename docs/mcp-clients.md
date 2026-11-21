# MCP Client Registration

The server speaks MCP over stdio only and opens no network listener ([ADR 0003](adr/0003-stdio-first-mcp.md)). The tools are listed in [mcp-tools.md](mcp-tools.md).

Build the MCP stdio server binaries:

```powershell
dotnet publish src/RoleRadar.Mcp.Stdio -c Release -o ./dist/mcp
```

The compiled binary will be located at `./dist/mcp/RoleRadar.Mcp.Stdio.exe` (or `RoleRadar.Mcp.Stdio.dll` run via `dotnet`).

## Keep the published build current

Clients run the published binary, not the working tree. After any change that touches a derived stored field (`company_name`, `disposition_rank`, `has_*` flags, milestones), a repository write path or a tool schema, republish and restart the client. A stale build keeps writing rows without the new fields, and the Insights page then shows "Some numbers need a one-time backfill" again.

1. Publish to a new folder, because a running server locks the DLLs in the old one: `dotnet publish src/RoleRadar.Mcp.Stdio -c Release -o ./dist/mcp-<short-sha>`.
2. Point the client config at the new `RoleRadar.Mcp.Stdio.dll` and restart the client (Claude desktop reads its config only at start).
3. Check the build: search `RoleRadar.Infrastructure.dll` in the new folder for the field names as UTF-16 text (a plain-text search finds nothing, because .NET stores string literals as UTF-16). For example `python -I -c "print(open('dist/mcp-<short-sha>/RoleRadar.Infrastructure.dll','rb').read().count('has_application'.encode('utf-16-le')))"` must print more than 0.
4. Run `reconcile-read-models` once, by the owner, to backfill rows written by the old build ([guide](operations/reconcile-read-models.md)).
5. Delete the old `dist/mcp*` folder when nothing runs from it.

## Claude Code CLI

Register RoleRadar into Claude Code (the target path must be absolute because Claude Code launches MCP servers from arbitrary working directories):

```powershell
claude mcp add roleradar -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true" -e ROLERADAR_MONGO_DATABASE="roleradar" -- dotnet C:/Users/<username>/source/repos/roleradar/dist/mcp/RoleRadar.Mcp.Stdio.dll
```

Or using the published Windows executable directly:

```powershell
claude mcp add roleradar -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true" -e ROLERADAR_MONGO_DATABASE="roleradar" -- C:/Users/<username>/source/repos/roleradar/dist/mcp/RoleRadar.Mcp.Stdio.exe
```

## Claude Desktop

Add RoleRadar to your Claude Desktop configuration (`%APPDATA%\Claude\claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "roleradar": {
      "command": "dotnet",
      "args": [
        "C:/Users/<username>/source/repos/roleradar/dist/mcp/RoleRadar.Mcp.Stdio.dll"
      ],
      "env": {
        "ROLERADAR_MONGO_CONNECTION_STRING": "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true",
        "ROLERADAR_MONGO_DATABASE": "roleradar"
      }
    }
  }
}
```

## Cowork / Scheduled Task

To launch RoleRadar in headless agentic workflows or scheduled background automation, execute:

```powershell
$env:ROLERADAR_MONGO_CONNECTION_STRING = "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true"
$env:ROLERADAR_MONGO_DATABASE = "roleradar"
dotnet C:/Users/<username>/source/repos/roleradar/dist/mcp/RoleRadar.Mcp.Stdio.dll
```

Standard input/output will process JSON-RPC MCP messages, while diagnostic logs are routed exclusively to standard error to prevent protocol stream corruption.
