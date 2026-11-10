# Try the demo

The demo runs the whole web app on your machine over a small fictional data set, so you can look around without setting up Mongo, an identity account or any real data. It is a separate Docker Compose project with its own containers, images, volumes and database, so it cannot touch a real RoleRadar stack.

**All demo data is fictional.** The companies, postings, applications, notes and the demo account are invented. Nothing in it is real, and nothing in it ever leaves your machine.

## What you need

- Docker with Compose v2. The demo mounts a generated configuration file from a volume with the `subpath` option, which needs **Docker Engine 26.0 or newer** (the release that added volume subpath mounts, see the [Engine 26.0 release notes](https://docs.docker.com/engine/release-notes/26.0/)) and **Docker Compose 2.26.0 or newer** (the first Compose release reported to pass the subpath to the engine; the engine 26.0 bundle shipped Compose 2.25.0, which does not). The Compose minimum comes from a community report rather than an official release note. The demo was tested on Docker Engine 29.9.2 with Compose 5.6.1.
- A current Chrome, Edge or Firefox. The demo uses `identity.localhost` and `api.localhost` names, which these browsers resolve to your own machine without any hosts-file edit. **They work in browsers only:** the Windows system resolver and Node on the host do not resolve `*.localhost`, so tools such as `fetch` or a .NET client on the host cannot use them (curl can, and scripts should use `curl --resolve`). The containers reach each other through network aliases.
- Free loopback ports 5101 (identity), 5102 (api) and 5103 (web). They differ from the real stack's ports on purpose, so both can run side by side.

## Start it

From the repository root:

```text
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait
```

The first run builds four images (the command line tool, identity, api and web), which takes a few minutes; later runs take about a minute. The command ends when every service is healthy and the seed step has finished. The seed step prints counts only, for example "created 14 companies, 48 postings, 20 applications".

## Get a sign-in

The demo has one throwaway account, `demo-owner`. No password is stored in the repository: the password and the authenticator (TOTP) key are generated on your machine, and this command prints them once:

```text
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml --profile tools run --rm demo-cli demo credentials
```

It needs a real terminal. In Windows Git Bash put `winpty` in front of it; in PowerShell or Windows Terminal it works as written. If output is redirected to a file or a pipe, the command refuses and prints nothing. Running it again resets the account and prints new values.

Add the printed TOTP secret to an authenticator app, or print the current code at any time:

```text
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml --profile tools run --rm demo-cli demo totp
```

## Open it

Browse to <https://localhost:5103> and sign in with the username, the password and a current code.

The demo uses self-signed certificates that are generated on your machine, so your browser shows a warning. Accept it once for each origin: once for `localhost:5103` (the web app) and once for `identity.localhost:5101` (the sign-in page you are redirected to). After that, open **Insights**: the funnels, stat tiles and charts show the fictional data, and every count links to the list it counts.

## Stop and remove it

```text
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml down -v
```

Always keep `-p roleradar-demo`. With it, `down -v` removes only the demo project's own volumes (`roleradar-demo-mongo`, `-identity`, `-certs` and `-secrets`). Never run `down -v` without the project name in a folder that holds a real stack.

## How it stays apart from a real stack

- Its own compose project name, container names, images (`roleradar-demo-*`), volumes and database (`roleradar-demo`). It never references the real Mongo volume, container name or port 27017, and its own Mongo publishes no port.
- A guard (`DemoModeGuard`) makes the seed step, the account commands and the demo API refuse to run unless demo mode (`ROLERADAR_DEMO=1`) is on, the database is exactly `roleradar-demo` and the server is the demo Mongo service. The API also refuses to serve the demo database when demo mode is off.
- The real compose files and the shipped images contain nothing demo related: tests check that the default configuration has no demo account, and that no file in the repository holds a demo password or TOTP key.
- The only committed demo values are not secrets: the username `demo-owner`, a fixed user id (the API's allowed subject, set in `deploy/demo/docker-compose.yml` because the compose file has to name it before Identity exists), the database name and the Mongo host name. Other tools and tests take these values from the compose file and this page.

## How sign-in works

Identity is the same Duende IdentityServer and ASP.NET Core Identity host as in the real stack, with the existing password and TOTP flow, five-attempt lockout and rate limits ([ADR 0017](adr/0017-owner-credential-and-security-controls.md)). The web app signs in through the standard OpenID Connect code flow, and the API accepts the resulting token only for the demo user. The sign-in code has no demo branches. The demo account is an ordinary user with a generated password and key, created by the seed step (whose generated values are discarded) and reset by `demo credentials`. The client secrets and the session key are generated into a volume on first start; the same volume carries the configuration file that gives Identity its secrets. The design is recorded in [ADR 0039](adr/0039-demo-stack-and-guard.md).

## Regenerate the screenshots

The README pictures are captured from the running demo by a Playwright spec, in light and dark: Insights, the list an Insights drill-down lands on (the "Interviewed" stage link), the postings list, a posting, an application with its timeline, a company and a rendered note (seven views, fourteen files, listed in `docs/images/manifest.json`, which also names the README hero image and the fictional records the capture opens). They are regenerated by hand and are never compared in CI, because the demo's dates move with the day it is seeded.

With the demo running and the credentials command's output at hand, set these variables in your shell (they are read from the environment only and written nowhere), then run the spec from the repository root:

```text
ROLERADAR_SCREENSHOT_DIR=docs/images
ROLERADAR_DEMO_URL=https://localhost:5103
ROLERADAR_DEMO_USERNAME=<username printed by the credentials command>
ROLERADAR_DEMO_PASSWORD=<password printed by the credentials command>
ROLERADAR_DEMO_TOTP_SECRET=<TOTP secret printed by the credentials command>

pnpm --filter web exec playwright test screenshots.spec.ts
```

Without `ROLERADAR_SCREENSHOT_DIR` the spec is skipped, so the normal end-to-end run writes nothing. The spec refuses any address other than the demo web app (https, `localhost`, port 5103) and ignores HTTPS errors because the demo certificates are self-signed.

**Review every image before you commit it.** Open all fourteen and look at each one. Binary files escape the text-based name check, so your eyes are the check: the only invented text in the pictures must be the seed's fictional names, and nothing else (no real name, address or account) may appear. Before committing, also run the image-metadata mode of the denylist script described in [pre-announce.md](pre-announce.md); it reports counts and file names only. The pictures carry no metadata chunks, which `web/tests/tooling/screenshots-manifest.test.ts` checks. A one-pixel `placeholder.png` stands in until all fourteen real images exist; the same test then requires it to be deleted.

## Troubleshooting

- **A port is taken:** stop whatever uses 5101, 5102 or 5103; the demo does not use the real stack's ports.
- **The sign-in page says the code is wrong:** codes last 30 seconds and each code works only once; if you just used one, wait for the next and run the totp command again.
- **Locked out after five wrong attempts:** run the credentials command again; it clears the lockout.
- **You want fresh data or new dates:** remove the demo with the `down -v` command above and start it again.
