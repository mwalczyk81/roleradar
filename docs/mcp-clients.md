# MCP Client Registration

The server speaks MCP over stdio only and opens no network listener ([ADR 0003](adr/0003-stdio-first-mcp.md)). The tools are listed in [mcp-tools.md](mcp-tools.md).

Build the MCP stdio server binaries:

```powershell
dotnet publish src/RoleRadar.Mcp.Stdio -c Release -o ./dist/mcp
```

The compiled binary will be located at `./dist/mcp/RoleRadar.Mcp.Stdio.exe` (or `RoleRadar.Mcp.Stdio.dll` run via `dotnet`).

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
