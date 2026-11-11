# Quickstart & Validation Guide: End-to-End Observability (`015-observability`)

**Feature**: [spec.md](./spec.md)
**Contracts**: [telemetry-signals.md](./contracts/telemetry-signals.md), [privacy-gate.md](./contracts/privacy-gate.md), [local-dev-loop.md](./contracts/local-dev-loop.md)

> **Important (`RR-85`)**: Always run `review-gate.ps1` and `linux-web-check.sh` from **Git Bash** (not Windows PowerShell) so `ODataDocsExamplesTests` resolves Git Bash's `/bin/bash` rather than the WSL stub. Never touch the developer's running MongoDB or `roleradar-identity-data` volume.

## Prerequisites

- .NET 10 SDK (`10.0.x`)
- Node.js 22 and `pnpm`
- Docker Engine 26+ / Docker Compose 2.26+
- Git Bash on Windows (`bash`)

## Gate-by-Gate Validation

### Gate 1 (P1): Local Observability Spike & `ADR 0049` (`RR-67`, Docs Only)

1. Run the spike in an isolated scratch directory outside the repo using strictly fictional data and throwaway containers/volumes (`roleradar-spike-*`), verifying:
   - MongoDB `9.0.4` replica-set (`rs0`) multi-document transaction execution.
   - Identity issuer URL and TLS parity with `deploy/docker-compose.apps.yml`.
   - Compose vs. AppHost drift check coverage.
   - Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.4@sha256:23411f715be2476da0b2c0ad110039237de657c6cae31bf5c587d12a26c11230`) built-in views (traces, structured logs, metrics, resource graph) bound to `127.0.0.1`.
2. Clean up all scratch files and throwaway containers/volumes.
3. Verify `docs/adr/0049-local-observability-apphost-and-dashboard.md` and `docs/adr/README.md` are committed with LF line endings and pass pre-commit checks:
   ```bash
   pre-commit run --all-files
   ```

### Gate 2 (P2): End-to-End Distributed Tracing & Telemetry Privacy Gate (`RR-68`, `RR-72`, `ADR 0050`)

1. Verify `docs/adr/0050-span-attribute-allowlist-processor.md` and `docs/adr/README.md` document the `LogScrubber`-independent span allowlist/drop-list and span exception-event policy (so Gate 2 stands independent of `RR-86`'s outcome in Gate 3).
2. Run the fast backend and web test suites verifying:
   - `SC-001`: Single connected trace (`traceId` and parent-child `spanId`) across `roleradar-web` -> `roleradar-api` -> `MongoDB.Driver` and `roleradar-web` -> `roleradar-identity`, matching the `traceId` in logs, RFC 9457 problem documents, and `request_correlation_id` audit entries.
   - `SC-002` (`RR-72`): Allowlist/drop-list processor removes `url.query`, query/fragment parts of `url.full`, request/response headers, `db.query.text`, non-allowlisted attributes, and span `exception.message` / `exception.stacktrace` / free-text `StatusDescription` (keeping `exception.type` / `error.type`), leaving 0 sentinel secrets or untrusted domain text in exported spans.
   - `SC-003`: With `OTEL_EXPORTER_OTLP_ENDPOINT` unset, 0 OTLP exporters are registered and 0 non-loopback connections are opened; `CLI` and `Mcp.Stdio` register no exporter by default and flush within the bounded timeout when opted in, with 0 bytes on `Mcp.Stdio` `stdout`.
   ```bash
   pwsh ./scripts/test-fast.ps1
   pnpm --filter web run typecheck
   pnpm --filter web run lint
   pnpm --filter web run test
   ```
3. Run the Linux web gate, the isolated `real-stack` E2E OTLP proof (`RealStackEndToEndTests` with `otel/opentelemetry-collector-contrib:0.164.0@sha256:340c9ca6a36617084c6661bdc4f0fe92b7cda5a805d61673ec8db375fddc522b`, never the dev stack), and the full review gate from Git Bash before opening the Gate 2 PR:
   ```bash
   bash scripts/linux-web-check.sh
   pwsh ./scripts/test-e2e-real.ps1
   pwsh ./scripts/review-gate.ps1
   ```

### Gate 3 (P3): OTLP Structured Logs & Runtime Scrubbing Decision (`RR-70`, `RR-86`, `RR-72`, `ADR 0051`)

1. Verify `docs/adr/0051-otel-logs-and-runtime-scrubbing.md` records a concrete producer (or proves none exists) for all 7 `LogScrubber` rules, documents `BaseProcessor<LogRecord>` exception scrubbing evidence, and compares the OTLP log model with the Feature 013 console record shape.
2. Run the unit, contract, and web log privacy gate suites (`SC-002`, `SC-004`):
   ```bash
   dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Telemetry"
   dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~Redaction|FullyQualifiedName~Telemetry"
   pnpm --filter web run test
   ```
3. Run the Linux web gate and review gate from Git Bash:
   ```bash
   bash scripts/linux-web-check.sh
   pwsh ./scripts/review-gate.ps1
   ```

### Gate 4 (P4): Service Request & Datastore Health Metrics (`RR-69`)

1. Run the metrics and label-cardinality privacy gate tests (`SC-005`):
   ```bash
   dotnet test tests/RoleRadar.Tests.Unit -c Release --filter "FullyQualifiedName~Metrics|FullyQualifiedName~Telemetry"
   dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~Metrics|FullyQualifiedName~Telemetry"
   pnpm --filter web run test
   ```
2. Run the Linux web gate and review gate from Git Bash:
   ```bash
   bash scripts/linux-web-check.sh
   pwsh ./scripts/review-gate.ps1
   ```

### Gate 5 (P5): One-Command Local Dev Loop & Aspire Dashboard (`RR-71`)

1. Run the architecture layering and compose-drift tests (`SC-007`):
   ```bash
   dotnet test tests/RoleRadar.Tests.Architecture -c Release
   ```
2. Start the local telemetry dev loop per `ADR 0049` (using throwaway volumes only):
   - Option 2 (Aspire AppHost):
     ```bash
     dotnet run --project src/RoleRadar.AppHost
     ```
   - Fallback (if `ADR 0049` selected the opt-in compose overlay):
     ```bash
     docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml -f deploy/docker-compose.telemetry.yml up --build --wait
     ```
3. **Manual Dashboard Verification Check (`SC-006`)**: Because the Aspire Dashboard has no public query API to automate UI assertions, open the Aspire Dashboard on `127.0.0.1` (URL printed at startup) against the isolated fictional stack and record the verification evidence in the Gate 5 PR note:
   - **Loopback check**: Confirm Dashboard UI and OTLP ports bind exclusively to `127.0.0.1`.
   - **Resources**: `web`, `api`, `identity`, and `mongo` (`rs0`) show healthy on throwaway volumes (`roleradar-apphost-*`).
   - **Traces**: Sign in and transition a fictional posting; record the observed 32-character W3C `traceId` spanning `roleradar-web` -> `roleradar-identity` and `roleradar-web` -> `roleradar-api` -> `mongodb` (including the replica-set transaction).
   - **Structured Logs**: Click a span to view correlated structured logs carrying the same `TraceId` and `SpanId` with `[redacted]` placeholders and zero unredacted secrets.
   - **Metrics**: Inspect `http.server.request.duration`, `roleradar.store.readiness.status`, and `roleradar.store.operation.duration`.
   - **Volume isolation**: Record `docker volume ls` output showing `roleradar-mongo-data` and `roleradar-identity-data` were untouched.
4. Stop the local dev loop and run the final Linux web gate and review gate from Git Bash:
   ```bash
   bash scripts/linux-web-check.sh
   pre-commit run --all-files
   pwsh ./scripts/review-gate.ps1
   ```
