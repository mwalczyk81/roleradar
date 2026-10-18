# Contract: Docker Compose Overlays & PowerShell Wrapper Script

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Approved

## 1. Docker Compose Profiles Overlay (`deploy/docker-compose.apps.yml`)

A compose extension file defines service definitions for running the CLI and MCP server within the compose network:

```yaml
services:
  cli:
    profiles: ["apps"]
    build:
      context: ..
      target: cli
    image: roleradar-cli:local
    read_only: true
    tmpfs:
      - /tmp:rw,noexec,nosuid,size=64m
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true
    environment:
      - ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27017/?directConnection=true
      - ROLERADAR_MONGO_DATABASE=roleradar
      - DOTNET_EnableDiagnostics=0
      - DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
      - DOTNET_gcServer=0
    volumes:
      - ./data/backup:/data/backup:rw
    network_mode: default

  mcp:
    profiles: ["apps"]
    build:
      context: ..
      target: mcp
    image: roleradar-mcp:local
    read_only: true
    tmpfs:
      - /tmp:rw,noexec,nosuid,size=64m
    cap_drop:
      - ALL
    security_opt:
      - no-new-privileges:true
    environment:
      - ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27017/?directConnection=true
      - ROLERADAR_MONGO_DATABASE=roleradar
      - DOTNET_EnableDiagnostics=0
      - DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
      - DOTNET_gcServer=0
    network_mode: default
```

> [!NOTE]
> `directConnection=true` is mandatory when connecting from container to container because the single-node replica set advertises `127.0.0.1:27017`.

---

## 2. Cross-Platform PowerShell Wrapper Script (`scripts/roleradar-docker.ps1`)

A thin, cross-platform wrapper script allows running containerized commands with identical developer ergonomics to native binary invocation:

```powershell
# Usage Examples:
pwsh ./scripts/roleradar-docker.ps1 status
pwsh ./scripts/roleradar-docker.ps1 status --json
pwsh ./scripts/roleradar-docker.ps1 backup ./backups
pwsh ./scripts/roleradar-docker.ps1 restore ./backups/2026-10-18.rrbak
pwsh ./scripts/roleradar-docker.ps1 mcp  # Launches stdio MCP session
```

### 2.1 Mount Rules Enforced by Wrapper Script
- **Input paths** (e.g. restore archives, import markdown files): Mounted as **read-only** (`:ro`).
- **Output paths** (e.g. backup output directory): Mounted as **read-write** (`:rw`).
- **All other host directories**: Unmounted.
