# Contract: Local Telemetry Dev Loop and Drift Guard (`RR-67`, `RR-71`)

**Feature**: `015-observability` (`RR-67`, `RR-71`, `FR-010` to `FR-013`, `SC-006`, `SC-007`)
**ADR**: `docs/adr/0049-local-observability-apphost-and-dashboard.md`

## 1. Gate 1 Spike Verification Matrix (`RR-67` -> `ADR 0049`)

The Gate 1 spike runs in an isolated scratch directory outside the repository using strictly fictional data and throwaway Docker resources, and records `ADR 0049` (docs-only PR). `ADR 0049` MUST test and record:

1. **Candidate Comparison**:
   - `.NET Aspire` (`Aspire.Hosting.AppHost` `13.6.4` + `Aspire.Dashboard` `13.6.4`) vs. `Seq`, `Jaeger`, and `Grafana LGTM` across licensing (MIT/Apache-2.0 required), signals supported (traces, logs, metrics, resource graph), footprint, and local loopback privacy.
2. **Check 1 — MongoDB Replica Set (`rs0`)**:
   - Verify that the local setup runs `mongo:9.0.4` with `--replSet rs0` and `GLIBC_TUNABLES=glibc.pthread.rseq=1`, initializes the single-node replica set, and completes a multi-document transaction (`TransactionAndAuditCommandDecorator`) without error.
3. **Check 2 — Identity Issuer URLs and TLS Parity**:
   - Verify that Identity's `ROLERADAR_IDENTITY_ISSUER_URI`, API's `ROLERADAR_OIDC_AUTHORITY`, Web's `ROLERADAR_IDENTITY_ISSUER_URI`, and the development TLS certificate SANs (`DNS:identity`, `DNS:api`, `DNS:localhost`, `IP:127.0.0.1` or `*.localhost`) allow both browser OIDC redirects and server-to-server backchannel discovery/JWKS/token calls to succeed over HTTPS.
4. **Check 3 — Compose Drift Prevention Mechanism**:
   - Verify how ports (`5001` Identity, `5002` API, `5003` Web, `27017` Mongo), required environment variables, and pinned image tags/digests (`mongo:9.0.4`, `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) are verified against `deploy/docker-compose.yml` and `deploy/docker-compose.apps.yml` via `AppHostComposeParityArchitectureTests`.
5. **Check 4 — Short-lived `CLI` and `Mcp.Stdio` Policy**:
   - Record the owner decision and rationale: `CLI` and `Mcp.Stdio` do not export OTLP telemetry by default in Feature 015 because their W3C `traceId` is already present in console/stderr logs and `request_correlation_id` audit entries from Feature 014; opt-in via `OTEL_EXPORTER_OTLP_ENDPOINT` uses a bounded flush timeout and never writes to `stdout`.
6. **Check 5 — Built-in Dashboard Views Expectation**:
   - Confirm that the Aspire Dashboard provides built-in views (Structured Logs, Traces, Metrics, Resources/Console) and does not support custom saved dashboards, noting that a hosted deployment would pair OTLP with a backend that supports saved dashboards.
7. **Check 6 — Option 3 Deferral**:
   - Explicitly record that Option 3 (using the AppHost as the single orchestration and publishing deployment artifacts, e.g., to Azure Container Apps via `azd`) is deferred until hosting is un-parked and a target is selected.
8. **Decision & Fallback Rule**:
   - Confirming Option 2 (Aspire AppHost + Aspire Dashboard) or reporting a concrete blocker is the expected outcome of `RR-67`.
   - If Check 1 (replica-set Mongo) or Check 2 (Identity issuer/TLS) hits an unresolvable blocker in the AppHost, `ADR 0049` records the blocker and selects the fallback: running the standalone Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) in an opt-in Docker Compose overlay (`deploy/docker-compose.telemetry.yml`).

## 2. Gate 5 Local Setup Invariants (`RR-71`)

1. **Loopback-only binding (`FR-005`, `SC-003`)**:
   - The Aspire Dashboard UI and OTLP ingestion endpoints bind exclusively to `127.0.0.1` (never `0.0.0.0`).
2. **Real-volume isolation (`FR-013`, `SC-006`)**:
   - The AppHost uses dedicated throwaway storage (`roleradar-apphost-*` or ephemeral container storage) and MUST NEVER reference or mount `roleradar-mongo-data` or `roleradar-identity-data`.
3. **Architecture & Layering (`ObservabilityArchitectureTests` / `LayerDependencyArchitectureTests`)**:
   - If Option 2 (`src/RoleRadar.AppHost`) is used:
     - `RoleRadar.AppHost` may reference `RoleRadar.Api` and `RoleRadar.Identity` as project resources.
     - `RoleRadar.AppHost` MUST NOT directly reference `Duende.*` NuGet packages.
     - Zero projects in `src/` (`Domain`, `Application`, `Infrastructure`, `Observability`, `Api`, `Identity`, `Cli`, `Mcp.Stdio`) may reference `RoleRadar.AppHost`.
4. **Automated Drift Test (`AppHostComposeParityArchitectureTests` in `RoleRadar.Tests.Architecture`)**:
   - Parses `deploy/docker-compose.yml` and `deploy/docker-compose.apps.yml` and compares them against the AppHost/overlay configuration to assert:
     - Matching pinned MongoDB image (`mongo:9.0.4`) and `GLIBC_TUNABLES=glibc.pthread.rseq=1` workaround.
     - Matching service ports (`5001` Identity, `5002` API, `5003` Web).
     - Pinned Aspire Dashboard digest (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`).
     - Complete absence of real volume names (`roleradar-mongo-data`, `roleradar-identity-data`), container `roleradar-mongo`, and port `27017` in the AppHost.
5. **Manual Dashboard Verification & Evidence Record (`SC-006`)**:
   - Because the Aspire Dashboard UI does not expose a public REST query API for automated test runners (and automated OTLP wire verification is handled in Gate 2's `RealStackEndToEndTests` via the OpenTelemetry Collector `file` exporter), verifying the Dashboard's four built-in views in Gate 5 is a **manual check on the isolated local stack with strictly fictional data**.
   - The Gate 5 PR verification record MUST log:
     1. Startup command and `127.0.0.1` loopback listener verification output.
     2. **Resources view**: `roleradar-web`, `roleradar-api`, `roleradar-identity`, and `mongo` (`rs0`) all showing `Running` / `Healthy` on throwaway volumes (`roleradar-apphost-*`).
     3. **Traces view**: the observed 32-character W3C `traceId` of a fictional sign-in (`roleradar-web` -> `roleradar-identity`) and a fictional posting transition (`roleradar-web` -> `roleradar-api` -> `mongodb` replica-set transaction).
     4. **Structured Logs view**: correlated log records for that `traceId` showing matching `TraceId`/`SpanId` and `[redacted]` placeholders with zero raw secrets.
     5. **Metrics view**: rendered data points for `http.server.request.duration`, `roleradar.store.readiness.status`, and `roleradar.store.operation.duration`.
     6. Post-run `docker volume ls` output confirming `roleradar-mongo-data` and `roleradar-identity-data` were untouched.
