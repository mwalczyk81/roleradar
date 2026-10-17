# Contributing to RoleRadar

RoleRadar is governed by the [RoleRadar Constitution](./.specify/memory/constitution.md) (v1.1.0). Every contribution must adhere to Principles I through IX.

## Non-Negotiable Rules

1. **Privacy First (Principle I)**:
   - Never include real job postings, company names from your personal search, recruiter contacts, or salary figures in code, tests, fixtures, commit messages, or pull requests.
   - Install and enable the repository `pre-commit` and `pre-push` hooks before making commits:
     ```powershell
     uv tool install pre-commit
     pre-commit install --install-hooks -t pre-commit -t pre-push
     ```
   - Create your local private denylist at `%LOCALAPPDATA%\roleradar\private\denylist.txt` (one real company or contact name per line) so the `roleradar-denylist` hook scans for real names locally.
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
pre-commit install --install-hooks -t pre-commit -t pre-push
dotnet restore RoleRadar.sln --locked-mode
dotnet format RoleRadar.sln --verify-no-changes
dotnet build RoleRadar.sln -c Release -warnaserror
dotnet test RoleRadar.sln -c Release --collect:"XPlat Code Coverage"
pre-commit run --all-files
```
