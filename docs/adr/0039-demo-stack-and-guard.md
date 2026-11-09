# ADR 0039: A Separate Demo Stack With a Guard and Generated Credentials

- **Status**: Accepted
- **Date**: 2026-11-08
- **Feature**: `012-front-door`

## Context and Problem Statement

A visitor to the public repository cannot see the product running. A demo needs a web app with fictional data and a way to sign in. The real stack must never be touched by it, and Principle I forbids committing credentials (ADR 0017 also rules out seed passwords and TOTP keys in files).

## Decision Outcome

- **A standalone compose file** (`deploy/demo/docker-compose.yml`) with the project name `roleradar-demo`, its own volumes and the database `roleradar-demo`. It is not an overlay of the real compose files, so the external `roleradar-mongo-data` volume, the `roleradar-mongo` container name and host port 27017 cannot leak into it. Mongo publishes no host port. Host ports are on loopback only, and differ from the real stack (5101 to 5103).
- **`*.localhost` names** (`identity.localhost`, `api.localhost`) replace hosts-file entries; inside the compose network they are aliases of the services.
- **Seed in the CLI host** (`src/RoleRadar.Cli/Demo/`, `roleradar demo seed`), through the real repositories, idempotent by natural key, ids from `Guid.CreateVersion7()`. Rejected: reusing the test seed (internal test code, edge-case matrix), a new source project, seed code in Domain, Application or Infrastructure, a script writing Mongo directly, a restored dump (the last two bypass the repository choke point for derived fields).
- **`DemoModeGuard`** (Infrastructure) allows demo mode (`ROLERADAR_DEMO=1`) only when the database name is exactly `roleradar-demo` and the single server host is the demo host. Production callers use `CheckForDemoStack`, which supplies the fixed host; only tests use the allowed-host overload. The API refuses to start in demo mode with another database or host, and refuses the demo database when demo mode is off. Identity has no Mongo and no start-up check; the demo commands' guard ties the account to the demo stack.
- **No committed credential.** The seed step creates the account with a generated password and TOTP key and logs neither. `roleradar demo credentials`, run through the `demo-cli` service, resets the account and prints username, password and TOTP secret once, only when stdin and stdout are interactive terminals, never through a logger. `roleradar demo totp` prints the current code. The sign-in code has no demo branch: it is the existing password plus TOTP flow with lockout and rate limits (ADR 0017). The client secrets, session key and TLS key are generated into demo volumes at first start by two one-shot compose services that reuse the pinned Mongo image (it ships openssl). The compose file is named `docker-compose.yml` in its own directory, so Dependabot's compose updater matches the standard name.
- **The one fixed value.** The demo user id is a fixed, non-secret, v7-shaped constant, set in the demo compose file as the API's allowed subject, because the compose file must name it before Identity exists. It is an Identity user id, not an owner id or a document id. The owner accepted this as the single deviation from Principle IX's "ids are generated with `Guid.CreateVersion7()`". The username (`demo-owner`) and the database name are the other committed demo constants; none is a secret.
- **Not in the default configuration.** The real compose files and the shipped images' default configuration contain nothing demo related; tests assert it.

## Consequences

- A visitor needs Docker only. The first run builds three images.
- The credentials command is deliberately awkward to automate; CI proves the demo up to the sign-in page and the owner proves sign-in.
- Browsers warn about the self-signed certificates once per origin; the docs say so.
- Hosting the demo is out of scope.
