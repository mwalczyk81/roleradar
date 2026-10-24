# Quickstart: Linux Container Readiness (Clean Machine, Docker Only)

This guide walks through verifying RoleRadar using **Docker only** on a clean machine without requiring a local .NET SDK installation.

---

## 1. Prerequisites

- [Docker Engine](https://docs.docker.com/engine/) with Docker Compose v2 (or Docker Desktop)
- PowerShell 7 (`pwsh`)

---

## 2. Build Container Images (Single Command)

Clone the repository and build both `roleradar-cli` and `roleradar-mcp` container images using the multi-stage targets:

```powershell
# Build the CLI image
docker build --target cli -t roleradar-cli:local .

# Build the Stdio MCP server image
docker build --target mcp -t roleradar-mcp:local .
```

Verify that both images are present:

```powershell
docker images roleradar-*
```

---

## 3. Start Database Services

Start the persistent MongoDB replica set service using Docker Compose:

```powershell
docker compose -f deploy/docker-compose.yml up -d mongo
```

Wait for MongoDB to report healthy:

```powershell
docker compose -f deploy/docker-compose.yml ps mongo
```

---

## 4. Run CLI Operations Inside the Container

Execute RoleRadar CLI commands using the supported hardened runtime configuration (`--read-only`, tmpfs `/tmp`, non-root, dropped capabilities):

### 4.1 Check Store Status

```powershell
docker run --read-only `
  --tmpfs /tmp:rw,noexec,nosuid,size=64m `
  --cap-drop ALL `
  --security-opt no-new-privileges `
  --network roleradar_default `
  -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://mongo:27017/?directConnection=true" `
  -e DOTNET_EnableDiagnostics=0 `
  -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 `
  -e DOTNET_gcServer=0 `
  --rm roleradar-cli:local status
```

Expected output:
```text
RoleRadar Store: Ready
Endpoint:        mongo:27017
Replica Set:     rs0
```

### 4.2 Create an Encrypted Backup

Mount a local directory for backup output:

```powershell
New-Item -ItemType Directory -Force -Path ./backups

"fictional-sample-passphrase" | docker run --read-only `
  --tmpfs /tmp:rw,noexec,nosuid,size=64m `
  --cap-drop ALL `
  --security-opt no-new-privileges `
  --network roleradar_default `
  -v "${PWD}/backups:/data/backup:rw" `
  -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://mongo:27017/?directConnection=true" `
  -e DOTNET_EnableDiagnostics=0 `
  -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 `
  -e DOTNET_gcServer=0 `
  -i --rm roleradar-cli:local backup /data/backup --passphrase-stdin
```

Verify that an encrypted `.rrbak` archive is generated in `./backups`.

---

## 5. Validate MCP Stdio Server Over Docker

Run the MCP stdio server interactively:

```powershell
docker run --read-only `
  --tmpfs /tmp:rw,noexec,nosuid,size=64m `
  --cap-drop ALL `
  --security-opt no-new-privileges `
  --network roleradar_default `
  -e ROLERADAR_MONGO_CONNECTION_STRING="mongodb://mongo:27017/?directConnection=true" `
  -e DOTNET_EnableDiagnostics=0 `
  -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 `
  -e DOTNET_gcServer=0 `
  -i --rm roleradar-mcp:local
```

Send an initialization JSON-RPC frame on stdin:
```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test-client","version":"1.0"}}}
```

The server responds on `stdout` with protocol capabilities while all logs are routed to `stderr`.

---

## 6. Run Image Verification Test Suites

To execute the automated launched-host test suites against the newly built container images:

```powershell
pwsh ./scripts/test-images.ps1
```

All startup smoke tests, concurrency race tests, contact privacy no-leak scans, and tool-schema contract tests execute using `docker run` and report 100% PASS.
