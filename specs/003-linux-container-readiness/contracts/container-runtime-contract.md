# Contract: Container Runtime & Execution Constraints

**Feature**: `003-linux-container-readiness`  
**Date**: 2026-10-18  
**Status**: Approved

## 1. Supported & Tested Runtime Execution Mode

Both container images (`roleradar-cli` and `roleradar-mcp`) are verified exclusively under the following execution parameters:

```bash
docker run \
  --read-only \
  --tmpfs /tmp:rw,noexec,nosuid,size=64m \
  --cap-drop ALL \
  --security-opt no-new-privileges \
  -e DOTNET_EnableDiagnostics=0 \
  -e DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
  -e DOTNET_gcServer=0 \
  -i --rm <image-tag> <arguments...>
```

### 1.1 Flag Semantics & Enforcement

| Flag | Contract Requirement | Failure Mode if Violated |
| :--- | :--- | :--- |
| `--read-only` | Root filesystem is mounted read-only. Applications must not attempt writes outside `/tmp` or mounted volumes. | Container crashes or raises `System.IO.IOException: Read-only file system`. |
| `--tmpfs /tmp:...` | Volatile RAM-backed filesystem mounted at `/tmp`. Max size 64MB. `noexec`, `nosuid` flags set. | .NET fails to initialize temporary buffers; Database Tools credential config cannot be created. |
| `--cap-drop ALL` | Strips all Linux capabilities (`CAP_SYS_ADMIN`, `CAP_NET_RAW`, etc.). | Image runs with excessive privileges, violating Principle I and V. |
| `--security-opt no-new-privileges` | Prevents child processes from gaining additional privileges via setuid/setgid binaries. | Security escalation risk. |
| `DOTNET_EnableDiagnostics=0` | Disables .NET diagnostic domain socket creation in `/tmp/.dotnet/`. | .NET host crashes attempting to bind diagnostic sockets on read-only/restricted paths. |
| `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` | Runs in invariant culture mode, eliminating ICU package requirements. | Missing ICU library errors if glibc slices lack libicu. |
| `DOTNET_gcServer=0` | Forces Workstation GC, reducing footprint for single-session CLI and stdio tools. | Higher baseline memory allocation during cold start. |

---

## 2. Environment Variables Contract

| Variable | Target Process | Default Value | Description |
| :--- | :--- | :--- | :--- |
| `ROLERADAR_MONGO_CONNECTION_STRING` | CLI & MCP | (Required) | MongoDB connection string. Credentials are automatically masked in all outputs. |
| `ROLERADAR_MONGO_DATABASE` | CLI & MCP | `roleradar` | Target database name. |
| `ROLERADAR_OWNER_ID` | CLI & MCP | (Configured) | Standard binary GUID string identifying system owner. |
| `ROLERADAR_LOG_LEVEL` | CLI & MCP | `Warning` | Minimum log verbosity for stderr (`Trace`, `Debug`, `Information`, `Warning`, `Error`). |
| `ROLERADAR_BACKUP_DUMP_COMMAND` | CLI | `mongodump --config={configPath} --archive` | Command executed by CLI backup. `{configPath}` placeholder is injected with temporary config file path. |
| `ROLERADAR_BACKUP_RESTORE_COMMAND` | CLI | `mongorestore --config={configPath} --archive` | Command executed by CLI restore. |

---

## 3. Protocol & Stream Hygiene

### 3.1 `roleradar-mcp` (Stdio MCP Server)
- **Standard Input (`stdin`)**: Reserved strictly for client JSON-RPC requests.
- **Standard Output (`stdout`)**: Reserved strictly for MCP JSON-RPC protocol responses. Zero diagnostic logs, progress bars, or debug messages.
- **Standard Error (`stderr`)**: All application logging, startup information, and unhandled errors.

### 3.2 `roleradar-cli`
- **Standard Output (`stdout`)**: Command result summaries, structured JSON (when `--json` is supplied), and interactive prompts.
- **Standard Error (`stderr`)**: Progress notifications, diagnostic logs, and error explanations.

---

## 4. Database Tools Credential Security & Config File Contract

When invoking `mongodump` or `mongorestore`, database authentication credentials must never be exposed on process command lines.

### 4.1 In-Memory Config File Creation
- **Path**: Located on the RAM-backed `/tmp` tmpfs mount (`/tmp/rr-tools-<uuid>.conf`).
- **Atomic Mode 0600 (`UnixCreateMode`)**: Created using .NET `FileStreamOptions` with `UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite` and `FileMode.CreateNew`. The file is NEVER created with loose permissions and chmodded after creation.
- **Cleanup Guarantee**: The config file is deleted immediately in a `finally` block upon command completion. Signal handlers (`SIGTERM`, `ProcessExit`, unhandled exceptions) guarantee deletion even on abnormal termination.

### 4.2 Security Test Invariants
Tests in `RoleRadar.Tests.Integration/Hosting/ContainerCredentialsSecurityTests.cs` assert the following invariants:
1. **Mode 0600 on First Read**: Posix file mode is asserted to be exactly `0600` at the instant of first read before child process execution begins.
2. **Zero Argv Exposure**: Child process `argv` inspected via `/proc/<pid>/cmdline` contains `--config=/tmp/rr-tools-<uuid>.conf` and NEVER contains database passwords, connection URIs, or credentials.
3. **Zero Output Leaks**: The database password does NOT appear in `stdout`, `stderr`, application diagnostic logs, or audit log entries.
4. **Zero Persistent Remnants**: No temporary config files remain anywhere under `/tmp` or on disk after process exit, verified under both clean completion, child crash/failure, and abrupt child termination by `SIGTERM`.
