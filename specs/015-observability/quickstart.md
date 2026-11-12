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
   - Aspire Dashboard (`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.5@sha256:e69a221d6d4ce3ee9bd74b9474c073dd154ef8df7eaa535c2f50f4a554adce54`) built-in views (traces, structured logs, metrics, resource graph) bound to `127.0.0.1`.
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

The loop is `src/RoleRadar.AppHost` (ADR 0049, ADR 0053). Identity, the API and the web app run from source next to a throwaway replica-set MongoDB, and the Aspire Dashboard shows their traces, structured logs and metrics. It is isolated from the real dev stack and needs no certificate: everything is loopback HTTP.

**Ports (all `127.0.0.1`, Compose's in-container port plus 10000, never the real stack's):**

| What | Host port |
| --- | --- |
| Identity, API, web | `15001`, `15002`, `15003` |
| MongoDB (`rs0`, `mongo:9.0.4`) | `37017` |
| Dashboard UI | `15888` |
| OTLP gRPC, OTLP HTTP, resource service | `15889`, `15890`, `15891` |

Resources, containers and volumes are named `roleradar-apphost-*`. The loop refuses to start (nothing is launched) if any of those ports is already bound, or if any name, volume or connection string points at `roleradar-mongo`, `roleradar-mongo-data`, `roleradar-identity-data` or port `27017`.

1. Run the architecture layering and compose-drift tests (`SC-007`):
   ```bash
   dotnet test tests/RoleRadar.Tests.Architecture -c Release
   ```
2. **Start the loop** (one command, from the repo root; the Dashboard login URL is printed at startup):
   ```bash
   dotnet run --project src/RoleRadar.AppHost
   ```
   Open the printed `http://127.0.0.1:15888/login?t=...` URL. The first start restores the web packages (`pnpm install --frozen-lockfile`) and builds the web app, which takes a minute or two. The Dashboard shows a "No trusted development certificate" banner; ignore it, nothing in this loop uses HTTPS.
3. **Stop the loop**: press `Ctrl+C` in the terminal that runs it. That stops the processes and removes the Mongo container, and the next start works as is: the Mongo data stays on the volume `roleradar-apphost-mongo-data` (so a seeded fictional posting survives a stop and start), and the root password is one fixed fictional value (`fictional-apphost-root-pw`, passed to Aspire as a parameter) so it always matches that volume. To start from empty, remove the volume with the commands below. A volume created before this fix holds a different, random root password and is rejected with `AuthenticationFailed`: remove it once with the same commands. Only if the terminal was closed abruptly, remove what was left (these commands touch only `roleradar-apphost-*` names; `-v` also removes the container's anonymous volume):
   ```powershell
   docker ps -a --filter "name=roleradar-apphost-" -q | ForEach-Object { docker rm -f -v $_ }
   docker volume ls -q --filter "name=roleradar-apphost-" | ForEach-Object { docker volume rm $_ }
   Remove-Item -Recurse -Force "$env:TEMP\roleradar-apphost-identity-data" -ErrorAction SilentlyContinue
   ```
4. **Fictional sign-in (do this before step 2, once).** The API accepts only the owner's `sub`, and Identity's data lives in the throwaway folder `%TEMP%\roleradar-apphost-identity-data`. Build the CLI first (the running loop locks the Identity binaries, so a `dotnet run` of the CLI cannot rebuild them), create the owner in that throwaway database, then pass the printed subject to the loop:
   ```powershell
   dotnet build src/RoleRadar.Cli -c Release
   New-Item -ItemType Directory -Force "$env:TEMP\roleradar-apphost-identity-data" | Out-Null
   $env:ROLERADAR_IDENTITY_DB = "Data Source=$env:TEMP\roleradar-apphost-identity-data\identity.db"
   dotnet src/RoleRadar.Cli/bin/Release/net10.0/RoleRadar.Cli.dll identity create-owner --username apphost-owner
   Remove-Item Env:ROLERADAR_IDENTITY_DB
   $env:ROLERADAR_APPHOST_OWNER_SUBJECT = "<the Owner subject (sub) value it printed>"
   ```
   Use a throwaway password and enter the printed Base32 secret into an authenticator app (delete that entry afterwards). Run `dotnet run --project src/RoleRadar.AppHost` in the same terminal so it sees `ROLERADAR_APPHOST_OWNER_SUBJECT`. Without it the API starts closed and every sign-in ends at "not authorized".
5. **A fictional posting (once the loop is up).** The web app lists postings but cannot create them, and a fresh throwaway database is empty. Seed one fictional company and posting through the repositories, the way `RealStackEndToEndTests` does (once is enough: the volume keeps it across stop and start). Save this outside the repo (for example `seed.cs`), with a `global.json` next to it that pins SDK `10.0.402`, and run it with `dotnet run seed.cs -- "<connection string>"`. The connection string is `mongodb://admin:fictional-apphost-root-pw@127.0.0.1:37017/?authSource=admin&authMechanism=SCRAM-SHA-256&directConnection=true` (the fixed fictional password of the throwaway Mongo, which listens on `127.0.0.1` only).
   ```csharp
   #:property PublishAot=false
   #:project <repo>/src/RoleRadar.Infrastructure/RoleRadar.Infrastructure.csproj
   #:package MongoDB.Driver@3.13.0
   using MongoDB.Driver;
   using RoleRadar.Domain.Common;
   using RoleRadar.Domain.Tracking;
   using RoleRadar.Infrastructure.Mongo;
   using RoleRadar.Infrastructure.Mongo.Repositories;

   var client = new MongoClient(args[0]);
   var db = client.GetDatabase("roleradar");
   await TrackingIndexInitializer.EnsureIndexesAsync(db, CancellationToken.None);
   var companyId = Guid.NewGuid();
   var company = Company.Create(id: companyId, ownerId: OwnerId.DefaultValue, name: "Acme Cloud Innovations", ownershipType: OwnershipType.Private, status: CompanyStatus.Target, description: "Fictional cloud tools", headquarters: "Seattle, WA", categoryTags: ["cloud"]);
   await new MongoCompanyRepository(client, db).UpsertCompanyAsync(company);
   var posting = Posting.Create(id: Guid.NewGuid(), ownerId: OwnerId.DefaultValue, companyId: companyId, title: "Senior Cloud Architect", disposition: PostingDisposition.Lead, resurfacePolicy: ResurfacePolicy.Never, requisitionId: "REQ-CLD-101", location: "Remote", score: 9.5m);
   await new MongoPostingRepository(client, db).UpsertPostingAsync(posting);
   ```
6. **Manual Dashboard Verification Check (`SC-006`, task `T074`).** The Dashboard has no query API for its views, so a person looks at them. Generate the activity first: open <http://127.0.0.1:15003/postings> (it sends you to Identity at `http://127.0.0.1:15001`), sign in as `apphost-owner` with the throwaway password and a current authenticator code (that is the fictional sign-in, `web` to `identity`), open the posting, choose **Change Disposition**, pick **Scored**, enter a reason such as `Fictional dashboard check`, and press **Apply Transition** (that is the fictional posting transition, `web` to `api` to `mongodb`). Then open the Dashboard at <http://127.0.0.1:15888> (use the login URL from the terminal) and record, in the Gate 5 PR note:

   | View (URL) | What it must show |
   | --- | --- |
   | **Loopback**: `netstat -ano` filtered to the ports above | Every listener is `127.0.0.1` (or `[::1]`), never `0.0.0.0`. `docker ps` shows Mongo as `127.0.0.1:37017->27017/tcp`. |
   | **Resources** (`/`) | `roleradar-apphost-mongo`, `roleradar-apphost-identity`, `roleradar-apphost-api` and `roleradar-apphost-web` are `Running`; `roleradar-apphost-web-installer` is `Finished`. Mongo's source shows `docker.io/library/mongo:9.0.4 ... --keyFile /etc/rs.key --bind_ip_all --replSet`. |
   | **Traces** (`/traces`) | A trace whose spans come from `roleradar-apphost-web` and `roleradar-apphost-identity` (the sign-in, started by the OIDC discovery or the token exchange). A trace from the posting transition with `roleradar-apphost-web`, `roleradar-apphost-api` and `roleradar-apphost-mongo` spans: the web `POST /postings/<id>`, the API span named `POST /api/v1/postings/{id:guid}/transition`, then the MongoDB `transaction` with `update postings`, `insert audit_log` and `commitTransaction`. Open a trace and copy its 32-hex trace id into the note. |
   | **Structured Logs** (`/structuredlogs`) | Records from the three services with a Trace ID and Span ID; opening a record from the sign-in shows `[redacted]` for token values (Identity's token events) and no password, client secret, session key or token anywhere. |
   | **Metrics** (`/metrics`, pick `roleradar-apphost-api`) | `http.server.request.duration` with data points (its `http.route` filter lists templates such as `/api/v1/postings/{id:guid}`, never a concrete id), plus `roleradar.store.readiness.status`, `roleradar.store.operation.duration` and `roleradar.store.readiness.duration` under the `RoleRadar.Store` meter. `roleradar-apphost-identity` has `roleradar.store.readiness.status` for SQLite, and `roleradar-apphost-web` has `http.server.request.duration`. |

   After stopping the loop (step 3), record `docker ps -a` and `docker volume ls` before and after the run: the real containers (`roleradar-mongo`, `deploy-identity-1`, `deploy-api-1`, `deploy-web-1`) and the volumes `roleradar-mongo-data` and `roleradar-identity-data` must be unchanged, and no `roleradar-apphost-*` item may remain.
7. Run the final gates from Git Bash:
   ```bash
   bash scripts/linux-web-check.sh
   pre-commit run --all-files
   pwsh ./scripts/review-gate.ps1 -IncludeE2E
   ```
