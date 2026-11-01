# Quickstart & Verification Guide: 005b-management-ui

**Feature**: `005b-management-ui`  
**Date**: 2026-10-31  
**Status**: Ready for Verification  

This guide provides step-by-step instructions for running the RoleRadar Management Web UI locally in development mode, running it via Docker Compose, and executing automated verification suites.

---

## 1. Prerequisites & Environment Setup

Ensure the following tools are installed on the local system:
- **Node.js**: v22 LTS (pinned in `.node-version`)
- **pnpm**: v10.4.0+ (pinned in `package.json`)
- **.NET SDK**: v10.0+ (`10.0.402` pinned in `global.json`)
- **Docker & Docker Compose**: running Docker daemon
- **Development Certificates**: generated in `deploy/certs/` (`dev-cert.pfx`, `dev-cert.crt`, `dev-cert.key`)

### Host File Configuration

The Identity discovery URL and canonical issuer are `https://identity:5001` (matching the dev certificate SAN and Docker Compose internal network authority). Because modern browser OIDC redirects validate the exact issuer URI in the browser address bar, developers must add the following entry to their local hosts file (`C:\Windows\System32\drivers\etc\hosts` on Windows, `/etc/hosts` on Linux/macOS):

```text
127.0.0.1 identity api
```

> [!IMPORTANT]
> On Windows, modifying `C:\Windows\System32\drivers\etc\hosts` requires an elevated PowerShell session (Run as Administrator).
> - **Local Dev Path (`web:dev`)**: The web dev server runs in Node on the host and performs server-to-server OIDC discovery and token exchange against `https://identity:5001`. The host OS **must** resolve `identity` to `127.0.0.1`.
> - **Container Path (`web` in Compose)**: Docker internal networking resolves `identity` and `api` between containers automatically; however, the developer's desktop browser still navigates to `https://identity:5001/Account/Login` during the interactive redirect, requiring the host DNS entry.
> - **Automated E2E Tests**: Playwright browser tests configure `--host-resolver-rules="MAP identity 127.0.0.1, MAP api 127.0.0.1"` at launch, so automated test suites run without modifying the system hosts file.

### Environment Configuration

Create or verify `web/.env.local` (a committed template is available at `web/.env.example`):

```bash
NODE_ENV=development
PORT=5003
ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001
ROLERADAR_API_BASE_URL=https://localhost:5002
ROLERADAR_WEB_CLIENT_SECRET=<must match the secret passed to Identity; see Step 2>
SESSION_ENCRYPTION_KEY=<32 random bytes, base64; generate your own as shown below>
NODE_EXTRA_CA_CERTS=../deploy/certs/dev-cert.crt
HTTPS_CERT_PATH=../deploy/certs/dev-cert.crt
HTTPS_CERT_KEY_PATH=../deploy/certs/dev-cert.key
```

> [!WARNING]
> **Secret Alignment Requirement**: `ROLERADAR_WEB_CLIENT_SECRET` in `web/.env.local` MUST exactly match the `$env:ROLERADAR_WEB_CLIENT_SECRET` exported when starting the Identity container. Identity registers the `roleradar-web` confidential client using the SHA-256 hash of this secret. If the web client presents a mismatched secret during the authorization code grant exchange (`/auth/callback`), Identity rejects the request with HTTP 400 `invalid_client`.

Generate the session key and client secret once per machine using Node.js (cross-platform, works reliably in Windows PowerShell 5.1 and PowerShell Core):

```powershell
# Generate session encryption key or web client secret (32 random bytes base64)
node -e "console.log(require('crypto').randomBytes(32).toString('base64'))"
```

### Compose Environment File (`deploy/.env`)

When running Docker Compose commands from the repository root or inside `deploy/`, Docker Compose automatically reads variable definitions from `deploy/.env` (if present). You can either export variables into your PowerShell session or place them in `deploy/.env` (names only; never commit this file):
- `ROLERADAR_HTTPS_CERT_PASSWORD`: Password for the development PFX certificate (`dev-cert.pfx`) shared by Identity, API, and Web containers.
- `ROLERADAR_WEB_CLIENT_SECRET`: Client secret for the `roleradar-web` confidential client, matching `web/.env.local`.
- `ROLERADAR_HEADLESS_CLIENT_SECRET`: Client secret for headless automation clients (`roleradar-headless`).
- `ROLERADAR_OWNER_ALLOWED_SUBJECT`: Lowercase GUID sub of the single owner account provisioned in Identity.
- `SESSION_ENCRYPTION_KEY`: 32-byte base64 encryption key for web sessions (used when running the containerized web service).

---

## 2. Running Supporting Services (Identity & API)

Before starting the web application, start the backend datastore (MongoDB), Identity host, and REST API:

```powershell
# 1. Start MongoDB and infrastructure services
pwsh ./scripts/start-services.ps1

# 2. Ensure hosts file contains the required entry (requires Administrator):
#    127.0.0.1 identity api
# (See Section 1 "Host File Configuration" above)

# 3. Export required environment variables (or place in deploy/.env)
$env:ROLERADAR_HTTPS_CERT_PASSWORD = "DevCertPassword123!"
$env:ROLERADAR_WEB_CLIENT_SECRET = "<your-web-client-secret-matching-web/.env.local>"
$env:ROLERADAR_HEADLESS_CLIENT_SECRET = (node -e "console.log(require('crypto').randomBytes(32).toString('base64'))")

# 4. If the single owner account has not been provisioned in roleradar-identity-data, provision it now:
#    (Note: If an owner account already exists, do not run create-owner; reuse the existing owner sub GUID)
# docker run --rm -it --user 1654:1654 `
#     -v roleradar-identity-data:/data/identity `
#     -e ROLERADAR_IDENTITY_DB="Data Source=/data/identity/identity.db" `
#     roleradar-cli:local identity create-owner --username matt

# 5. Export the owner's verbatim sub GUID (from create-owner output)
$env:ROLERADAR_OWNER_ALLOWED_SUBJECT = "<verbatim-owner-sub-guid>"

# 6. Build and start Identity and API services in Compose
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps up -d identity api
```

Verify services are healthy:
- Identity: `https://localhost:5001/health`
- API: `https://localhost:5002/health`

---

## 3. Running the Web Application Locally (Dev Server)

The local dev server runs with React Router v7 and Vite with **in-process TLS** on `https://localhost:5003`.

```powershell
# 1. Install monorepo dependencies
pnpm install

# 2. Build contracts and run TypeScript typecheck
pnpm exec nx run web:typecheck

# 3. Start the React Router development server with in-process TLS
pnpm exec nx run web:dev
```

`web/vite.config.ts` automatically loads `web/.env.local` into `process.env` with Vite's `loadEnv` before server modules run, and configures `server.https` from `HTTPS_CERT_PATH` and `HTTPS_CERT_KEY_PATH`.

The application will listen on **`https://localhost:5003`** (HTTPS).

---

## 4. Running the Web Application via Docker Compose (Containerized)

To run the containerized web service with its read-only rootfs, tmpfs, in-process TLS, and unprivileged user (`node:node` UID 1000):

```powershell
# 1. Ensure SESSION_ENCRYPTION_KEY and ROLERADAR_WEB_CLIENT_SECRET are set in the environment
$env:SESSION_ENCRYPTION_KEY = "<your-32-byte-base64-key>"
$env:ROLERADAR_WEB_CLIENT_SECRET = "<your-web-client-secret>"

# 2. Build and launch the containerized web service alongside the stack
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps up -d --build web

# 3. Check container health and logs
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps ps web
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps logs -f web
```

Inside the container:
- In-process TLS terminates on port 5003 (`https://*:5003`) using `HTTPS_CERT_PATH=/https/cert.crt` and `HTTPS_CERT_KEY_PATH=/https/cert.key`.
- Container talks to API via `ROLERADAR_API_BASE_URL=https://api:5002` and Identity via `ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001`.

---

## 5. End-to-End Interactive Verification Workflow

Follow this manual checklist to verify all P1 user stories:

### Step 1: Unauthenticated Visit & Redirect
1. Open an incognito browser window and navigate to `https://localhost:5003/postings`.
2. **Expected Outcome**: The browser is immediately redirected to `https://identity:5001/Account/Login` with valid `client_id=roleradar-web`, `response_type=code`, `code_challenge`, and `state`.

### Step 2: Sign-In with Owner Credentials & TOTP
1. Enter the configured owner credentials (username and password).
2. Enter the TOTP code when prompted.
3. Submit the form.
4. **Expected Outcome**:
   - Identity redirects the browser back to `https://localhost:5003/auth/callback?code=...&state=...`.
   - The web server exchanges the code for tokens and issues an `HttpOnly`, `Secure`, `SameSite=Lax` session cookie (`__Host-roleradar_session`).
   - Browser lands on `https://localhost:5003/postings`.
   - Inspecting browser developer tools (Cookies and LocalStorage) confirms **zero access tokens or refresh tokens are exposed to the browser**.

### Step 3: Browse, Filter, and Sort Postings
1. On `/postings`, verify job opportunities are displayed in the `DataTable`.
2. Enter a search query in the `Toolbar` (e.g. `engineer`).
3. Select a disposition filter chip (e.g. `identified`).
4. **Expected Outcome**: The table updates using the backend parameters `GET /api/v1/postings?q=engineer&disposition=identified`.
5. Click a column header (e.g. `Score`).
6. **Expected Outcome**: Rows on the current page are sorted locally with a clear banner/badge: *"Sorting page-local results"*.

### Step 4: Untrusted Text Escaping Verification
1. Click into a posting detail view with seeded HTML strings (e.g. `<script>alert('xss')</script>`).
2. **Expected Outcome**: The text renders literally as characters in the DOM text node; no script executes, and the browser console remains clean.

### Step 5: Disposition Transition & Optimistic Concurrency
1. On the posting detail page, click "Update Disposition".
2. Select target disposition `scored` (notice `applied` is not offered).
3. Enter a transition reason: `"Strong alignment with screening criteria"`.
4. Submit the transition.
5. **Expected Outcome**: Toast confirms successful transition; posting detail shows updated disposition.
6. To test concurrency conflict: In a second tab, submit another transition on the same posting. Return to the first tab and attempt another update without refreshing.
7. **Expected Outcome**: The API returns HTTP 409 Conflict; the UI captures the error and displays a Toast: *"This posting was modified elsewhere. [Reload Latest]"*. Clicking "Reload Latest" fetches the fresh state without corrupting data.

### Step 6: Sign Out
1. Click the user menu in the top right corner of the `AppShell`.
2. Click "Sign Out".
3. **Expected Outcome**: The server evicts the session, expires the session cookie, redirects to Identity's end-session endpoint, and completes RP-Initiated Logout (clearing Identity and 2FA cookies before redirecting to the post-logout URI). Attempting to return to `https://localhost:5003/postings` redirects to Identity and must require both password and TOTP (confirming Identity's session cookie was fully cleared and does not silently sign the user back in).

---

## 6. Automated Test Suites Execution

Execute all test suites from the repository root:

```powershell
# 1. Monorepo lint and typecheck
pnpm exec nx affected -t lint build

# 2. Fast unit, component, and accessibility tests
pnpm exec vitest run

# 3. Fast .NET tests
pwsh ./scripts/test-fast.ps1

# 4. End-to-end Playwright smoke test (using throwaway containers)
pnpm exec playwright test tests/e2e/smoke.spec.ts

# 5. Container image integration & Web smoke tests (read-only rootfs & in-process TLS)
pwsh ./scripts/test-images.ps1
```

### Container Smoke & Image Verification Notes
- `pwsh ./scripts/test-images.ps1` builds `roleradar-cli:test`, `roleradar-mcp:test`, `roleradar-identity:test`, and `roleradar-web:test`.
- Executes `WebContainerSmokeTests` under production hardening conditions: read-only rootfs, `node` non-root user, tmpfs `/tmp:rw,noexec,nosuid,size=64m`, `cap_drop ALL`, and `no-new-privileges`.
- Verifies in-process TLS on port 5003, OIDC discovery handshake against an ephemeral Identity container, CSP headers with dynamic nonces, unauthenticated redirect to Identity with HttpOnly PKCE cookies, inspect security flags, secret log hygiene (no secrets in stdout/stderr), and fail-closed termination on missing secrets or TLS certificates.
- All Docker resources (network, volume, containers) are uniquely named per run (`rrtest-web-*`) and cleaned up immediately in `finally` without touching the developer's compose stack or data volumes.

### Playwright Verification Notes
- The Playwright smoke test signs in via the real Identity container using throwaway credentials.
- After executing a disposition transition, the test connects directly to the ephemeral MongoDB container and asserts that an append-only entry exists in the `audit_log` collection with the action and owner subject.
- Never connects to or mutates developer database instances.
