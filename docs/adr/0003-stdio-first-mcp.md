# ADR 0003: Stdio-First MCP Host Without Network Listeners

- **Status**: Accepted
- **Date**: 2026-10-17
- **Feature**: `001-memory-replacement`

## Context and Problem Statement

RoleRadar provides an AI agent with durable note tools via the Model Context Protocol (MCP). A server can expose MCP over standard input/output (`stdio`) launched as a child process by the client (Claude Code, Claude Desktop, Cowork scheduled tasks) or over HTTP/SSE on a network port.

Which transport should Feature 001 use?

## Decision Drivers

- **Constitution Principle VI (*stdio Protocol Hygiene*)**: The stdio host must not open or bind any network listener, `stdout` must carry exclusively MCP protocol frames, and all logs must go to `stderr`.
- **Zero-Auth Local Security**: Operating over `stdio` as a child process inherits the local OS user's boundary without exposing an unauthenticated localhost HTTP port before OIDC/Duende IdentityServer arrives in Feature 004.

## Considered Options

1. **Stdio-only MCP host (`RoleRadar.Mcp.Stdio`) with zero network listeners and `stderr`-only logging**.
2. **Localhost HTTP/SSE MCP listener**.

## Decision Outcome

Chosen option: **Option 1 — Stdio-only MCP host**, verified by the early `spikes/StdioEcho` prototype (which proved ModelContextProtocol 2.2.0 stdio framing on net10.0 with stderr-only logging, and was removed prior to release) and enforced by an automated stdout-hygiene integration test that launches `RoleRadar.Mcp.Stdio`, executes MCP requests + invalid error requests, and asserts every line emitted on `stdout` is a valid JSON-RPC 2.0 frame.

### Consequences

- **Good**: Zero local network attack surface; no authentication bypasses or static tokens needed in Feature 001.
- **Good**: Works directly with Claude Code, Claude Desktop, and local process invocation.
- **Bad / Trade-off**: Any accidental `Console.WriteLine` or default `ILogger` stdout provider would corrupt the JSON-RPC stream, so stdout-hygiene testing is mandatory in CI.
