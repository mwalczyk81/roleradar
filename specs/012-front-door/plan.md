# Implementation Plan: Front Door

**Branch**: `012-front-door` | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

## Summary

Add a self-contained demo compose project (`deploy/demo/`), a `roleradar demo` CLI group (`seed`, `credentials`, `totp`) that seeds a fictional dataset through the real repositories and manages one throwaway owner whose password and TOTP key are generated, never committed and never logged, a shared `DemoModeGuard`, a gated Playwright screenshot spec, a rewritten README with its surrounding files, a link and consistency test, a changelog with release text, and local pre-announce checks. No product behavior changes. The real compose files and default image configuration gain nothing.

## Technical Context

**Language/Version**: C# on .NET 10 (CLI, Infrastructure, Identity, API); TypeScript on Node 22 (web tests, Playwright, Vitest)
**Primary Dependencies**: existing only (System.CommandLine, Mongo driver, ASP.NET Core Identity, Playwright, Vitest). No new image: the one-shot steps reuse the already pinned `mongo:8.0.34` (it has `openssl`, `base64`, `head`, `tr`; research R-009).
**Storage**: MongoDB 8.0.34 replica set in the demo project (database `roleradar-demo`, volume `roleradar-demo-mongo`); Identity SQLite in volume `roleradar-demo-identity`
**Testing**: xUnit (Unit, Architecture, Contract; Integration for the seed and compose wiring), Vitest (`web/tests/tooling`), Playwright (gated spec)
**Target Platform**: Docker Compose v2 on Windows, macOS, Linux; Linux CI
**Project Type**: existing multi-host solution plus `web/`
**Performance Goals**: demo up in under 3 minutes warm, under 10 cold; the measured time is recorded (SC-001a)
**Constraints**: fictional data only; no committed credential; never touch `roleradar-mongo`, port 27017 or database `roleradar`; no new test projects; LF endings; no new custom script unless justified (FR-012, reviewed in the PR)
**Scale/Scope**: 14 companies, 48 postings, 20 applications, 6 notes, 8 search runs

## Constitution Check

| Principle | Status | Why |
| --- | --- | --- |
| I Privacy first | Pass | The dataset is invented. No credential is committed: the password and TOTP key are generated at seed time, stored only as a hash and a key in the demo Identity volume, and printed once to an interactive terminal by `demo credentials`. The committed demo constants are the username, the user id and the database name, none of which is a secret. A test scans tracked files for a demo password or TOTP secret, so ADR 0017 ("no seed passwords or TOTP keys in files") still holds and no gitleaks allowlist exists. History and image checks stay local. |
| II Tests first, isolated containers | Pass | Every new unit starts with a failing test (task order enforces it). The seed Integration test uses Testcontainers and a throwaway database; the demo guard is never pointed at the dev stack. |
| III Layering enforced | Pass | Seed in `Cli`, guard in `Infrastructure`. Architecture tests pin that `Domain` and `Application` hold no demo code, that no production project references the Integration test project, and that demo constants are referenced only from `Cli/Demo` and the guard. |
| IV Named seams only | Pass | No generic repository, mediator or bus. If the seed needs a natural-key lookup the existing repositories lack, it is added as a named method on the existing repository port (R-013), not a new abstraction. |
| V Untrusted posting text | Pass | Seeded text goes through the existing React text nodes and the locked-down note renderer; seeded notes use no raw HTML or images. |
| VI stdout hygiene | Pass | The `demo` commands are CLI only; `Mcp.Stdio` is untouched. |
| VII Durable data, destructive confirmation | Pass | Teardown removes only the demo project's own, explicitly named volumes. The seed refuses non-demo databases. Nothing wipes or restores real data. |
| VIII ADRs, pins | Action | ADR 0039 records the demo stack, the guard and the credential model. The certificate image is pinned by tag and digest. Dependabot docker coverage for `deploy/demo/` is added if the existing config does not already cover the directory. |
| IX .NET 10, nullable, warnings as errors, UTC, UUIDv7, `owner_id` | Pass, one noted exception | Every id the seed creates is `Guid.CreateVersion7()`, found-or-created by natural key. Timestamps are UTC. The seed owner is `OwnerId`. The single exception is the demo user id: a fixed constant, because the demo compose file must name the allowed subject before Identity exists. It is v7-shaped (produced once by `CreateVersion7` at authoring time), non-secret, and an Identity user id, not an owner id or a document id. See Complexity Tracking. |

Post-design re-check: no new violation from D1 to D9.

## Design

### D1. Demo compose project (`deploy/demo/docker-compose.yml`)

A standalone file, not an overlay, so nothing from the real files can leak in. Top-level `name: roleradar-demo`. Services:

- `demo-certs`: one-shot, the pinned `mongo:8.0.34` image (has `openssl`), writes a throwaway certificate and key (PFX, PEM certificate, PEM key; names `identity.localhost`, `api.localhost`, `localhost`) into volume `roleradar-demo-certs`. Nothing key-like is committed.
- `demo-mongo`: `mongo:8.0.34` (same pin and `GLIBC_TUNABLES` workaround as the real file), no `container_name`, no published port, volume `roleradar-demo-mongo`, the same self-initiating replica-set healthcheck with host `demo-mongo:27017`. Not `external`: the demo owns this volume and `down -v` is the intended teardown.
- `demo-seed`: one-shot, CLI image, runs `demo seed` (guard, data, then creates the owner with a generated, discarded password and TOTP key). It prints counts only. Mounts the identity volume.
- `demo-cli`: profile `tools`, same image, mounts the identity volume, used for `demo credentials` and `demo totp` (so the one-shot's command is never overridden).
- `identity`, `api`, `web`: same Dockerfiles as `docker-compose.apps.yml`. Demo environment: issuer `https://identity.localhost:5101`, network aliases `identity.localhost` and `api.localhost`, `ROLERADAR_DEMO=1`, `ROLERADAR_MONGO_DATABASE=roleradar-demo`, allowed owner subject set to the fixed non-secret user id, client secrets set to compose-level generated values if the existing apps need any (R-014; see below).
- Host ports: web `127.0.0.1:5103`, identity `127.0.0.1:5101`, api `127.0.0.1:5102`, distinct from the real stack's 5001 to 5003.

The real apps compose file requires `ROLERADAR_WEB_CLIENT_SECRET`, `ROLERADAR_HEADLESS_CLIENT_SECRET` and `SESSION_ENCRYPTION_KEY`. Committing fixed values for them would also be committed secrets. R-014 resolves this: a one-shot `demo-secrets` step generates them into a volume at first start (same pattern as the certificates) and the services read them from files or from the environment of a wrapper entrypoint. If an image can only read environment variables, the compose file uses a documented variable passed on the command line with `openssl rand`-style generation in the documented command (`$(…)`), and the one-command rule is kept by a small compose-level `env_file` written by `demo-secrets` to the secrets volume. Decision deferred to T004 with a fallback recorded in research.

One documented command:

```text
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait
```

Then `docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml --profile tools run --rm demo-cli demo credentials` prints the sign-in details once. Teardown: `... down -v`. "Try the demo" says the first run builds four images.

### D2. `DemoModeGuard` (Infrastructure, next to `MongoOptions`)

A pure, fail-closed function. Inputs: the `ROLERADAR_DEMO` value, the database name, the connection string, and the allowed host. It allows only when demo mode (`ROLERADAR_DEMO=1`) is exactly `1`, the database name equals `roleradar-demo` (ordinal) and the connection's single server host equals the allowed host. Two entry points:

- `CheckForDemoStack(...)`: production callers use this. It passes the fixed demo host (`demo-mongo`) itself; callers never see or pass a host.
- `Check(..., allowedHost)`: used by the guard tests and the seed Integration test (Testcontainers host). An architecture test asserts that no production code outside the guard calls this overload.

Errors never echo the connection string or host. Callers:

- `roleradar demo seed`, `demo credentials`, `demo totp` (the guard first, then any work).
- API start-up: if demo mode is on, call the guard and stop start-up on refusal. If demo mode is off and the database name is `roleradar-demo`, also stop (the demo database is never served as real). With neither, nothing changes.
- Identity has no Mongo and does not reference Infrastructure, so it has no start-up check; the demo commands' guard (demo mode, demo database, host `demo-mongo`, which exists only in the demo compose network) ties the demo account to the demo stack.

Tests (Unit, first): table-driven refusals (real name, empty, `roleradar-demo2`, `Roleradar-Demo`, mode `0`/missing/`true`, hosts `127.0.0.1:27017`, `host.docker.internal`, `mongo`, a second server in the string), the single accept case, the explicit allowed-host case, and "message never contains the input". Architecture test: the guard type is in Infrastructure; API and CLI call it; the allowed-host overload has no other production callers.

### D3. Throwaway account, no committed credential

Identity's flow is unchanged (password then TOTP, lockout and rate limits per ADR 0017). The demo provisions the same entities the interactive command does, through new methods on `OwnerProvisioningService` used only from `Cli/Demo`: `ProvisionOwnerWithIdAsync(userId, username, password, authenticatorKey)` and `ResetCredentialsAsync(userId, password, authenticatorKey)` (also rotates the security stamp so earlier sessions end).

- `demo seed`: after the dataset, creates the user (fixed username `demo-owner`, fixed user id) if it does not exist, with a password and key generated by `RandomNumberGenerator` and **discarded**: nothing prints them or logs them.
- `demo credentials`: resets the account with a new generated password (meets the Identity password options, R-002) and a new 20-byte Base32 key, then prints username, password, TOTP secret and the `otpauth://` URI once to the console. It refuses unless stdin and stdout are interactive terminals (so it cannot be redirected to a file or captured by a log driver), writes through `Console` only (never `ILogger`), and runs through `demo-cli`, where `docker compose run` allocates a terminal. Running it again resets the account again.
- `demo totp`: reads the account's authenticator key from the Identity store and prints the current 6-digit code (RFC 6238 over the framework `HMACSHA1`, about 15 lines, unit-tested on the RFC vectors). Justification: a visitor without an authenticator app can still sign in.
- Non-secret committed constants: username `demo-owner`, the v7-shaped user id, database name `roleradar-demo`, host `demo-mongo`. In production C# they are referenced only from `src/RoleRadar.Cli/Demo/` and the guard; docs, compose, the screenshot spec and tests take them from the compose file and `docs/demo.md`.

Tests (first): the credentials command refuses a redirected output; it writes nothing to a recording logger provider; a password generated by the generator satisfies the Identity options; reset creates credentials that the real password check and TOTP check accept and invalidates the previous ones; TOTP vectors. "No committed secret" test (Unit): scans every tracked file (via `git ls-files` when available, else a directory walk excluding `bin`, `obj`, `node_modules`, `.git`) for an `otpauth://` URI, a Base32 key next to the demo username, and `password`-style assignments in any file under `deploy/demo/`, `docs/demo.md`, `src/RoleRadar.Cli/Demo/`, and fails with file names only. "Default config has no demo account" test: the real compose files, `appsettings*.json` of Identity and API and the Dockerfiles contain none of the demo username, user id or `roleradar-demo`. Architecture tests: demo constants referenced only from `Cli/Demo` and the guard; `Cli/Demo` has no string-literal owner id; `Cli/Demo` credentials code uses no `ILogger`.

### D4. Seed (`src/RoleRadar.Cli/Demo/`)

`DemoDataset` (pure, deterministic content: names, titles, relative day offsets) and `DemoSeeder` (writes through the existing Mongo repositories). Dates are `asOf - n days`, `asOf` defaults to today UTC (`--as-of` overrides). Idempotency by natural key: companies by name, career sources by company, provider and URL, postings by canonical URL (`https://jobs.example.com/...`), one application per posting, notes by title and area, search runs by (started date), criteria revision by its label. Each is looked up first and created with `Guid.CreateVersion7()` only when missing; an existing application's timeline and an existing posting's signals are never appended to. If any demo data already exists the seeder reports counts and changes nothing, including on a different `--as-of` (teardown first to refresh dates). Writing goes through the repositories so derived fields come from the choke point; nothing writes Mongo documents directly. R-013 checks that the repositories offer the lookups and accept backdated timestamps, with the fallback of adding a named lookup method to the existing port.

Unit tests (no Mongo) on `DemoDataset`: counts, dispositions, event types, window checks, notes without raw HTML or images, every name and host from the fictional list. Integration test (Testcontainers, throwaway database, the allowed-host guard overload): seed twice, row counts stable and every id version 7; the second run reports "already seeded"; `reconcile-read-models` in dry run reports no changes; seeding with the real database name is refused before any write.

### D5. Screenshots (`web/tests/e2e/screenshots.spec.ts`)

Gated by `ROLERADAR_SCREENSHOT_DIR`, plus `ROLERADAR_DEMO_URL`, `ROLERADAR_DEMO_USERNAME`, `ROLERADAR_DEMO_PASSWORD`, `ROLERADAR_DEMO_TOTP_SECRET` (read from the environment; the owner copies them from the credentials command's output into the shell session, nothing is written to a file). The spec refuses a base URL that is not the demo address (`localhost:5103` or a `*.localhost` host), ignores HTTPS errors (the Playwright config already sets `ignoreHTTPSErrors: true`; the spec asserts it), signs in through the real flow with a TOTP code computed from the secret (helper in `web/tests/e2e/support`, R-003), captures seven views (Insights, cut at a section boundary; the list reached from its "Interviewed" stage link, checked against the link's count; postings list; the hero posting; its application; the hero company; a note) in light and dark at a fixed viewport using the existing `capture-settle` helper, and writes names listed in `docs/images/manifest.json`. Command (documented in `docs/demo.md`):

```text
ROLERADAR_SCREENSHOT_DIR=docs/images ROLERADAR_DEMO_URL=https://localhost:5103 pnpm --filter web exec playwright test screenshots.spec.ts
```

(with the three credential variables set in the same shell). The default run skips the spec with a reason. Review: the owner opens all 14 images before commit; the seed's names are the only invented text in them; the US5 image-metadata check runs before commit. A tiny placeholder image (`docs/images/placeholder.png`, a 1x1 PNG) is committed early so the README's image references resolve while the link check runs; the manifest test requires the placeholder to be removed once all 14 images exist and the README to reference the real ones. GIF: out of scope (R-006).

### D6. README and surrounding files

Root README target about 150 lines: pitch (two lines), badges, hero `<picture>` (light and dark), "Try the demo" (the four steps; first run builds four images; accept the self-signed certificate warning once per origin, web and `identity.localhost`; all demo data is fictional; the measured typical time), Architecture (new mermaid: MCP server, CLI, API, Identity, web UI, OData read seam, Application, Infrastructure, Mongo, encrypted backup), How it was built (with open AI-agent disclosure), Evidence, Status and limits, Documentation (links to `docs/README.md`, the ADR index, the feature 008 worked example), Quickstart (shortened real-stack start), CLI and MCP reference (command list, one line each, links), Environment variables (table kept verbatim so `MongoOptionsReadmeConsistencyTests` passes), Security and contributing.

Moved to `docs/`: `mcp-clients.md`, `mcp-tools.md`, `cli-reference.md`, `insights.md`, `operations/backup-restore.md`, plus new `demo.md` (command, credentials command, TOTP code, ports, certificate warnings, fictional-data disclaimer, teardown, how the guard works, how Duende and the password plus TOTP flow handle the account, screenshot procedure, "where the non-secret constants come from"), `threat-model.md` (one page, including the line that the demo account is throwaway and demo-only), `pre-announce.md`; the v0.1.0 release text is drafted in `specs/012-front-door/release-v0.1.0.md` (owner direction). `docs/README.md` indexes all of `docs/`.

`.github/ISSUE_TEMPLATE/config.yml` (blank issues off, `SECURITY.md` link); `CODE_OF_CONDUCT.md` (Contributor Covenant 2.1, contact from `SECURITY.md`); CONTRIBUTING gains the code-of-conduct link and the `docs/README.md` pointer.

### D7. Link and consistency check

markdownlint-cli2 checks fragments but not file targets, images or URLs, so a Vitest test is needed: `web/tests/tooling/front-door-links.test.ts`. It resolves relative links, `#anchors` (GitHub slugs) and image references (including `<picture>`/`<source srcset>`/`<img src>`) in the root README, `CONTRIBUTING.md` and `docs/README.md`, checks badge URLs with the `URL` parser (https, allowlisted hosts, no network), asserts the three files give identical command blocks for the demo, fast loop and review gate, asserts the certificate-warning and fictional-data statements appear in `docs/demo.md` and the README, and holds the manifest and placeholder rules (D5) and the changelog PR-number check (D8). Runs in the existing `pnpm --filter web run test` and so in CI. Tests first, shown red against current files and a deliberately broken fixture.

### D8. Changelog and release text

`CHANGELOG.md` (Keep a Changelog layout), written once by hand from `git log`: 001 (#1), 002 (#2), 003 (#3), 004 (#9 to #15), 005a (#20), 005b (#28), 006 (#41), 007 (#51), 008 (#53), 009 (#54), 010 (#55), 011 (#61 to #66), 012 (number filled by the owner at merge); Dependabot bumps summarized in one line. Release text in `docs/releases/v0.1.0.md`. Test: every `#NNN` in the changelog is in the `git log` subjects (skipped with a reason on shallow clones). No release automation.

### D9. Pre-announce checks (local)

- gitleaks (`docs/pre-announce.md`): the pre-commit hook scans the working tree, so the one-time command runs the same pinned version (v8.30.1) over history through its container image: `docker run --rm -v "${PWD}:/repo" zricethezav/gitleaks:v8.30.1 git /repo --config /repo/.gitleaks.toml --redact --no-banner`; tag and digest recorded in R-007.
- denylist: extend the existing `scripts/denylist-check.ps1` with `-Mode History` (all commits' content and messages) and `-Mode ImageMetadata` (printable strings in `docs/images/*` and file names). Counts and file names only, never matched text; exit 1 on a match; reuses the script's term loading (comments, blanks, BOM). Tests extend the script's existing tests with a throwaway repository and throwaway terms.
- `docs/threat-model.md`: one page.
- CI demo smoke (R-010): a job in `nightly-quality.yml` runs the documented command on Linux, checks the seed exit code and counts line, the web health endpoint and the sign-in page. It does not sign in, because the credentials command refuses non-interactive output by design; sign-in is verified by the owner (T021) and by the screenshot run.

## Project Structure

```text
deploy/demo/docker-compose.yml
specs/012-front-door/ (spec, plan, research, data-model, quickstart, contracts/cli-demo.md, tasks, checklists)
src/RoleRadar.Infrastructure/Mongo/DemoModeGuard.cs
src/RoleRadar.Cli/Demo/ (DemoAccount, DemoDataset, DemoSeeder, CredentialPrinter, TotpCode)
src/RoleRadar.Cli/Demo/DemoCommand.cs   (inside Cli/Demo so demo constants stay referenced only there)
src/RoleRadar.Identity/Services/OwnerProvisioningService.cs   (+ProvisionOwnerWithIdAsync, +ResetCredentialsAsync)
src/RoleRadar.Api/Program.cs   (demo-mode serve check)
tests/RoleRadar.Tests.Unit/Demo/ (guard, dataset, credentials, TOTP, no-committed-secret, default-config)
tests/RoleRadar.Tests.Architecture/DemoArchitectureTests.cs
tests/RoleRadar.Tests.Integration/Demo/DemoSeedTests.cs
web/tests/e2e/screenshots.spec.ts, web/tests/e2e/support/totp.ts
web/tests/tooling/front-door-links.test.ts
docs/ (README.md, demo.md, threat-model.md, pre-announce.md, mcp-clients.md, mcp-tools.md, cli-reference.md,
       insights.md, operations/backup-restore.md, threat-model.md, pre-announce.md, images/*, adr/0039-demo-stack-and-guard.md)
README.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, CHANGELOG.md, .github/ISSUE_TEMPLATE/config.yml
scripts/denylist-check.ps1   (+History, +ImageMetadata modes)
```

## Complexity Tracking

| Addition | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| Standalone demo compose file | Isolation from `roleradar-mongo` and the real ports provable by reading one file | An overlay inherits the external volume, container name and port 27017 |
| `demo credentials` and `demo totp` | No committed credential (Principle I) yet a visitor must be able to sign in | Fixed committed credentials violate Principle I; a hidden default would be a backdoor |
| Interactive-only credentials output | Guarantees the secret cannot be redirected to a file or log driver | Printing in the seed step puts a secret in `docker compose` logs |
| Guard checks host as well as name | A correctly named database on the developer's own Mongo must be refused | A name-only guard allows `roleradar-demo` on the real server |
| One fixed, v7-shaped user id | The demo compose file must name the API's allowed subject before Identity exists | Letting the seed print the generated subject and feeding it to the API needs a shared file and a restart; the id is non-secret and not a document id |
| Two new `denylist-check.ps1` modes | Counts-only, comment- and BOM-safe output cannot be a safe one-liner | A pasted `git grep -f` prints matches and breaks on blank lines |
| Generated demo secrets step (R-014) | The apps need client secrets and a session key, and committing any value would be a committed secret | Fixed values in the compose file are committed credentials |
