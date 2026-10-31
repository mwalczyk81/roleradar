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
- **.NET SDK**: v10.0+
- **Docker & Docker Compose**: running Docker daemon
- **Development Certificates**: generated in `deploy/certs/` (`dev-cert.pfx`, `dev-cert.crt`, `dev-cert.key`)

### Environment Configuration

Create or verify `web/.env.local`:
```bash
NODE_ENV=development
PORT=5003
ROLERADAR_IDENTITY_ISSUER_URI=https://localhost:5001
ROLERADAR_API_BASE_URL=https://localhost:5002
ROLERADAR_WEB_CLIENT_SECRET=<the same value exported for the compose stack; never a shared default>
SESSION_ENCRYPTION_KEY=<32 random bytes, base64; generate your own as shown below>
NODE_EXTRA_CA_CERTS=../deploy/certs/dev-cert.crt
HTTPS_CERT_PATH=../deploy/certs/dev-cert.crt
HTTPS_CERT_KEY_PATH=../deploy/certs/dev-cert.key
```

Generate the session key once per machine (never commit it, never reuse a value copied from documentation):

```powershell
[Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

---

## 2. Running Supporting Services (Identity & API)

Before starting the web application, start the backend datastore, Identity host, and REST API:

```powershell
# 1. Start MongoDB and infrastructure services
pwsh ./scripts/start-services.ps1

# 2. Build and start Identity and API services in Compose
docker compose -f deploy/docker-compose.apps.yml up -d identity api
```

Verify services are healthy:
- Identity: `https://localhost:5001/health`
- API: `https://localhost:5002/health`

---

## 3. Running the Web Application Locally

```powershell
# 1. Install monorepo dependencies
pnpm install

# 2. Build contracts and run TypeScript typecheck
pnpm exec nx run web:typecheck

# 3. Start the React Router development server with in-process TLS
pnpm exec nx run web:dev
```

The application will listen on **`https://localhost:5003`**.

---

## 4. Running the Web Application via Docker Compose

To test the containerized web service with its read-only filesystem, tmpfs, and unprivileged user:

```powershell
# Build and run the entire stack including the web service
docker compose -f deploy/docker-compose.apps.yml up -d --build web

# Check container health and logs
docker compose -f deploy/docker-compose.apps.yml ps web
docker compose -f deploy/docker-compose.apps.yml logs -f web
```

---

## 5. End-to-End Interactive Verification Workflow

Follow this manual checklist to verify all P1 user stories:

### Step 1: Unauthenticated Visit & Redirect
1. Open an incognito browser window and navigate to `https://localhost:5003/postings`.
2. **Expected Outcome**: The browser is immediately redirected to `https://localhost:5001/Account/Login` with valid `client_id=roleradar-web`, `response_type=code`, `code_challenge`, and `state`.

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
2. Select target disposition `screened_in` (notice `applied` is not offered).
3. Enter a transition reason: `"Strong alignment with screening criteria"`.
4. Submit the transition.
5. **Expected Outcome**: Toast confirms successful transition; posting detail shows updated disposition.
6. To test concurrency conflict: In a second tab, submit another transition on the same posting. Return to the first tab and attempt another update without refreshing.
7. **Expected Outcome**: The API returns HTTP 409 Conflict; the UI captures the error and displays a Toast: *"This posting was modified elsewhere. [Reload Latest]"*. Clicking "Reload Latest" fetches the fresh state without corrupting data.

### Step 6: Sign Out
1. Click the user menu in the top right corner of the `AppShell`.
2. Click "Sign Out".
3. **Expected Outcome**: The server evicts the session, expires the session cookie, redirects to Identity's end-session endpoint, and completes RP-Initiated Logout. Attempting to return to `https://localhost:5003/postings` prompts for login.

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
```

### Playwright Verification Notes
- The Playwright smoke test signs in via the real Identity container using throwaway credentials.
- After executing a disposition transition, the test connects directly to the ephemeral MongoDB container and asserts that an append-only entry exists in the `audit_log` collection with the action and owner subject.
- Never connects to or mutates developer database instances.
