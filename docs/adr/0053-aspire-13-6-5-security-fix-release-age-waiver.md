# ADR 0053: Aspire 13.6.5 Security Fix and the Release-Age Waiver

- **Status**: Accepted
- **Date**: 2026-11-12
- **Feature**: `015-observability` (Gate 5, Jira RR-71)
- **Decided by**: Matt (the owner), 2026-11-11

## Context and Problem Statement

Gate 5 adds `src/RoleRadar.AppHost`, the one-command local dev loop chosen in [ADR-0049](./0049-local-observability-apphost-and-dashboard.md). It needs `Aspire.Hosting.AppHost`, `Aspire.Hosting.MongoDB` and `Aspire.Hosting.JavaScript`. The repository rule is that an adopted release is at least 14 days old (cutoff `2026-10-28` as of `2026-11-11`). Gate 1 pinned `13.6.4` (published `2026-10-24`, 18 days old). On `2026-11-11` restoring `Aspire.Hosting` `13.6.4` raised `NU1903`, which `Directory.Build.props` (`NuGetAudit`, `TreatWarningsAsErrors`) turns into a build error.

## The Advisory

| Field | Value |
| --- | --- |
| Identifier | `GHSA-f7vh-h5g3-7jrh` / `CVE-2026-120006` |
| Title | Microsoft Security Advisory CVE-2026-120006: Aspire Elevation of Privilege Vulnerability |
| Severity | High, CVSS 3.1 score 7.5 (`AV:L/AC:H/PR:L/UI:R/S:C/C:H/I:H/A:H`), CWE-201 |
| Published | `2026-11-10T23:24:48Z` |
| Affected | NuGet `Aspire.Hosting.AppHost` and `Aspire.Hosting`, every version below `13.6.5` |
| Fixed in | `13.6.5` |
| Scenario | A developer starts an AppHost, signs in to its Dashboard, and follows a resource link that routes to an attacker-controlled container behind a linked HTTPS YARP gateway. The Dashboard authentication cookie reaches that container, which can replay the session and run code as the developer. The advisory lists macOS (x64 and arm64) as the affected platform. |

Source: the GitHub advisory database entry for `GHSA-f7vh-h5g3-7jrh` (read with `gh api advisories/GHSA-f7vh-h5g3-7jrh`).

## Decision

The owner waived the 14-day release-age rule on `2026-11-11` for the Aspire `13.6.5` release, and Gate 5 pins it exactly:

| Package | Pinned | Published (NuGet catalog, UTC) | Age on 2026-11-12 | Licence |
| --- | --- | --- | --- | --- |
| `Aspire.Hosting.AppHost` | `13.6.5` | `2026-11-10T17:13:42Z` | 2 days | MIT |
| `Aspire.Hosting.MongoDB` | `13.6.5` | `2026-11-10T17:15:30Z` | 2 days | MIT |
| `Aspire.Hosting.JavaScript` | `13.6.5` | `2026-11-10T17:15:21Z` | 2 days | MIT |
| `Aspire.Hosting` (transitive) | `13.6.5` | `2026-11-10T17:13:41Z` | 2 days | MIT |
| `Aspire.AppHost.Sdk` (pinned in `global.json`) | `13.6.5` | `2026-11-10T17:12:03Z` | 2 days | MIT |
| `Aspire.Dashboard.Sdk.<rid>`, `Aspire.Hosting.Orchestration.<rid>` (added by the SDK for the build machine's RID) | `13.6.5` | `2026-11-10T17:13:22Z` to `17:15:58Z` | 2 days | MIT |

The waiver covers these `13.6.5` packages only. It does not change the 14-day rule for anything else, and `13.6.4` is not used anywhere.

Why waiting until `2026-11-24` was worse:

1. The rule exists to keep brand-new, unreviewed releases out. This release is the vendor's own fix for a published High CVE, it is MIT, and the advisory itself says every earlier version is affected. Waiting two weeks means running the known-vulnerable `13.6.4`, or not having the loop.
2. The alternative to the waiver was the compose overlay fallback (the standalone Dashboard container). It has no resource graph and cannot show the AppHost's resources, so the Gate 5 evidence (`SC-006`) would be incomplete for those two weeks.
3. A waiver with a recorded reason and an exact pin costs one ADR. Suppressing the warning, or pinning the affected `13.6.4`, costs a standing security gap.

## Never Suppress

- `NU1903` and every other NuGet audit warning (`NU1901` to `NU1904`) stay on, as errors, everywhere. No `NuGetAuditSuppress`, no `NoWarn` for them, no `WarningsNotAsErrors` for them. `AppHostComposeParityArchitectureTests` fails the build if one appears in any `.csproj`, `.props`, `.targets` or `NuGet.Config`.
- The two narrow diagnostics that are not advisories are recorded in ADR 0049's Gate 5 addendum: `ASPIRE010` (optional Aspire CLI bundle) in the AppHost project, and the experimental-API diagnostic `ASPIREMONGODB001` on the single `WithReplicaSet` call.

## Does the Advisory Cover the Dashboard Container Image?

The advisory lists only two affected NuGet packages, `Aspire.Hosting.AppHost` and `Aspire.Hosting`, and says the affected hosting code is in `Aspire.Hosting`. It does not mention the `mcr.microsoft.com/dotnet/aspire-dashboard` image. That is the extent of what can be shown: the advisory does not name the image, and this ADR does not claim the image is or is not affected beyond that. The AppHost does not run the container at all: it uses the Dashboard shipped in `Aspire.Dashboard.Sdk` `13.6.5`. The container is the pinned standalone receiver (documented fallback and real-stack OTLP receiver), and its pin moves to the same release:

`mcr.microsoft.com/dotnet/aspire-dashboard:13.6.5@sha256:e69a221d6d4ce3ee9bd74b9474c073dd154ef8df7eaa535c2f50f4a554adce54`

Verification of the digest against the registry: `docker buildx imagetools inspect mcr.microsoft.com/dotnet/aspire-dashboard:13.6.5` returns the manifest list `sha256:e69a221d...` (linux/amd64 `sha256:a7309770...`, linux/arm64 `sha256:b46a791c...`), and inspecting the image by that digest returns the same manifest list. The MCR catalog lists the same digest under the moving tag `13`, last modified `2026-11-11T03:20:17Z`.

## Consequences

- **Good**: the AppHost restores and builds with zero audit warnings on a patched release; the exact pins (`Directory.Packages.props`, `global.json`) are enforced by an architecture test.
- **Good**: the waiver, its scope and its date are on record, so the next release-age exception has a precedent that is narrow, not a habit.
- **Neutral**: `13.6.5` is two days old. `13.6.6` or later is not adopted until it is at least 14 days old, unless another advisory forces the same decision.
