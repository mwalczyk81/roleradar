## Summary

- **Feature / ADR Reference**:
- **What Changed & Why**:

## Constitution Checklist

- [ ] **Principle I (Privacy First)**: Strictly fictional data only; hooks installed via `uv tool install pre-commit` and `pre-commit install --install-hooks -t pre-commit -t commit-msg -t pre-push`, and `pre-commit run --all-files` (including `gitleaks`, `zizmor`, `roleradar-forbidden-paths`, and `roleradar-denylist`) passes.
- [ ] **Principle II (Test-First)**: Failing tests were written first; `>= 85%` line coverage maintained on `Domain` and `Application`.
- [ ] **Principle III (Test-Enforced Layering)**: `RoleRadar.Tests.Architecture` passes (inward dependencies + every `ICommandHandler<,>` wrapped by `TransactionCommandDecorator<,>`).
- [ ] **Principle IV (Named Seams Only)**: Uses `INoteRepository` and plain `ICommandHandler`/`IQueryHandler` with typed `Result<T>` outcomes.
- [ ] **Principle VIII (Pinned Dependencies)**: Any dependency change updates `Directory.Packages.props` and `packages.lock.json` (`dotnet restore --locked-mode`).
- [ ] **Principle X (Standards First)**: Any new mechanism has a "Standards considered" row in the plan (what was checked, what was chosen, why); a hand-rolled one has an ADR naming the standard or library it lost to, and no upstream issue, PR or discussion was opened without the owner's approval.
