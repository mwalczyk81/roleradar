# Quickstart & Verification Guide: 005b-management-ui

**Feature**: `005b-management-ui`  
**Date**: 2026-11-01  
**Status**: Ready for Verification  

This guide provides step-by-step instructions for running the RoleRadar Management Web UI locally in development mode, running it via Docker Compose, and executing automated verification suites.

Every command in this guide is verified and copy-paste compatible with both **Windows PowerShell 5.1** and **PowerShell 7+ (`pwsh`)**.

---

## 1. Prerequisites & Environment Setup

Ensure the following tools are installed on the local system:
- **Node.js**: v22 LTS (pinned in `.node-version`)
- **pnpm**: v10.4.0+ (pinned in `package.json`)
- **.NET SDK**: v10.0+ (`10.0.100` pinned in `global.json`)
- **Docker & Docker Compose**: running Docker daemon
- **Development Certificates**: generated in `deploy/certs/` (`dev-cert.pfx`, `dev-cert.crt`, `dev-cert.key`)

### Host File Configuration

The Identity discovery URL and canonical issuer are `https://identity:5001` (matching the dev certificate SAN and Docker Compose internal network authority). Because modern browser OIDC redirects validate the exact issuer URI in the browser address bar, developers must add the following entry to their local hosts file (`C:\Windows\System32\drivers\etc\hosts` on Windows, `/etc/hosts` on Linux/macOS):

```text
127.0.0.1 identity api
```

> [!IMPORTANT]
> On Windows, modifying `C:\Windows\System32\drivers\etc\hosts` requires an elevated PowerShell session (**Run as Administrator**).
> - **Local Dev Path (`web:dev`)**: The web dev server runs in Node on the host and performs server-to-server OIDC discovery and token exchange against `https://identity:5001`. The host OS **must** resolve `identity` to `127.0.0.1`.
> - **Container Path (`web` in Compose)**: Docker internal networking resolves `identity` and `api` between containers automatically; however, the developer's desktop browser still navigates to `https://identity:5001/Account/Login` during the interactive redirect, requiring the host DNS entry.
> - **Automated E2E Tests**: Playwright browser tests configure route interception or host resolver rules, so automated test suites run without modifying the system hosts file.

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
> **Secret Alignment Requirement**: `ROLERADAR_WEB_CLIENT_SECRET` in `web/.env.local` MUST exactly match `$env:ROLERADAR_WEB_CLIENT_SECRET` exported when starting the Identity container. Identity registers the `roleradar-web` confidential client using the SHA-256 hash of this secret. If the web client presents a mismatched secret during the authorization code grant exchange (`/auth/callback`), Identity rejects the request with HTTP 400 `invalid_client`.

Generate the session key and client secret once per machine using Node.js (cross-platform, copy-paste correct in both Windows PowerShell 5.1 and pwsh):

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

Before starting the web application, start the backend datastore (MongoDB), Identity host, and REST API. Commands work in both PowerShell 5.1 and pwsh:

```powershell
# 1. Start MongoDB and infrastructure services
if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    pwsh ./scripts/start-services.ps1
} else {
    powershell -ExecutionPolicy Bypass -File ./scripts/start-services.ps1
}

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
- Identity: `https://identity:5001/health`
- API: `https://localhost:5002/health`

---

## 3. Running the Web Application Locally (Dev Server)

The local dev server runs with React Router v7 and Vite with **in-process TLS** on `https://localhost:5003`.

```powershell
# 1. Install monorepo dependencies
pnpm install

# 2. Run TypeScript typecheck
pnpm --filter web run typecheck

# 3. Start the React Router development server with in-process TLS
pnpm --filter web run dev
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

> [!IMPORTANT]
> **Human-Required Verification Steps**:
> The steps in this section (Steps 1 through 6) require manual human interaction in a real desktop browser. While headless Chromium automated tests (T062) execute identical journeys continuously in CI, automated tools cannot replace human verification of visual contrast, layout comfort, physical TOTP enrollment via mobile authenticator apps, and interactive DevTools inspection.
>
> **Explicit list of steps requiring a human**:
> 1. **Step 1 (Human)**: Launch a physical desktop browser (Chrome/Edge/Firefox) in Incognito mode and navigate to `https://localhost:5003/postings`.
> 2. **Step 2 (Human)**: Visually inspect the Identity login page, physically enter the owner username and password, use a TOTP authenticator mobile app to read the TOTP 6-digit code, submit the challenge, and open browser DevTools to confirm zero tokens in `localStorage` or `sessionStorage`.
> 3. **Step 3 (Human)**: Interact with the postings search input, select status and disposition filters, and verify page-local score sorting.
> 4. **Step 4 (Human)**: Visually inspect raw HTML test strings in posting detail views to ensure zero unescaped script execution.
> 5. **Step 5 (Human)**: Open two simultaneous browser tabs, submit a disposition change in tab 1, and attempt a concurrent update in tab 2 to visually verify the 409 conflict toast and reload behavior.
> 6. **Step 6 (Human)**: Click "Sign Out", confirm session eviction, and attempt to re-access `/postings` to verify full re-challenge.

### Step 1: Unauthenticated Visit & Redirect (Human Step)
1. Open an incognito browser window and navigate to `https://localhost:5003/postings`.
2. **Expected Outcome**: The browser is immediately redirected to `https://identity:5001/Account/Login` with valid `client_id=roleradar-web`, `response_type=code`, `code_challenge`, and `state`.

### Step 2: Sign-In with Owner Credentials & TOTP (Human Step)
1. Enter the configured owner credentials (username and password).
2. Enter the TOTP code from your mobile authenticator app when prompted.
3. Submit the form.
4. **Expected Outcome**:
   - Identity redirects the browser back to `https://localhost:5003/auth/callback?code=...&state=...`.
   - The web server exchanges the code for tokens and issues an `HttpOnly`, `Secure`, `SameSite=Lax` session cookie (`__Host-roleradar_session`).
   - Browser lands on `https://localhost:5003/postings`.
   - Inspecting browser developer tools (Cookies and LocalStorage) confirms **zero access tokens or refresh tokens are exposed to the browser**.

### Step 3: Browse, Filter, and Sort Postings (Human Step)
1. On `/postings`, verify job opportunities are displayed in the `DataTable`.
2. Enter a search query in the `Toolbar` (e.g. `engineer`).
3. Select a disposition filter chip (e.g. `lead` or `scored`).
4. **Expected Outcome**: The table updates using the backend parameters `GET /api/v1/postings?q=engineer&disposition=lead`.
5. Click a column header (e.g. `Score`).
6. **Expected Outcome**: Rows on the current page are sorted locally with a clear banner/badge: *"Sorting page-local results"*.

### Step 4: Untrusted Text Escaping Verification (Human Step)
1. Click into a posting detail view with seeded HTML strings (e.g. `<script>alert('xss')</script>`).
2. **Expected Outcome**: The text renders literally as characters in the DOM text node; no script executes, and the browser console remains clean.

### Step 5: Disposition Transition & Optimistic Concurrency (Human Step)
1. On the posting detail page, click "Change Disposition".
2. Select target disposition `scored` (notice `applied` is not offered directly).
3. Enter a transition reason: `"Strong alignment with screening criteria"`.
4. Click "Apply Transition".
5. **Expected Outcome**: Modal closes, disposition badge updates to `Scored`, and posting detail reflects fresh state.
6. To test concurrency conflict: In a second tab, submit another transition on the same posting. Return to the first tab and attempt another update without refreshing.
7. **Expected Outcome**: The API returns HTTP 409 Conflict; the UI captures the error and displays a Toast: *"This posting was modified elsewhere. [Reload Latest]"*. Clicking "Reload Latest" fetches the fresh state without corrupting data.

### Step 6: Sign Out (Human Step)
1. Click the "Sign Out" button in the navigation bar or AppShell.
2. **Expected Outcome**: The server evicts the session, expires the session cookie, redirects to Identity's end-session endpoint, and completes RP-Initiated Logout. Attempting to return to `https://localhost:5003/postings` redirects to Identity and requires both password and TOTP (confirming Identity's session cookie was fully cleared and does not silently sign the user back in).

---

## 6. Automated Test Suites Execution

Execute all test suites from the repository root (commands work across PowerShell 5.1 and pwsh):

```powershell
# 1. Web TypeScript typecheck
pnpm --filter web run typecheck

# 2. Web linter
pnpm --filter web run lint

# 3. Web unit and component tests
pnpm --filter web run test

# 4. Web coverage gate (>=80% lines, >=75% branches)
pnpm --filter web run test:coverage

# 5. Web build
pnpm --filter web run build

# 6. Playwright Chromium component and architecture E2E tests (run 3 times to ensure zero flakes)
pnpm --filter web run test:e2e -- --repeat-each=3

# 7. Fast .NET test suite (Architecture + Unit + Contract)
if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    pwsh ./scripts/test-fast.ps1
} else {
    powershell -ExecutionPolicy Bypass -File ./scripts/test-fast.ps1
}

# 8. Real-Stack End-to-End Test (real Identity, API, Web, Mongo, Chromium, audit log assertions)
if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    pwsh ./scripts/test-e2e-real.ps1
} else {
    powershell -ExecutionPolicy Bypass -File ./scripts/test-e2e-real.ps1
}

# 9. Container image parity & Web container smoke tests
if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    pwsh ./scripts/test-images.ps1
} else {
    powershell -ExecutionPolicy Bypass -File ./scripts/test-images.ps1
}

# 10. Automated Review Gate (full stack verification with optional real-stack E2E)
if (Get-Command pwsh -ErrorAction SilentlyContinue) {
    pwsh ./scripts/review-gate.ps1 -IncludeE2E
} else {
    powershell -ExecutionPolicy Bypass -File ./scripts/review-gate.ps1 -IncludeE2E
}
```

### Container Smoke & Image Verification Notes
- `scripts/test-images.ps1` builds `roleradar-cli:test`, `roleradar-mcp:test`, `roleradar-identity:test`, and `roleradar-web:test`.
- Executes `WebContainerSmokeTests` under production hardening conditions: read-only rootfs, `node` non-root user, tmpfs `/tmp:rw,noexec,nosuid,size=64m`, `cap_drop ALL`, and `no-new-privileges`.
- Verifies in-process TLS on port 5003, OIDC discovery handshake against an ephemeral Identity container, CSP headers with dynamic nonces, unauthenticated redirect to Identity with HttpOnly PKCE cookies, inspect security flags, secret log hygiene (no secrets in stdout/stderr), and fail-closed termination on missing secrets or TLS certificates.
- All Docker resources (network, volume, containers) are uniquely named per run (`rrtest-web-*`) and cleaned up immediately in `finally` without touching the developer's compose stack or data volumes.

### Real-Stack Playwright Verification Notes (T062)
- `scripts/test-e2e-real.ps1` runs the full end-to-end integration test (`RealStackEndToEndTests`).
- Spins up real `roleradar-identity:local`, `roleradar-api:local`, `roleradar-web:local`, and a real `mongo:8.0.34` replica set on an isolated Docker network.
- Drives real Chromium via Playwright: performs full 2FA login (password + TOTP), lists postings, filters/searches, transitions disposition, creates an application, edits a company profile, signs out, and asserts re-challenge.
- Directly inspects MongoDB `audit_log` to assert that all three write operations (`posting_transition`, `application_create`, and `company_update`) were durably recorded with expected actor and entity IDs.
- Full teardown in `finally` guarantees zero residue on developer machine.
