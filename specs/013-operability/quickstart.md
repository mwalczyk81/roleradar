# Quickstart: Validate Operability (013)

Use isolated stacks and test containers only. Never point any of this at the developer's running stack, and never use real data.

## Automated validation

| Story | Command | Proves |
| --- | --- | --- |
| all | `dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror` | builds clean; removing the `Actor` defaults compiles |
| all | `pwsh ./scripts/test-fast.ps1` | architecture rules (Observability references, no defaulted `Actor`), formatter and scrubber units, envelope and health contracts, `IAuditLogReader` contract suite |
| US1 | `pnpm --filter web run typecheck`, `lint`, `test` | web logger, scrubber, correlation, retry policy; `no-console` rule; compose log caps tooling test |
| US1, US2 | `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~Redaction"` | no sentinel secret in any host's captured output (FR-013) |
| US2 | `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~Actor"` | every API write records the subject |
| US2 | `dotnet test tests/RoleRadar.Tests.Contract -c Release --filter "FullyQualifiedName~SignIn"` | one event per sign-in outcome, no username on failures |
| US3 | `dotnet test tests/RoleRadar.Tests.Integration -c Release --filter "FullyQualifiedName~DatastoreOutage"` | 503 envelope within 5 s; recovery within 30 s without restart |
| US1 | `pwsh ./scripts/test-e2e-real.ps1` (runs `operability.real-stack.spec.ts`) | web and API log lines share the response's correlation id |
| phase end | `bash scripts/linux-web-check.sh`, `pre-commit run --all-files`, `pwsh ./scripts/review-gate.ps1` | full gate; report numbers from output only |

## Manual walk-through (fictional data, isolated stack)

1. Start the stack the usual way (`scripts/start-services.ps1`, then the apps profile). `docker compose logs api identity web` shows one JSON object per line, each with `ts`, `level`, `service`, `msg`.
2. Run the API outside compose (`dotnet run --project src/RoleRadar.Api`) with `ROLERADAR_LOG_FORMAT` unset: the same records print as readable lines.
3. Open a posting URL with an unknown id. Take `X-Correlation-ID` from the browser's network panel and search `docker compose logs` for it: lines from `web` and `api` carry it.
4. Sign in with a wrong password once, then correctly. The `identity` log shows one `SignInFailed` (no username) and one `SignInSucceeded` with `subject`.
5. Change a posting's disposition in the web app, then run `roleradar audit list --limit 5`: the newest entry's actor is your subject and it shows `req=<id>`.
6. Optional viewer: `docker compose -f deploy/docker-compose.logs.yml up -d`, open `http://127.0.0.1:5004`, and see the same records; `down` removes it. It does not exist in the demo stack.
7. Readiness: `docker compose ps` shows `api` and `identity` healthy, now judged by `/health/ready`. Do not stop the real Mongo to see the 503; the DatastoreOutage test covers that on a throwaway container.

Expected outcomes are the acceptance scenarios in [spec.md](spec.md); exact shapes are in [contracts/](contracts/).
