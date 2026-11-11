# ADR 0049: Local Observability Orchestration — Aspire AppHost and Dashboard

- **Status**: Accepted
- **Date**: 2026-11-11
- **Feature**: `015-observability` (Gate 1, Jira RR-67)

## Context and Problem Statement

Feature 015 (`015-observability`, Epic RR-66) adds OpenTelemetry distributed tracing, structured logs, and runtime metrics across the RoleRadar stack (`RoleRadar.Web`, `RoleRadar.Api`, `RoleRadar.Identity`, `RoleRadar.Cli`, and `RoleRadar.Mcp.Stdio`). Feature 014 ([ADR-0044](./0044-problem-details-and-trace-context.md)) already standardized W3C Trace Context (`traceparent` and 32-hex `traceId`) across HTTP boundaries, problem documents, structured logs, and MongoDB `request_correlation_id` audit entries.

We need a local developer observability loop and telemetry viewer that:

1. Receives standard OpenTelemetry Protocol (OTLP) traces, logs, and metrics over loopback (`127.0.0.1`) so switching to a hosted backend later is a configuration change (`OTEL_EXPORTER_OTLP_ENDPOINT`) rather than a code change.
2. Starts the local development loop with one command (`dotnet run --project src/RoleRadar.AppHost`) while keeping the Docker Compose stack (`deploy/docker-compose.yml` + `deploy/docker-compose.apps.yml`) as the packaged stack for real owner data, the `Lane=real-stack` end-to-end suite, the demo stack, and any future container deployment.
3. Proves via an isolated scratch spike (using strictly fictional data and throwaway `roleradar-spike-*` containers/volumes on non-default ports, never touching the running developer stack on `27017`/`5001`/`5002`/`5003` or `roleradar-identity-data`) whether the .NET Aspire AppHost (`Option 2`) can faithfully reproduce:
   - MongoDB `9.0.4` as a single-node replica set (`rs0`) supporting multi-document ACID transactions (`TransactionAndAuditCommandDecorator`).
   - `RoleRadar.Identity` OIDC issuer URLs (`iss`) and strict TLS certificate validation across `Web`, `Api`, and `Identity` without `NODE_TLS_REJECT_UNAUTHORIZED=0` or `DangerousAcceptAnyServerCertificateValidator`.
   - Automated drift prevention between `src/RoleRadar.AppHost/Program.cs` and `deploy/docker-compose*.yml`.
   - Built-in local telemetry views in the Aspire Dashboard and verification of whether saved custom dashboards exist.

If the spike revealed a blocker on replica-set MongoDB or Identity TLS/issuer parity, the pre-agreed fallback (`Option 1`) was to run the standalone `mcr.microsoft.com/dotnet/aspire-dashboard` container in an opt-in Docker Compose overlay (`deploy/docker-compose.observability.yml`) without an AppHost project.

## Standards and Options Considered

| Candidate | Signals Supported | License | Assessment |
| --- | --- | --- | --- |
| **.NET Aspire AppHost + Aspire Dashboard (`13.6.4`) — Option 2** | OTLP Traces, Structured Logs, Metrics, plus Resource Graph & Console | MIT (`dotnet/aspire`) | **Selected (with NuGet audit finding noted below).** Native OTLP gRPC (`18889`) and HTTP (`18890`) receiver + Blazor UI (`18888`) bound to `127.0.0.1`. Orchestrates `Identity`, `Api`, and `Web` from source alongside replica-set `mongo:9.0.4`. |
| **Standalone Aspire Dashboard (`13.6.4`) in Compose Overlay — Option 1 (Fallback)** | OTLP Traces, Structured Logs, Metrics | MIT (`dotnet/aspire`) | **Retained as documented fallback and `Lane=real-stack` OTLP receiver.** Zero-dependency container (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`). Used as the OTLP collector in the isolated real-stack E2E test (`SC-001`) and as the fallback if `NU1903` on `Aspire.Hosting` `13.6.4` is not waived before `13.6.5` reaches 14 days of age. |
| **Aspire AppHost as Single Orchestration & Deployment Publisher (`azd`) — Option 3** | OTLP + Deployment Manifests | MIT | **Deferred.** Replacing Docker Compose or publishing deployment manifests from the AppHost waits until cloud hosting is un-parked ([ADR-0011](./0011-hosted-mongodb-selection-and-compatibility.md), [ADR-0012](./0012-azure-container-runtime-target.md)). When hosting is un-parked, Azure Container Apps via `azd` is the likely target and Option 3 will be reconsidered then. |
| **Seq (`datalust/seq`)** | Structured Logs, OTLP Traces | Proprietary / Commercial (BSL/EULA; free single-user tier only) | **Rejected.** Not open-source (fails OSI license preference when MIT/Apache-2.0 alternatives exist) and lacks native OTLP metrics views comparable to the Aspire Dashboard. |
| **Jaeger (`jaegertracing/all-in-one`)** | OTLP Traces only | Apache-2.0 | **Rejected.** Traces-only; provides neither structured log viewing nor OTLP metric histograms/counters, requiring a second and third tool for logs and metrics. |
| **Grafana LGTM (`otel-lgtm` / Loki + Grafana + Tempo + Mimir/Prometheus)** | OTLP Traces, Logs, Metrics, Custom Dashboards | AGPLv3 / FSL | **Rejected for local dev loop.** Heavy multi-daemon container footprint (~1 GB+ RAM) and AGPLv3/FSL licensing barrier. Reserved as a future hosted backend option via standard OTLP config. |

## Decision Outcome

We confirm **Option 2 (`.NET Aspire AppHost + Aspire Dashboard`)** for the local source development loop (`src/RoleRadar.AppHost` in Gate 5), while keeping Docker Compose (`deploy/docker-compose.yml` + `deploy/docker-compose.apps.yml`) unchanged as the packaged stack for real data, `Lane=real-stack` verification, and the demo stack.

### Important Gate 5 NuGet Audit Finding (`GHSA-f7vh-h5g3-7jrh` vs. 14-Day Release Age Rule)

During the scratch spike on `2026-11-11`, restoring `Aspire.Hosting` `13.6.4` triggered NuGet audit warning `NU1903` (elevated to an error in repo builds by `TreatWarningsAsErrors=true` and `NuGetAudit=true` in `Directory.Build.props`):

- **Advisory**: `GHSA-f7vh-h5g3-7jrh` / `CVE-2026-120006` (High severity: Aspire Elevation of Privilege vulnerability affecting `Aspire.Hosting` and `Aspire.Hosting.AppHost` `< 13.6.5`), published `2026-11-10T23:24:48Z`.
- **Patched version**: `13.6.5`, published `2026-11-10T17:13:41Z` (1 day old as of `2026-11-11`; reaches the 14-day release age threshold on `2026-11-24`).
- **Impact on Gates 2–4**: None. Gates 2, 3, and 4 use only `OpenTelemetry.*` `1.20.1` packages (`OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, and `OpenTelemetry.Exporter.InMemory` in test projects—all Apache-2.0, published `2026-10-20`, 22 days old, 0 advisories, matching [plan.md](../../specs/015-observability/plan.md) and [tasks.md](../../specs/015-observability/tasks.md); an earlier draft of this ADR mistakenly wrote `1.15.2`, which was a typo and not a deliberate difference) plus the standalone `otel/opentelemetry-collector-contrib:0.164.0` and `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4` container images (0 Trivy/NuGet build warnings).
- **Gate 5 Resolution (Owner Decides)**: When Gate 5 (`RR-71`) adds `src/RoleRadar.AppHost/RoleRadar.AppHost.csproj`, if Gate 5 lands before `2026-11-24`, **the owner decides** between:
  1. **(Recommended)** An explicit owner waiver of the 14-day release-age rule for the `13.6.5` security patch so `Aspire.Hosting.*` pins `13.6.5` cleanly with zero vulnerabilities.
  2. **(Fallback)** Activating the Option 1 Compose overlay fallback (`deploy/docker-compose.observability.yml`) until `13.6.5` reaches 14 days of age on `2026-11-24`.
  3. **(Not Recommended — Never Suppress a High Advisory)** Adding `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-f7vh-h5g3-7jrh" />` to `RoleRadar.AppHost.csproj`. Suppressing a High-severity vulnerability advisory to keep an unpatched `13.6.4` package is explicitly not recommended.

## Pinned Packages and Container Image Digest (Release Age & License Audit)

All candidate packages and container images were audited against the 14-day minimum release age rule (cutoff `<= 2026-10-28` relative to `2026-11-11`):

| Artifact | Ecosystem | Pinned Version / Digest | Published Date | Age (as of 2026-11-11) | License | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| `OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Exporter.InMemory` | NuGet | `1.20.1` | `2026-10-20` (`1.21.0` on `2026-11-11` excluded as `< 14` days) | 22 days | Apache-2.0 | Pinned in Gates 2–4 (`plan.md` & `tasks.md`); 0 advisories |
| `Aspire.Hosting` / `Aspire.Hosting.AppHost` | NuGet | `13.6.4` (patch `13.6.5` noted above) | `2026-10-24` (`13.6.5` on `2026-11-10`) | 18 days (`13.6.4`) | MIT | Affected by `GHSA-f7vh-h5g3-7jrh` (`< 13.6.5`); see Gate 5 resolution above |
| `Aspire.Hosting.MongoDB` | NuGet | `13.6.4` | `2026-10-24` | 18 days | MIT | Orchestrates `mongo:9.0.4` resource |
| `Aspire.Hosting.JavaScript` | NuGet | `13.6.4` | `2026-10-24` | 18 days | MIT | Orchestrates `web/` (`pnpm run dev`) |
| `mcr.microsoft.com/dotnet/aspire-dashboard` | Container | `13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230` | `2026-10-24` | 18 days | MIT | Standalone & AppHost dashboard image |

## Empirical Spike Evidence (`RR-67`)

All spike checks executed in an isolated scratch directory outside the repository (`$env:TEMP\roleradar-spike-015`) guarded by `assert-spike-safety.ps1`, which failed closed on any reference to port `27017`, `5001`–`5003`, `8080`, `roleradar-mongo`, `roleradar-mongo-data`, or `roleradar-identity-data`. All scratch containers, volumes, and files were deleted upon completion and verified absent via `docker ps -a`, `docker volume ls`, and `git status`.

### 1. Replica-Set MongoDB (`mongo:9.0.4`, `rs0`) and Multi-Document ACID Transaction

- **Model Verification**: Constructed a `DistributedApplication` using `Aspire.Hosting` `13.6.4` and `Aspire.Hosting.MongoDB` `13.6.4`:

  ```csharp
  var mongo = builder.AddMongoDB("mongo")
      .WithImageTag("9.0.4")
      .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
      .WithArgs("--replSet", "rs0", "--bind_ip_all");
  ```

  Verified in-memory `DistributedApplicationModel` resource metadata:
  `ASPIRE_MODEL_CHECK: Resource=mongo, Image=library/mongo:9.0.4, CmdArgsAnnotations=1, EnvAnnotations=2`.
- **Container & Transaction Verification**: Launched throwaway container `roleradar-spike-mongo` (`mongo:9.0.4 --replSet rs0 --bind_ip_all --port 27099`, `GLIBC_TUNABLES=glibc.pthread.rseq=1`, volume `roleradar-spike-mongo-data`) bound to `127.0.0.1:27099`, initialized `rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: '127.0.0.1:27099' }] })`, and ran a C# verification client (`MongoDB.Driver` `3.13.0`) executing `StartSessionAsync()` -> `StartTransaction(ReadConcern.Snapshot, WriteConcern.WMajority)` -> inserting fictional documents into `companies` (`Fictional Horizon Robotics Ltd`) and `audit_log` -> `CommitTransactionAsync()`.
- **Output**:
  `MONGO_TXN_COMMIT_OK: setName=rs0, primary=true, companies=1, audit_log=1, companyId=01a1cb7c-a260-7ae8-86ea-437f5852eb98, auditId=01a1cb7c-a260-7f0c-a519-be5d244afae6`.
- **Nuance Recorded for Gate 5**: `Aspire.Hosting.MongoDB` starts `mongod` with `--replSet rs0`, which requires a one-shot idempotent `rs.initiate` command after container startup if the volume is uninitialized, plus `directConnection=true` (or replica-set member host matching `127.0.0.1:<port>`) in the connection string injected into `RoleRadar.Api`.

### 2. Identity Issuer URLs (`iss`) and TLS Parity (Compose vs. AppHost from Source)

- **Compose Baseline (`deploy/docker-compose.apps.yml`)**:
  - `identity`, `api`, and `web` run inside a bridge network using `deploy/certs/dev-cert.pfx` / `dev-cert.crt` (`Subject: CN=identity`, `SAN: DNS Name=identity, DNS Name=api, DNS Name=localhost, IP Address=127.0.0.1`).
  - `ROLERADAR_IDENTITY_ISSUER_URI` and `ROLERADAR_OIDC_AUTHORITY` are set to `https://identity:5001`.
  - Container-to-container TLS trust is established via `SSL_CERT_FILE=/https/dev-cert.crt` (in `api`) and `NODE_EXTRA_CA_CERTS=/https/dev-cert.crt` (in `web`).
- **AppHost Source Execution (`Option 2`)**:
  - When `RoleRadar.Identity`, `RoleRadar.Api`, and `web` run as host processes from source under `RoleRadar.AppHost`, all three share the host loopback namespace (`localhost` / `127.0.0.1` / `::1`).
  - Setting `ROLERADAR_IDENTITY_ISSUER_URI=https://localhost:5001` (or the isolatedspike port `https://localhost:55001`) on `Identity` and `Web` and `ROLERADAR_OIDC_AUTHORITY=https://localhost:5001` on `Api` aligns the browser redirect origin, the OIDC discovery document `issuer`, the JWT `iss` claim, and the backchannel JWKS/token endpoints to the exact same `https://localhost:<port>` origin.
- **Empirical Verification & Host TLS Nuances Discovered**:
  - Launched `RoleRadar.Identity` (`https://localhost:55001`, throwaway SQLite DB) and `RoleRadar.Api` (`https://localhost:55002`, connected to `roleradar-spike-mongo` on `127.0.0.1:27099`) as host processes and drove them from Node.js `v22.23.4`.
  - **Nuance 1 (Dual-Stack Loopback Binding)**: On Windows 11, `localhost` resolves to IPv6 `::1` first. If `Identity` binds only to `https://127.0.0.1:55001` while `Api` fetches discovery from `https://localhost:55001`, `HttpClient` incurs a ~2-second TCP timeout on `[::1]:55001` before falling back to `127.0.0.1:55001`. Binding Kestrel to `https://localhost:<port>` listens on both `127.0.0.1` and `::1`, eliminating the delay (`GET /.well-known/openid-configuration/jwks responded 200 in 10.563 ms`).
  - **Nuance 2 (Windows Schannel vs. Linux OpenSSL CA Trust & Certificate Store Record)**: On Windows, .NET's `SocketsHttpHandler` uses OS Schannel (`Cert:\CurrentUser\Root`) and ignores `SSL_CERT_FILE` (which is only read by OpenSSL on Linux). Therefore, when running `Identity` and `Api` as host processes on Windows under `RoleRadar.AppHost`, Kestrel uses the trusted ASP.NET Core development certificate (`dotnet dev-certs https --trust`) or `deploy/certs/dev-cert.crt` trusted in `CurrentUser\Root`, while Node.js `web` receives `NODE_EXTRA_CA_CERTS` pointing to the PEM certificate.
    - **Plain Record of What the Spike Ran and Whether the Certificate Store Changed**: During the spike, the agent invoked `dotnet dev-certs https -ep $devPfx -p $devPass --trust` once. On Windows, `--trust` prompts a modal Windows Security confirmation dialog to add the certificate to `CurrentUser\Root`; because the command ran non-interactively and was cancelled after 30 seconds without user confirmation, **nothing was added to or modified in the machine's certificate stores**:
      - `CurrentUser\My` retained its pre-existing ASP.NET Core development certificate unchanged (`Thumbprint=68EAEACDB6CE399ACC7685ECCBB03C76CEF1F7B5`, `Subject=CN=localhost`, valid `2026-03-08` to `2027-03-08`).
      - `CurrentUser\Root` was not modified (`dotnet dev-certs https --check --trust` still exits `1` reporting `68EAEACDB6CE399ACC7685ECCBB03C76CEF1F7B5` is not trusted, and `deploy/certs/dev-cert.crt` [`Thumbprint=30DD1A40F86EE7AE7A9AF09E069123D867DC6E30`] is not in `CurrentUser\Root`). The spike re-ran `dotnet dev-certs https -ep $devPfx -p $devPass` **without** `--trust` to export only to `$env:TEMP\roleradar-spike-015` (subsequently deleted).
    - **Standing Rule**: **The owner, never an agent, runs any certificate trust step** (such as `dotnet dev-certs https --trust` or importing a certificate into `CurrentUser\Root`), because trusting a root certificate modifies machine/user security state and triggers an interactive OS security dialog.
  - **Spike Output**:
    `OIDC_TLS_PARITY_OK: {"issuer":"https://localhost:55001","jwt_iss":"https://localhost:55001","jwt_aud":"roleradar.api","jwt_sub":"01950000-0000-7000-8000-000000000001","jwt_scope":["roleradar.api.read"],"authorization_endpoint":"https://localhost:55001/connect/authorize","token_endpoint":"https://localhost:55001/connect/token","jwks_uri":"https://localhost:55001/.well-known/openid-configuration/jwks","jwks_keys_count":1,"token_minted":true,"api_ready_status":"Healthy","api_unauth_status":401,"api_problem_traceId":"4bf92f3577b34da6a3ce929d0e0e4736"}`.

### 3. Drift Prevention Between AppHost and Docker Compose

- Per `AGENTS.md` ("Prefer an existing tool or GitHub Action to a new custom script; a new script needs a one-line justification"), drift between `src/RoleRadar.AppHost/Program.cs` and `deploy/docker-compose.yml` + `deploy/docker-compose.apps.yml` is prevented by an xUnit architecture test (`AppHostComposeParityArchitectureTests`) in the existing `tests/RoleRadar.Tests.Architecture` project (`pwsh ./scripts/test-fast.ps1`), requiring zero new test projects and zero custom scripts.
- **What the Test Asserts**:
  1. **MongoDB Image Tag Parity**: Parses `deploy/docker-compose.yml` (`image: mongo:9.0.4`) and asserts that the `MongoDBServerResource` in `RoleRadar.AppHost`'s `DistributedApplicationModel` (or shared constant) pins the identical `9.0.4` image tag, `--replSet rs0` argument, and `GLIBC_TUNABLES=glibc.pthread.rseq=1` environment variable ([ADR-0040](./0040-mongodb-9-and-one-image-source.md)).
  2. **Service Port & Endpoint Parity**: Asserts that `identity` (`5001`), `api` (`5002`), and `web` (`5003`) ports and health probe paths (`/health/ready`, `/health/live`, `/health`) match between `deploy/docker-compose.apps.yml` and `RoleRadar.AppHost`.
  3. **Required Environment Variable Parity**: Asserts that every non-container-specific configuration variable required by `identity`, `api`, and `web` in `deploy/docker-compose.apps.yml` is wired into the corresponding `RoleRadar.AppHost` resource builder, with only the documented host-vs-container network differences (`localhost` vs. `identity`/`api` service hostnames, and host dev-cert trust vs. `/https/dev-cert.crt` container mounts).

### 4. Aspire Dashboard Built-In Views and Custom Dashboard Capabilities

- Launched `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230` in throwaway container `roleradar-spike-dashboard` bound exclusively to `127.0.0.1` (`18888/tcp -> 127.0.0.1:58888`, `18889/tcp -> 127.0.0.1:58889`).
- Probed the UI routes and confirmed the four built-in views return `200 OK`:
  - `/` (Resources / Console logs when hosted by AppHost; redirects/renders structured views in standalone mode; `status=200`, `5815` bytes)
  - `/structuredlogs` (Structured Logs view with trace/span filtering; `status=200`, `5770` bytes)
  - `/traces` (Distributed Traces waterfall view; `status=200`, `5790` bytes)
  - `/metrics` (OTLP Metrics instrument & histogram explorer; `status=200`, `5780` bytes)
- Confirmed that `/api/telemetry`, `/api/traces`, and `/api/dashboards` return `404 Not Found`:
  - The Aspire Dashboard **does not support saved custom dashboards** (all views are built-in, in-memory views that reset when the container restarts).
  - Because the Aspire Dashboard exposes no public HTTP query API for saved dashboards or telemetry assertions, automated end-to-end verification in Gate 2 (`SC-001`) will assert OTLP export directly at the OTLP collector boundary, and Gate 5 (`RR-71`) will verify visual rendering in the Aspire Dashboard via a manual check on the isolated stack with fictional data.

### 5. `RoleRadar.Cli` and `RoleRadar.Mcp.Stdio` Telemetry Export Policy

- **Decision**: Both `RoleRadar.Cli` and `RoleRadar.Mcp.Stdio` are **default-off** for OTLP telemetry export in Feature 015 (`SC-003`).
- **Rationale**:
  1. Both hosts are short-lived or stdio-bound processes that already emit the 32-hex W3C `traceId` in structured logs (`stderr` only for `Mcp.Stdio` per Constitution Principle VI) and record `request_correlation_id` in MongoDB `audit_log` entries from Feature 014 ([ADR-0044](./0044-problem-details-and-trace-context.md)).
  2. When no `OTEL_EXPORTER_OTLP_ENDPOINT` is configured (or when it is empty/whitespace), **no OTLP exporter is registered** and **zero outbound network connections are opened**, guaranteeing zero startup or shutdown latency penalty and offline operation.
  3. When a developer explicitly sets `OTEL_EXPORTER_OTLP_ENDPOINT` for local debugging, `RoleRadar.Observability` registers the OTLP exporter with a bounded `ForceFlush(timeoutMilliseconds)` on shutdown (`ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS`, default `2000` ms, clamped `100..10000` ms) so `RoleRadar.Cli` never hangs if the local collector is unreachable, and `RoleRadar.Mcp.Stdio` never writes a single byte to `stdout`.

### 6. Deferral of Option 3 (Single Orchestration / `azd` Deployment Artifacts)

- Using the Aspire AppHost (`src/RoleRadar.AppHost`) as the single orchestration definition that replaces Docker Compose or generates cloud deployment manifests (`azd` / Azure Container Apps Bicep) is **explicitly out of scope and deferred**.
- Local-first operation and the hardened Docker Compose stack (`deploy/docker-compose.yml` + `deploy/docker-compose.apps.yml`) remain authoritative for packaged containers, `Lane=real-stack`, and the demo stack. Option 3 will be revisited when cloud hosting is un-parked ([ADR-0012](./0012-azure-container-runtime-target.md)).

## Consequences

- **Good**: Confirming Option 2 provides a one-command source developer loop (`dotnet run --project src/RoleRadar.AppHost`) with correlated traces, logs, metrics, and resource health in the Aspire Dashboard on `127.0.0.1` without altering the packaged Docker Compose stack.
- **Good**: Identifying the Windows `https://localhost:<port>` dual-stack binding requirement, the owner-only `dotnet dev-certs https --trust` prerequisite, and the `Aspire.Hosting` `13.6.4` `GHSA-f7vh-h5g3-7jrh` (`13.6.5` age) advisory in Gate 1 prevents surprises when Gate 5 (`RR-71`) is implemented.
- **Neutral**: Because the Aspire Dashboard has no saved custom dashboards and no public telemetry query API, automated tests assert telemetry at the OTLP receiver boundary (`InMemoryExporter` and a lightweight OTLP test receiver in `Lane=real-stack`), while Dashboard UI inspection in Gate 5 is a documented manual check on the isolated stack.
