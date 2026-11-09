# Research: Front Door

Each item is Decision, Rationale, Alternatives. OPEN items name the task that closes them and carry a fallback. R-005 (gitleaks allowlist) was dropped: no credential is committed, so nothing needs allowlisting; its number is retired, not reused.

## R-001 `*.localhost` without hosts entries (RESOLVED here 2026-11-08; owner still confirms on Windows in T021; CI proof is the nightly smoke)

- **Decision**: use `identity.localhost` and `api.localhost`; inside compose they are network aliases of the identity and api services.
- **Rationale**: Chromium, Firefox and Edge resolve `*.localhost` to loopback by themselves (RFC 6761); aliases give the containers the same names, so the issuer URL is identical for browser, web and API.
- **Alternatives**: hosts-file lines (what the real stack needs; rejected by the spec); pointing containers at `localhost` (breaks loopback inside a container). Fallback if a platform fails: document one hosts-file line for that platform only.
- **Gate**: the owner confirms the Windows result before the demo task is done.
- **Result (T003, this Windows machine, Docker 29.9.2, throwaway project `roleradar-demo-research`, removed afterwards)**:
  - Container to container through network aliases: Node `fetch` from a client container to `https://identity.localhost:5001` and `https://api.localhost:5001` returned 200 for both, trusting the generated certificate through `NODE_EXTRA_CA_CERTS`.
  - Browser on the host: Chromium driven by Playwright (`ignoreHTTPSErrors`) reached `https://identity.localhost:<published port>` and `https://api.localhost:<published port>` with 200 and the expected body.
  - Not resolvable on this Windows host: the system resolver (`[System.Net.Dns]::GetHostAddresses('identity.localhost')` -> "No such host is known") and therefore Node `fetch` on the host (`ENOTFOUND`). `curl` (Git Bash) resolved the name itself and failed only on the untrusted self-signed certificate.
  - **Consequence**: anything on the host that is not a browser must not use `*.localhost` names: the screenshot spec uses browser navigation only and computes the TOTP code locally (no host-side `fetch` to the demo); the nightly smoke uses `curl` (which resolves `*.localhost` itself) with `-k` or `--resolve`, and checks the health endpoints through the published `localhost` ports where possible. The OIDC redirect still passes through the browser, which resolves the name.
  - **Not proven here**: Linux CI (browser resolution is Chromium's own; container aliases are Docker's, so the same behaviour is expected) and macOS. The nightly smoke job is the CI proof; until it has run, CI remains unverified.

## R-002 Password policy and generator (OPEN, closes in T014)

- **Decision**: generate 24 characters from an alphabet that satisfies the Identity password options (read from the Identity configuration, asserted by a test), using `RandomNumberGenerator`.
- **Rationale**: the demo must pass the normal password check; tests prove it.
- **Alternatives**: a fixed password (rejected: committed credential).

## R-003 TOTP in e2e (OPEN, closes in T024)

- **Decision**: reuse the helper in `web/tests/e2e/real-stack.spec.ts` if it is a pure function; otherwise add `web/tests/e2e/support/totp.ts` tested by Vitest on RFC 6238 vectors.
- **Rationale**: the screenshot spec needs a code from the secret passed in the environment.

## R-004 Markdown parsing in the link test (Decided in T029)

- **Decision**: regex extraction of `[t](x)`, `![a](x)`, `<img src>`, `<source srcset>` and `<a href>`, with fenced blocks (indented ones too) and inline code removed first; GitHub heading slugs computed in the test.
- **Rationale**: the notes renderer's Markdown parser (remark, under react-markdown) is not a direct dependency of `web`, and pnpm's strict layout means importing it would be a new dependency in practice. The three files are ours and simple, and a deliberately broken fixture (`web/tests/fixtures/front-door/broken.md.txt`) proves every check reports what it should.
- **Constraint found**: the Linux web gate does not copy `src/` or `tests/` into its container, so the front-door files must not link into them; the test reports such a link with that reason.

## R-006 Animated GIF (Decided: out of scope)

- Playwright records WebM, not GIF; a GIF needs a converter that is not in the repo. Adding one is a new tool.

## R-007 gitleaks history scan (Decided in T040)

- **Decision**: run the pinned v8.30.1 through its container image, with tag and digest recorded; installing it locally is documented as the alternative. Image `ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f` (the Docker Hub `zricethezav/gitleaks:v8.30.1` index has the same digest). Command shape `gitleaks git /repo --config /repo/.gitleaks.toml --log-opts=--all --redact --no-banner` with the repository mounted read-only; `GIT_CONFIG_*` environment variables set `safe.directory` for the mounted folder. Checked on 2026-11-09 against a throwaway one-commit repository: 1 commit scanned, no leaks, exit 0. The real history scan is the owner's step (T042).
- **Rationale**: same version and rules as the pre-commit hook; `--redact` keeps secrets out of the output.

## R-008 Markdown features in the note renderer (RESOLVED in T009)

- **Decision**: seed notes use only the renderer's allowed elements. ADR 0032 lists the vocabulary: paragraphs, headings (shifted down two levels), lists, task lists, blockquotes, code and fenced code, emphasis, links, and tables; no raw HTML and no images. The demo notes use headings, lists, task lists, a table, fenced code, a blockquote and https links to example.com.

## R-009 Certificate set for Identity, API and web (RESOLVED; proven on this Windows machine; owner confirms on Windows in T021)

- **Decision**: a one-shot service with a pinned OpenSSL image writes PFX (Kestrel), PEM certificate (`SSL_CERT_FILE`, `NODE_EXTRA_CA_CERTS`) and PEM key (web) into a named volume.
- **Rationale**: nothing key-like enters the repository (`detect-private-key` and gitleaks stay quiet without allowlists).
- **Alternatives**: committing a second dev certificate (committed key); reusing the existing dev certificate (names do not include `*.localhost`).
- **Gate**: the owner confirms the Windows result before the demo task is done.
- **Result (T004)**: the already pinned `mongo:8.0.34` image has `openssl` (3.0.13), `base64`, `head` and `tr`, so **no new image is needed**: `demo-certs` and `demo-secrets` reuse the Mongo image that the demo already pins, run as root with an inline command, and write into named volumes. One `openssl req -x509 -newkey rsa:2048 -nodes -addext subjectAltName=DNS:identity.localhost,DNS:api.localhost,DNS:localhost,IP:127.0.0.1` produced `cert.pem` and `key.pem`; `openssl pkcs12 -export -passout pass:` produced `cert.pfx` with an empty password. Proven: Node (HTTPS server from the PEM pair; client trusting the PEM), Chromium (with the cert error ignored), and .NET 10 (`X509CertificateLoader.LoadPkcs12FromFile` with an empty password loads the PFX with its private key). **Proven by the full stack run (T020, this machine):** Kestrel started from the PFX in both the Identity and API images (both healthy); the API image trusted the PEM through `SSL_CERT_FILE` (a signed-in session in a real browser reached `/insights`, which needs the API to fetch the signing keys from `https://identity.localhost:5101` and validate the token); the web image trusted it through `NODE_EXTRA_CA_CERTS` (its OIDC discovery to identity worked). The browser warns once per origin, as documented.
  - Rejected: `alpine/openssl` or another new image (extra pin, extra Dependabot surface); `node:22-alpine` (no `openssl` binary: checked).

## R-010 Demo in CI (Decided)

- A job in `nightly-quality.yml` runs the documented command on Linux and checks the seed exit code and counts line, the web health endpoint and that the sign-in page loads. It does not sign in: `demo credentials` refuses non-interactive output by design. Sign-in is verified by the owner (T021) and the screenshot run.
- **Alternatives**: a `script`-wrapped pseudo-terminal in CI (adds a tool and weakens the guarantee under test); a flag to allow piped output (defeats the no-file rule).

## R-011 PR numbers (Decided)

- Taken from `git log` (map in plan D8). The 012 number is added by the owner at merge.

## R-012 Shared Identity volume between CLI and Identity (RESOLVED on this machine; owner confirms on Windows in T021)

- Both images run as `1654:1654`, so a named volume should be writable by both. If not, `demo seed` and `demo credentials` move into the Identity image as entrypoint arguments.
- **Gate**: the owner confirms the Windows result before the demo task is done. Also confirm that SQLite tolerates the CLI resetting credentials while Identity runs (short write transactions; WAL).

- **Finding (T004)**: the Identity image creates `/data/identity` owned by `1654:1654` (`COPY --chown`), but the CLI image has no such directory, so a named volume first mounted by the CLI image would be root-owned and unwritable for uid 1654. **Decision**: the `demo-secrets` one-shot (Mongo image, root) also runs `chown -R 1654:1654` on the demo Identity volume before any service uses it, and `demo-seed` and `demo-cli` depend on it. No image change.

- **Result (T020/T021 evidence, this machine)**: the `demo-secrets` step's `chown` worked: `demo-seed` and `demo-cli` (CLI image) wrote the Identity SQLite store that the Identity image served. `demo credentials` ran twice while Identity was running and healthy; the sign-in right after the second reset, with the new password and a code from the new key, succeeded, so SQLite tolerated a credentials reset while Identity was running. No log held the password, the TOTP key, the client secret or the session key (4,042 log lines scanned, zero hits).

## R-013 Natural-key lookups and backdated writes in the repositories (RESOLVED in T009)

- **Question**: do the existing repository ports offer lookup by company name, canonical posting URL and application per posting, and accept explicit timestamps (as `InsightsSeed` shows they do for writes)?
- **Decision**: use what exists; no port change was needed. Companies: `GetByNormalizedNameAsync`; postings: `GetByDedupKeyBAsync` (the normalised URL); applications: `GetByPostingIdAsync`; search runs: `GetByStartedAtAsync`; ids default to `Guid.CreateVersion7()` in the domain factories, and explicit timestamps are accepted (`now:`). Notes: the registered tracking context cannot see notes (its note methods return false there), so the seeder looks through `INoteRepository.SearchAsync` by area and compares titles, and creates through the audited `CreateNoteCommand` handler. Found by the live run: the first version used the tracking context, and a second `demo seed` created the six notes again. The fix and the integration test (second run creates nothing) came from that run.
- **Search runs** have no stable natural key (the start time moves with the as-of date), so they are written only when a run created tracker rows; a second run on a seeded database changes nothing whatever its as-of date.

## R-014 Client secrets and session key for the demo apps (RESOLVED; proven in the full stack run)

- **Problem**: the real apps compose requires `ROLERADAR_WEB_CLIENT_SECRET`, `ROLERADAR_HEADLESS_CLIENT_SECRET` and `SESSION_ENCRYPTION_KEY`. Committing fixed values would be committed secrets.
- **Decision**: a one-shot `demo-secrets` step generates them into a named volume on first start, and the services read them from files; if an image only reads environment variables, an entrypoint wrapper in the demo compose file exports them from the volume.
- **Alternatives**: fixed values in the compose file (committed secrets); asking the visitor to export variables first (breaks the one-command start).
- **Result (T004)**: generation works with the Mongo image's `head -c N /dev/urandom | base64` into a named volume (a 44-character session key and a 30-character client secret were produced and readable with mode 644 in the volume). Consumption, by image: **web** (`node:22-alpine`, has `sh`): an entrypoint `sh -c` wrapper in the demo compose file exports `ROLERADAR_WEB_CLIENT_SECRET` and `SESSION_ENCRYPTION_KEY` from the secrets volume, then `exec`s the existing start command. **Identity** (chiseled, no shell): `IdentityServerConfig` reads `Clients:Web:Secret` and `Clients:Headless:Secret` from configuration before the environment variables, so the one-shot also writes `appsettings.Demo.json` into the secrets volume, the compose file mounts it with a volume `subpath` at `/app/appsettings.Demo.json` and sets `ASPNETCORE_ENVIRONMENT=Demo`; no product code changes. **API**: needs no client secret. Verify in T020 that `Demo` as an environment name changes no behaviour (look for `IsDevelopment`/`IsProduction` checks) and that the Compose and Engine versions support volume `subpath` (Compose 2.26 and Engine 26 or newer; Engine 29.9.2 here). Fallback if that fails: generate the values on the host in the documented command (`$(...)`), which keeps secrets out of the repository but gives up the pure one-command start; that fallback needs the owner's decision.

## R-015 Decision on `ADR 0017` relationship (Decided)

- ADR 0039 states that the demo keeps ADR 0017: it uses the same provisioning entities and the same password plus TOTP flow, and adds generated credentials only for a database that the guard proves is the demo database.

## R-016 Environment name `Demo` and the Production code paths (RESOLVED in T015)

- **Finding**: the only environment predicates in the repository's hosts are two `IsDevelopment()` calls in `src/RoleRadar.Api/Program.cs` (HTTPS metadata requirement and the developer-only endpoints). Identity, Infrastructure, Application and the CLI have none; the web app branches only on `NODE_ENV` values `development` and `test`. `Demo` is none of those, so it takes the Production paths.
- **Test**: `DemoEnvironmentParityTests` pins this (exactly two predicates, both `IsDevelopment()` in the API; no environment-name literals; the web's `NODE_ENV` comparisons are only `development` and `test`; the demo compose runs the web as `production` and never names the developer environment). It is a static scan, so it proves no other branch exists, not the behaviour of a running host. The running demo (Identity with `ASPNETCORE_ENVIRONMENT=Demo`, signing in over HTTPS with the API requiring HTTPS metadata) is the live check.

## R-017 Compose and Docker versions for the volume `subpath` (RESOLVED, with one unconfirmed point)

- **Engine**: official Docker Engine 26.0 release notes list the new `Subpath` field in volume options, so Engine 26.0 or newer.
- **Compose**: the Engine 26.0 notes say they bundle Compose 2.25.0, which does not pass the subpath; a community issue states Compose 2.26.0 is the first release that does. That Compose minimum is not confirmed in an official Compose release note, and `docs/demo.md` says so.
- **Tested here**: Engine 29.9.2, Compose 5.6.1.

## R-018 Number of images built on first run (RESOLVED)

- The first run builds **four** images (`roleradar-demo-cli`, `-identity`, `-api`, `-web`), not three: the CLI image is needed for the seed step and the credentials helper. The README text, spec and tasks say four.

## R-019 `docker compose run` and the interactive-only credentials command (RESOLVED)

- `docker compose run` allocates a terminal when the calling shell has one; in Windows Git Bash `winpty` is needed. Redirected or piped output is refused (exit 2, nothing printed), shown live. An agent or CI step without a terminal cannot read the credentials, by design; for the owner-side verification the agent used `docker create -t -i` plus `docker start -a`, which gives the container a terminal without the host CLI needing one.
