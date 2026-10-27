## Summary

- **Feature / ADR Reference**:
- **What Changed & Why**:

## Constitution Checklist

- [ ] **Principle I (Privacy First)**: Strictly fictional data only; hooks installed via `uv tool install pre-commit` and `pre-commit install --install-hooks -t pre-commit -t commit-msg -t pre-push`, and `pre-commit run --all-files` (including `gitleaks`, `zizmor`, `roleradar-forbidden-paths`, and `roleradar-denylist`) passes.
- [ ] **Principle II (Test-First)**: Failing tests were written first; `>= 85%` line coverage maintained on `Domain` and `Application`.
- [ ] **Principle III (Test-Enforced Layering)**: `RoleRadar.Tests.Architecture` passes (inward dependencies + every `ICommandHandler<,>` wrapped by `TransactionAndAuditCommandDecorator<,>`).
- [ ] **Principle IV (Named Seams Only)**: Uses `INoteRepository` and plain `ICommandHandler`/`IQueryHandler` with typed `Result<T>` outcomes.
- [ ] **Principle VIII (Pinned Dependencies)**: Any dependency change updates `Directory.Packages.props` and `packages.lock.json` (`dotnet restore --locked-mode`).
