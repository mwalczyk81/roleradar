# Repository Settings & Branch Protection Checklist

This checklist records the GitHub repository settings required to enforce the [RoleRadar Constitution](../.specify/memory/constitution.md) on `origin`.

## 1. Branch Ruleset (`main`)

- **Require a pull request before merging**: Enabled
- **Require status checks to pass**:
  - `Windows (Unit, Architecture & PSScriptAnalyzer)` (`ci.yml`)
  - `Ubuntu (Build, Testcontainers, >=85% Coverage & Pre-Commit)` (`ci.yml`, job `ci-gate`)
  - `Analyze (csharp)` (`codeql.yml`)
  - `Dependency Review` (`dependency-review.yml`)
- **Require branches to be up to date before merging**: Enabled
- **Require linear history**: Enabled (Squash or Rebase merge)
- **Block force pushes & deletions**: Enabled

### What the required checks are

GitHub shows a workflow check as `<workflow name> / <job name>` on a pull request, but the name to select in the ruleset is the job name alone, exactly as written above (source: GitHub Actions).

- `Ubuntu (Build, Testcontainers, >=85% Coverage & Pre-Commit)` is the `ci-gate` job. It always runs (`if: always()`) and fails unless each of these jobs succeeded or was cleanly skipped by the path filter: `changes` (`Detect Changed Paths`, which must succeed), `pre-commit` (`Pre-Commit & Action Pins (All Changes)`), `ubuntu-build-and-test` (`Ubuntu (Build, Testcontainers & Container Security)`), `web` (`Web (Build, Lint & Test)`), `windows-verify` (`Windows (Unit, Architecture & PSScriptAnalyzer)`) and `e2e-real-stack` (`Real-Stack E2E (Identity, API, Web, Mongo)`). Requiring `ci-gate` therefore covers all of them; the Windows entry above is listed on its own as well.
- `ci.yml`, `codeql.yml` and `dependency-review.yml` run on every pull request to `main` (no path filter), so their checks always report.
- Do not require `Native Linux ARM64 (Multi-Stage Build & Image Parity)` (`arm64.yml`): it only runs when `Dockerfile`, `deploy/**`, `scripts/**` or its own workflow change, so a required check would stay pending on every other pull request. `macOS (Unit, Architecture & Script Portability)` runs only on the schedule and manual runs.
- The `ubuntu-build-and-test` job has a similar name to `ci-gate` (`Ubuntu (Build, Testcontainers & Container Security)`); do not select it by mistake.

## 2. Security & Automated Analysis

- **Private Vulnerability Reporting**: Enabled (`SECURITY.md`)
- **Secret Scanning & Push Protection**: Enabled (complemented by `.gitleaks.toml`, `.pre-commit-config.yaml`, and `scripts/denylist-check.ps1`)
- **Dependabot Alerts & Security Updates**: Enabled (`.github/dependabot.yml`)
- **CodeQL Analysis**: Enabled via `.github/workflows/codeql.yml`
