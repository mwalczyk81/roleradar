# Threat model

One page on what RoleRadar trusts, what it defends against, and what it does not claim. It covers the real local stack and the demo. Decisions behind it are in the [ADR index](adr/README.md); vulnerabilities are reported as [SECURITY.md](../SECURITY.md) describes.

## What it protects

A single owner's job-search records and notes: companies, postings, applications, private contacts and free-text notes. The assets are their confidentiality on the owner's machine, their integrity (nothing is silently changed or lost), and keeping real data out of this public repository.

## Trust boundaries

| Boundary | Trusted side | Untrusted side | Control |
| --- | --- | --- | --- |
| The machine | The owner's user account and local processes | Anything on the network | Every listener binds to `127.0.0.1`: Mongo on 27017, Identity, API and web on 5001 to 5003 (demo: 5101 to 5103). The MCP server speaks stdio and opens no port ([ADR 0003](adr/0003-stdio-first-mcp.md)). |
| Browser to web app | The signed-in owner | Other pages and origins | OpenID Connect sign-in with password and TOTP; CSRF tokens checked on every form action; a strict nonce-based content security policy ([ADR 0026](adr/0026-strict-csp-nonce-and-theme-initialization.md)). |
| Web app to API | Tokens Identity issued for the owner | Any other token | The API accepts bearer tokens only for the configured owner subject, and every read goes through an owner-scoped seam ([ADR 0028](adr/0028-odata-read-seam-and-query-execution.md)). |
| Content to the UI | Application code | Posting, company, application and note text | Rendered as React text nodes; links only for http and https with `rel="noopener noreferrer"`; note Markdown without raw HTML or images ([ADR 0032](adr/0032-markdown-rendering-of-notes.md)). |
| Agent to tools | The MCP client the owner launched | Text inside postings that an agent reads | Narrow, typed, audited tools; no tool deletes anything; every write records an append-only audit entry ([ADR 0010](adr/0010-immutable-auditability-and-zero-hard-deletes.md)). |
| Repository | Fictional fixtures and code | The owner's real data | gitleaks and the private-denylist hooks on commit and push; generated secrets live in ignored files or volumes, never in tracked files. |

## Authentication

The owner signs in through Duende IdentityServer with a password and a TOTP code. Five failed attempts lock the account for 15 minutes, and the sign-in and token endpoints are rate limited ([ADR 0017](adr/0017-owner-credential-and-security-controls.md)). Tokens are short-lived and refresh tokens rotate with reuse detection. The web session is held encrypted in memory ([ADR 0022](adr/0022-encrypted-in-memory-session-store.md)).

## Secrets and backups

Client secrets, the session key and certificates for the real stack are generated locally into ignored files; nothing secret is committed. Backups stream through libsodium `secretstream` (XChaCha20-Poly1305) with an Argon2id key, restore verifies the whole archive before writing, and passphrases are never taken from the command line ([ADR 0007](adr/0007-backup-streaming-aead-encryption.md)).

## Logs and the audit trail

The services write structured logs ([logs-and-health.md](operations/logs-and-health.md), [ADR 0041](adr/0041-operability-logging-correlation-health.md)). A log is a second copy of whatever it contains, so the control is to keep secrets and private text out of it.

- **Redaction.** One scrubber is applied to every record before it is written. It removes bearer and basic credentials, JWT-shaped values, credentials in connection strings, query strings, sensitive `key=value` and JSON pairs in free text, and the value of any property whose key names a password, token, secret, cookie, authorization header, note body or OIDC `code` or `state`. The .NET hosts share one implementation (`RoleRadar.Observability`); the web has a matching pure one (`web/app/lib/observability.ts`), and each is changed together with the other. Request records carry the method, the path without its query string, the status and the duration, never headers, cookies or bodies.
- **The tests that prove it.** Unit tests cover each rule with fictional secret vectors (`LogScrubberTests`, `RoleRadarConsoleFormatterTests`, `web/tests/unit/observability.test.ts`). `IdentityRedactionContractTests` drives the real Identity host through wrong-password, unknown-user and full sign-in flows, and `web/tests/unit/web-redaction.test.ts` drives the web's real failure paths and structured fields. `RedactionIntegrationTests` runs the launched hosts with sentinel passwords, tokens, note bodies and a typed username, and fails if any sentinel appears in any stream. The log of the viewer and of `docker compose logs` is therefore as safe as the scrubber and these tests are complete.
- **Sign-in events without the username.** Identity logs one event per sign-in outcome (`SignInSucceeded`, `SignInFailed`, `AccountLockedOut`, `OneTimeCodeFailed`) with the account's subject id, and only when the typed username matched an account. The username typed on a failed attempt is never logged, so the logs do not reveal which names were tried or confirm that one exists. `SignInEventContractTests` pins this.
- **The audit trail.** Every write records who made it (the API records the signed-in subject, the CLI and MCP a fixed actor name) and the request's correlation id. It is append-only and readable only through `roleradar audit list`, which prints no note text or before and after values. There is no MCP tool or API endpoint for it.

### The optional log viewer

`deploy/docker-compose.logs.yml` starts a log viewer (Dozzle) that is off unless the owner starts it. To read container output it mounts the Docker socket. The mount is read-only, but read access to the socket is still effectively host-level Docker access for that one container: whoever controls the viewer can inspect every container on the machine, including its environment variables.

That risk is accepted, and only on these terms:

- **Opt-in.** It is a separate file, never part of the default stack or the demo, and the owner stops it with one command.
- **Loopback only.** It publishes `127.0.0.1:5004` and nothing else. Exposing it beyond loopback would need a socket proxy and a new decision.
- **The owner's machine.** Anyone who can already reach the Docker socket or run Docker on that machine can do more than the viewer can, so it grants no new capability to a local attacker.
- **Hardened and quiet.** Read-only root filesystem, all capabilities dropped, no new privileges, a pinned image digest, and analytics, release checks, registry checks and avatar fetches turned off, so it makes no outbound calls.

## The demo

The demo ([demo.md](demo.md)) runs the same images over fictional data in a separate Compose project, `roleradar-demo`, with its own containers, volumes and database ([ADR 0039](adr/0039-demo-stack-and-guard.md)).

- **What it exposes.** The web app, Identity and the API, each on a loopback-only port (`127.0.0.1:5101` to `5103`). Mongo publishes no port at all. Anyone who can reach those ports on the machine sees fictional data only.
- **The guard.** The seed step, the account commands and the demo API refuse to run unless demo mode (`ROLERADAR_DEMO=1`) is on, the database is exactly `roleradar-demo` and the server is the demo Mongo service. The API also refuses to serve the demo database when demo mode is off. The real compose files and default configuration contain no demo value.
- **Throwaway credentials.** The demo account is throwaway and demo-only: it exists only in the demo stack, its password and TOTP key are generated at run time and never committed. The credentials command prints them once, only to an interactive terminal, and never to a log; running it again resets them. The client secrets and session key are also generated into a demo volume on first start.
- **Self-signed certificates.** Generated on the machine at first start for `localhost`, `identity.localhost` and `api.localhost`. The browser warning is expected; accepting it trusts that one local origin, nothing else.
- **Teardown.** `down -v` with `-p roleradar-demo` removes the demo's own volumes and nothing else.

## What is not claimed

- **Not hardened for the internet.** RoleRadar is local-first by design. Exposing any port beyond loopback, or hosting it, is out of scope; hosting is parked.
- **No protection from the local machine.** Mongo runs without authentication and relies on loopback binding. Anyone with the owner's account, or administrator rights, can read the data.
- **Single owner.** Owner ids scope every collection, but there is no multi-user isolation, account management or role model.
- **Self-signed certificates prove nothing.** They protect traffic on the machine; they do not identify a server.
- **No vulnerability-free images.** The gate fails on fixable CRITICAL findings; fixable HIGH findings are reported and tracked, not blocked ([ADR 0036](adr/0036-test-lanes-and-gate-scan.md)).
- **Logs are not a secret store.** The scrubber and its tests keep known secret shapes and private text out of the logs, but they cannot recognise every private value a future message might include. Anyone who can read the container logs or run the viewer can read what is there.
- **No external audit.** No penetration test or third-party review has been done. Agents should still treat posting text as untrusted input, whatever the tools allow.
