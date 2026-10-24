# Contract: Multi-Stage Dockerfile & Build Pipeline

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Approved

## 1. Multi-Stage Dockerfile Targets

The repository root `Dockerfile` defines multi-stage build targets:

```text
├── build (SDK 10.0.402 pinned by digest, locked restore, framework-dependent publish)
├── tools-downloader (Dedicated stage downloading Database Tools 100.17.0, SHA-256 verified, extracting shared libraries)
├── mcp (Final runtime for RoleRadar.Mcp.Stdio using noble-chiseled distroless base)
└── cli (Final runtime for RoleRadar.Cli using noble-chiseled distroless base + bundled tools & libraries)
```

### 1.1 Base Image Digest Pinning

Base images MUST be pinned using multi-arch repository digests:

| Stage | Base Image Reference | Rationale |
| :--- | :--- | :--- |
| `build` | `mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:...` | Matches `global.json`, locked mode package restore. |
| `tools-downloader` | `ubuntu:24.04@sha256:...` | Ubuntu Noble stage with `curl` and `ca-certificates` downloading tools and extracting `libgssapi-krb5-2` shared libraries. |
| `mcp` (runtime) | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:...` | Zero shell, zero package manager, non-root `app` user (UID 1654). |
| `cli` (runtime) | `mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:...` | Zero shell, zero package manager, non-root `app` user (UID 1654); 6 required shared libraries copied from build stage. |

### 1.2 MongoDB Database Tools Downloader Specification

The `tools-downloader` stage downloads official MongoDB Database Tools version **100.19.1** (compatible with MongoDB 8.0.x server wire protocol):

- **amd64 Archive**: `https://fastdl.mongodb.org/tools/db/mongodb-database-tools-ubuntu2404-x86_64-100.19.1.tgz`
  - SHA-256: `f34b84a67ab96b4ff52571ec5d15e2148d96cb140b1ea9ef8c29ac33c48e3480`
- **arm64 Archive**: `https://fastdl.mongodb.org/tools/db/mongodb-database-tools-ubuntu2404-arm64-100.19.1.tgz`
  - SHA-256: `ebf518d9a303b36e612e40be106cd0ecc7135dc15d098626f81ac3ee8654a821`
- **Extracted Binaries**: Only `bin/mongodump` and `bin/mongorestore` are copied into `/usr/local/bin/` with `0755` permissions. All other tools (bsondump, mongotop, etc.) are discarded.
- **Extracted Shared Libraries & Dpkg Metadata**: `mongodump` and `mongorestore` are dynamically linked Go binaries requiring GSSAPI Kerberos shared libraries. The stage installs `libgssapi-krb5-2` and copies strictly the 6 required shared libraries (`libgssapi_krb5.so.2`, `libkrb5.so.3`, `libk5crypto.so.3`, `libcom_err.so.2`, `libkrb5support.so.0`, `libkeyutils.so.1`) into the chiseled runtime library search path. Additionally, the corresponding dpkg status entries for these packages are copied into `/var/lib/dpkg/status` so vulnerability scanners (Trivy, Syft) can catalog and audit them. No shell or package manager is present in the final CLI image.

### 1.3 User & Security Metadata

- Non-root UID/GID: `1654:1654` (`app:app`).
- Shell: None (`/bin/sh` and `/bin/bash` absent from both images).
- Package Manager: None (`apt`, `dpkg` absent from both images).
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
