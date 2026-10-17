# Spike: can Claude launch a local stdio MCP server?

Question this answers: can Claude Desktop, a Cowork scheduled task and Claude Code launch a locally installed stdio MCP server and call a tool? The answer decides whether the weekly job talks to RoleRadar over stdio (no network listener) or over localhost HTTP with login.

Protocol-level smoke test already passes: `initialize`, `tools/list` and `tools/call echo` all return correct JSON-RPC over stdin/stdout.

## Build and publish

```powershell
dotnet publish spikes/StdioEcho -c Release -o "$env:LOCALAPPDATA\roleradar\stdio-echo"
```

Executable: `%LOCALAPPDATA%\roleradar\stdio-echo\roleradar-stdio-echo.exe`

## Test 1: Claude Code (no restart needed)

```powershell
claude mcp add roleradar-echo -- "$env:LOCALAPPDATA\roleradar\stdio-echo\roleradar-stdio-echo.exe"
```

Start a new Claude Code session and ask it to call the `echo` tool. Remove later with `claude mcp remove roleradar-echo`.

## Test 2: Claude Desktop

1. Quit Claude Desktop completely (tray icon included). The app rewrites its config while running and clobbers edits.
2. Add this entry under `mcpServers` in `%APPDATA%\Claude\claude_desktop_config.json`, keeping your existing servers:

```json
"roleradar-echo": {
  "command": "C:\\Users\\mwalc\\AppData\\Local\\roleradar\\stdio-echo\\roleradar-stdio-echo.exe",
  "args": []
}
```

1. Start Desktop and ask it to call `echo`.

## Test 3: Cowork scheduled task (the one that matters)

Create a one-time or short-interval Cowork scheduled task with the prompt: "Call the roleradar-echo `echo` tool with the message 'scheduled run' and report the output verbatim."

Pass criteria: the run output contains `roleradar echo: scheduled run | utc=... | pid=... | user=...`. Compare the `pid` across runs to confirm a fresh process each time.

## Reading the result

| Result | Consequence |
| --- | --- |
| Works in Code, Desktop and the scheduled task | Phase 1 is stdio only |
| Works in Code and Desktop, fails in the scheduled task | Weekly job needs localhost HTTP, so Duende comes before the weekly job moves |
| Fails everywhere | Check the stderr log in the client, confirm the path, run the exe by hand |

stdout belongs to the protocol. All logging goes to stderr, so never `Console.WriteLine` in a stdio server.
