## Summary

- **Feature / ADR Reference**:
- **What Changed & Why**:

## Constitution v1.1.0 Checklist

- [ ] **Principle I (Privacy First)**: Strictly fictional data only; `pwsh ./scripts/pre-commit.ps1` and `gitleaks` pass.
- [ ] **Principle II (Test-First)**: Failing tests were written first; `>= 85%` line coverage maintained on `Domain` and `Application`.
- [ ] **Principle III (Test-Enforced Layering)**: `RoleRadar.Tests.Architecture` passes (inward dependencies + every `ICommandHandler<,>` wrapped by `TransactionAndAuditCommandDecorator<,>`).
- [ ] **Principle IV (Named Seams Only)**: Uses `INoteRepository` and plain `ICommandHandler`/`IQueryHandler` with typed `Result<T>` outcomes.
- [ ] **Principle VIII (Pinned Dependencies)**: Any dependency change updates `Directory.Packages.props` and `packages.lock.json` (`dotnet restore --locked-mode`).
