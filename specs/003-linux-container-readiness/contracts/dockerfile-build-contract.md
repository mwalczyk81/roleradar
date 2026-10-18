# Contract: Multi-Stage Dockerfile & Build Pipeline

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Approved

## 1. Multi-Stage Dockerfile Targets

The repository root `Dockerfile` defines multi-stage build targets:

```text
├── build (SDK 10.0.402 pinned by digest, locked restore, framework-dependent publish)
├── tools-downloader (Dedicated stage for MongoDB Database Tools, SHA-256 verified)
├── mcp (Final runtime for RoleRadar.Mcp.Stdio using noble-chiseled distroless base)
└── cli (Final runtime for RoleRadar.Cli using noble minimal base + bundled tools)
```

### 1.1 Base Image Digest Pinning

Base images MUST be pinned using multi-arch repository digests:

| Stage | Base Image Reference | Rationale |
| :--- | :--- | :--- |
| `build` | `mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:...` | Matches `global.json`, locked mode package restore. |
| `tools-downloader` | `curlimages/curl:8.10.1@sha256:...` | Minimal image with curl and sha256sum for tool downloads. |
| `mcp` (runtime) | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:...` | Zero shell, zero package manager, non-root `app` user (UID 1654). |
| `cli` (runtime) | `mcr.microsoft.com/dotnet/runtime:10.0-noble@sha256:...` | Provides glibc dependencies for MongoDB Database Tools; non-root `app` user (UID 1654). |

### 1.2 MongoDB Database Tools Downloader Specification

The `tools-downloader` stage downloads official MongoDB Database Tools version 100.11.0:

- **amd64 Archive**: `mongodb-database-tools-ubuntu2404-x86_64-100.11.0.tgz`
  - SHA-256: Verified against pinned hash in Dockerfile.
- **arm64 Archive**: `mongodb-database-tools-ubuntu2404-arm64-100.11.0.tgz`
  - SHA-256: Verified against pinned hash in Dockerfile.
- **Extracted Binaries**: Only `bin/mongodump` and `bin/mongorestore` are copied into `/usr/local/bin/` with `0755` permissions. All other tools (bsondump, mongotop, etc.) are discarded.

### 1.3 User & Security Metadata

- Non-root UID/GID: `1654:1654` (`app:app`).
- Working directory: `/app`.
- Listening ports (`EXPOSE`): None.
- `HEALTHCHECK`: None (stdio protocol does not bind network sockets).

---

## 2. `.dockerignore` Deny-by-Default Policy

The repository `.dockerignore` enforces strict exclusion of all sensitive files, test artifacts, and build outputs:

```dockerignore
# Deny by default
**

# Allow only source and required build metadata
!RoleRadar.sln
!global.json
!NuGet.Config
!Directory.Build.props
!Directory.Packages.props
!packages.lock.json
!src/**/packages.lock.json
!src/**/*.csproj
!src/**/*.cs

# Explicitly exclude sensitive directories
import-review/
private/
db/
*.rrbak
*.dump
*.sql.gz
*.env
.git/
TestResults/
bin/
obj/
dist/
```
