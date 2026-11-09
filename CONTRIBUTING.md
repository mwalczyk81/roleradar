# Contributing to RoleRadar

RoleRadar is governed by the [RoleRadar Constitution](./.specify/memory/constitution.md). Every contribution must adhere to Principles I through IX.

Everyone taking part follows the [code of conduct](CODE_OF_CONDUCT.md). All documentation is indexed in [docs/README.md](docs/README.md).

## Try the demo first

The quickest way to see the app is the demo over fictional data ([docs/demo.md](docs/demo.md)):

```bash
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait
```

## Non-Negotiable Rules

1. **Privacy First (Principle I)**:
   - Never include real job postings, company names from your personal search, recruiter contacts, or salary figures in code, tests, fixtures, commit messages, or pull requests.
   - Install and enable the repository `pre-commit` and `pre-push` hooks before making commits:

     ```powershell
     uv tool install pre-commit
     pre-commit install --install-hooks -t pre-commit -t commit-msg -t pre-push
     ```

   - Create your local private denylist at `%LOCALAPPDATA%\roleradar\private\denylist.txt` (see [docs/denylist.example.txt](docs/denylist.example.txt); one real company or contact name per line) so the `roleradar-denylist` hook scans for real names locally.
   - Always run `pre-commit run --all-files` before opening a pull request.
2. **Test-First Development (Principle II)**:
   - Write unit, architecture, contract, or integration tests **first** and verify they fail before writing production implementation code.
   - Integration and contract tests use `Testcontainers.MongoDb` (`mongo:8.0.34` replica set `rs0`) and must never connect to or mutate a developer's running local containers.
   - Line coverage across `RoleRadar.Domain` and `RoleRadar.Application` must remain `>= 85%`.
3. **Test-Enforced Layering & Named Seams (Principles III & IV)**:
   - `Domain` -> `Application` -> `Infrastructure` -> `Mcp.Stdio` / `Cli`.
   - Every `ICommandHandler<,>` registration must be wrapped by `TransactionAndAuditCommandDecorator<,>`.
   - Expected failures (`NotFound`, `StaleVersionToken`, `SectionNotFound`, `AmbiguousHeading`, `ImportConflict`, `InvalidUtf8`, `DatastoreOffline`) must be returned as typed `Result<T>` values, never thrown as exceptions.

## Developer Workflow

```powershell
uv tool install pre-commit
pre-commit install --install-hooks -t pre-commit -t commit-msg -t pre-push
dotnet restore RoleRadar.sln --locked-mode
dotnet format RoleRadar.sln --verify-no-changes
dotnet build RoleRadar.sln -c Release -warnaserror
dotnet test RoleRadar.sln -c Release --collect:"XPlat Code Coverage"
pre-commit run --all-files
```

### Fast Test Loop

During local development, you can run the fast test loop (`Architecture`, `Unit`, and `Contract` test suites, omitting `Integration` tests):

```powershell
pwsh ./scripts/test-fast.ps1
```

Or run all tests with:

```powershell
pwsh ./scripts/test-all.ps1
```

Continuous Integration (CI) always runs the complete test suite across all projects:

```powershell
dotnet test RoleRadar.sln -c Release
```

### Review Gate Script

Before submitting a pull request, run the cross-platform review gate script to execute all verification gates against a clean clone:

```powershell
pwsh ./scripts/review-gate.ps1
```

`-Branch <branch-name>` checks out another branch in the clone, and `-IncludeE2E` adds the real-stack Playwright suite. `review-gate.ps1` performs the following automated checks without printing noisy build logs:

1. Clones the repository to an isolated temporary directory.
2. Restores dependencies with `--locked-mode`.
3. Verifies code formatting with `dotnet format --verify-no-changes`.
4. Compiles the solution with warnings as errors (`-warnaserror`).
5. Type-checks the web app and executes all tests (`Unit`, `Architecture`, `Contract`, `Integration`) via `scripts/test-all.ps1`.
6. Computes CI-equivalent merged `RoleRadar.Domain` + `RoleRadar.Application` coverage (enforcing `>= 85%`).
7. Builds the five Linux container images (CLI, MCP, Identity, web and API), measures their sizes and smoke-tests the web image.
8. Scans the images it built with the pinned Trivy and runs the scan test, which cannot pass on a skipped scan ([ADR 0036](docs/adr/0036-test-lanes-and-gate-scan.md)).
9. Prints a concise summary table and cleans up the temporary clone.

## Test Data Isolation & Developer Stack Protection

RoleRadar enforces strict isolation so tests can never corrupt, modify, or delete a developer's real data:

1. **Ephemeral Ports & Namespaces**:
   - Contract and integration tests instantiate isolated `Testcontainers.MongoDb` containers on randomized, ephemeral host ports.
   - Container-level tests (`IsolatedContainerMongoFixture`) start dedicated Mongo containers on dynamic bridge networks (`rrtest-net-*`) with unique project (`rrtest-proj-*`), container (`rrtest-mongo-*`), and volume (`rrtest-vol-*`) identifiers.
   - All test databases are prefixed with `rrtest_` and torn down after each test.

2. **Hard Dev Stack Guards**:
   - `ExternalMongoSafetyGuard` and `ContainerTestHarness` actively block any connection resolving to `127.0.0.1:27017`, `localhost:27017`, or `host.docker.internal:27017`. Tests throw `DevStackTargetException` immediately if an attempt is made to reach the default dev stack, unless explicitly bypassed via `ROLERADAR_ALLOW_DEV_STACK=1`.
   - `ValidateDatabaseNameForDrop` strictly forbids dropping or wiping the default database `roleradar`.

3. **External Volume Immutability**:
   - In `deploy/docker-compose.yml`, `roleradar-mongo-data` is declared `external: true`. As an external volume, `docker compose down -v` is prevented by the Docker engine from removing it.
   - `scripts/start-services.ps1` automatically ensures the external volume exists via `docker volume create roleradar-mongo-data` if it has not yet been initialized.

4. **Dev Stack Safety Canary**:
   - `scripts/review-gate.ps1` and CI execute `scripts/verify-devstack-canary.ps1` before and after test execution.
   - If a dev stack is reachable on port 27017, the canary records all database names and collection document counts, and fails the verification gate if any database outside `rrtest_` prefixes was created, dropped, or modified.

## Backup and Restore

Encrypted backup, the two-pass restore and the overwrite safeguards are described in [docs/operations/backup-restore.md](docs/operations/backup-restore.md).
