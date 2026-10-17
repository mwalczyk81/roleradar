# Repository Settings & Branch Protection Checklist

This checklist records the GitHub repository settings required to enforce the [RoleRadar Constitution](../.specify/memory/constitution.md) on `origin`.

## 1. Branch Ruleset (`main`)

- **Require a pull request before merging**: Enabled
- **Require status checks to pass**:
  - `Windows (Unit, Architecture & PSScriptAnalyzer)` (`ci.yml`)
  - `Ubuntu (Build, Testcontainers, >=85% Coverage & Gitleaks)` (`ci.yml`)
  - `Analyze (csharp)` (`codeql.yml`)
  - `Dependency Review` (`dependency-review.yml`)
- **Require branches to be up to date before merging**: Enabled
- **Require linear history**: Enabled (Squash or Rebase merge)
- **Block force pushes & deletions**: Enabled

## 2. Security & Automated Analysis

- **Private Vulnerability Reporting**: Enabled (`SECURITY.md`)
- **Secret Scanning & Push Protection**: Enabled (complemented by `.gitleaks.toml`, `.pre-commit-config.yaml`, and `scripts/denylist-check.ps1`)
- **Dependabot Alerts & Security Updates**: Enabled (`.github/dependabot.yml`)
- **CodeQL Analysis**: Enabled via `.github/workflows/codeql.yml`
