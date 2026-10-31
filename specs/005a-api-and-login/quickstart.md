# Quickstart Validation Guide: 005a REST API and Duende Identity Host

**Feature**: `005a-api-and-login` | **Date**: 2026-10-25

This guide provides step-by-step instructions for provisioning the single owner account, exporting the local HTTPS certificate, launching the services in Docker Compose, and verifying authenticated API access.

---

## 1. Prerequisites

1. .NET 10 SDK (`10.0.402` pinned in `global.json`).
2. Docker Engine & Docker Compose.
3. An RFC 6238 TOTP authenticator application (such as a mobile or desktop authenticator app).

---

## 2. Step 1: Generate Development HTTPS Certificate with Container SANs

Because both the identity host (`https://identity:5001`) and the REST API (`https://api:5002`) enforce HTTPS across all environments and use `https://identity:5001` as the canonical OIDC issuer/authority, the TLS certificate **must** include Subject Alternative Names (SANs) for `DNS:identity`, `DNS:api`, `DNS:localhost`, and `IP:127.0.0.1`.

Because `roleradar-api` runs on `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled` (which has no shell or `update-ca-certificates`), the API container trusts the development certificate by mounting the public PEM certificate at `/https/dev-cert.crt:ro` and setting `SSL_CERT_FILE=/https/dev-cert.crt` in `deploy/docker-compose.apps.yml`.

```powershell
# Create certificate directory if not present (git-ignored via deploy/certs/ and *.pfx)
New-Item -ItemType Directory -Force -Path "./deploy/certs"

# Generate a self-signed certificate with SANs for identity, api, localhost, and 127.0.0.1,
# exporting both PKCS#12 (dev-cert.pfx for Kestrel) and PEM (dev-cert.crt for SSL_CERT_FILE trust):
openssl req -x509 -newkey rsa:2048 -sha256 -days 365 -nodes `
    -keyout "./deploy/certs/dev-cert.key" `
    -out "./deploy/certs/dev-cert.crt" `
    -subj "/CN=identity" `
    -addext "subjectAltName=DNS:identity,DNS:api,DNS:localhost,IP:127.0.0.1"

openssl pkcs12 -export `
    -out "./deploy/certs/dev-cert.pfx" `
    -inkey "./deploy/certs/dev-cert.key" `
    -in "./deploy/certs/dev-cert.crt" `
    -passout pass:DevCertPassword123!
```

---

## 3. Step 2: Provision Single Owner Account via CLI

Run the CLI command to initialize the identity operational datastore and enroll the single owner with password + TOTP 2FA:

```powershell
dotnet run --project src/RoleRadar.Cli -- identity create-owner
```

**Interactive Prompts**:
1. Enter username: `matt`
2. Enter password (input hidden): `[SecurePassword123!]`
3. Confirm password: `[SecurePassword123!]`
4. The terminal renders an ASCII QR code alongside the raw Base32 secret string.
5. Scan the QR code with your authenticator app.
6. Enter the 6-digit TOTP code displayed in your app to verify setup: `123456`
7. Confirmation message: `[OK] Owner account 'matt' successfully created with TOTP two-factor authentication.`

> [!NOTE]
> **Backup Policy Note**: The identity store (`/data/identity/identity.db`) is intentionally excluded from RoleRadar database backups. Losing the identity file only invalidates active sessions; domain job-search data in MongoDB remains completely unaffected, and you can re-create the owner account at any time using this command.

---

## 4. Step 3: Generate Required Secrets and Launch Services via Docker Compose

`deploy/docker-compose.apps.yml` fails closed when client secrets or the owner subject are unset (`${ROLERADAR_WEB_CLIENT_SECRET:?set it}`, `${ROLERADAR_HEADLESS_CLIENT_SECRET:?set it}`, `${ROLERADAR_OWNER_ALLOWED_SUBJECT:?set it}`). Generate cryptographically random secrets and export the required environment variables before starting the stack:

```powershell
# Generate 32-byte URL-safe random secrets for the web and headless OIDC clients
$env:ROLERADAR_WEB_CLIENT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:ROLERADAR_HEADLESS_CLIENT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# Set the interactive owner's provisioned UserId (GUID from Step 2) as Owner__AllowedSubjects__0
$env:ROLERADAR_OWNER_ALLOWED_SUBJECT = "<provisioned-owner-user-guid>"

# (Optional) Enable the read-only headless client subject as Owner__AllowedSubjects__1 (defaults to "roleradar-headless" in Identity)
$env:ROLERADAR_HEADLESS_ALLOWED_SUBJECT = "roleradar-headless"

# Start MongoDB, the Duende Identity host, and the REST API
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps up -d
```

Verify service health:
```powershell
# Verify container health status reported by internal exec-form --healthcheck probe
docker compose -f deploy/docker-compose.apps.yml ps

# Probe Identity discovery endpoint
curl -k https://localhost:5001/.well-known/openid-configuration
# Returns: OIDC discovery JSON
```

---

## 5. Step 4: Obtain Access Token via Client Credentials

To quickly test headless API access:

```powershell
$tokenResponse = Invoke-RestMethod -Uri "https://localhost:5001/connect/token" `
    -Method Post `
    -SkipCertificateCheck `
    -Body @{
        grant_type    = "client_credentials"
        client_id     = "roleradar-headless"
        client_secret = "HeadlessClientSecret123!"
        scope         = "roleradar.api.read"
    }

$accessToken = $tokenResponse.access_token
```

---

## 6. Step 5: Execute Authenticated REST API Query

Query job postings via the REST API using the bearer token:

```powershell
$headers = @{
    Authorization = "Bearer $accessToken"
}

$postings = Invoke-RestMethod -Uri "https://localhost:5002/api/v1/postings?limit=10" `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "Total postings visible through API: $($postings.totalCount)"
```

**Expected Outcome**:
- Total count matches the number of postings stored in MongoDB for `local-owner` (e.g. 232 postings if populated from 004 import).
- Data matches MCP tool query results (`tracker_query`).

---

## 7. Step 6: Verify Concurrency Conflict Detection (HTTP 409)

Attempt to transition a posting using a stale version token:

```powershell
$postingId = $postings.items[0].id

try {
    Invoke-RestMethod -Uri "https://localhost:5002/api/v1/postings/$postingId/transition" `
        -Method Post `
        -Headers $headers `
        -SkipCertificateCheck `
        -ContentType "application/json" `
        -Body (@{
            toDisposition = "interviewing"
            reason        = "Test transition"
            versionToken  = "stale-version-token-12345"
        } | ConvertTo-Json)
} catch {
    $response = $_.Exception.Response
    Write-Host "Expected HTTP Status: $($response.StatusCode.value__)" # 409
}
```

**Expected Outcome**: Returns HTTP 409 Conflict with `errorCode: "concurrency_conflict"`.

---

## 8. Operator Real-Data Rehearsal Procedure (Throwaway Database Override)

Before pointing `RoleRadar.Api` at the primary `roleradar` database, rehearse the API and OIDC workflow against a throwaway database restored from an encrypted backup using the `ROLERADAR_MONGO_DATABASE` environment variable override:

```powershell
# 1. Restore a recent encrypted backup into an isolated throwaway database
$env:ROLERADAR_MONGO_DATABASE = "roleradar_rehearsal_005a"
dotnet run --project src/RoleRadar.Cli -- restore --input "./backups/latest.rrbak"

# 2. Provision the owner account in a dedicated rehearsal SQLite store
$env:ROLERADAR_IDENTITY_DB = "Data Source=./rehearsal-identity.db"
dotnet run --project src/RoleRadar.Cli -- identity create-owner --username owner

# 3. Note the provisioned UserId (or query AspNetUsers.Id) and launch RoleRadar.Identity and RoleRadar.Api
$env:Owner__AllowedSubjects__0 = "<provisioned-owner-user-guid>"
dotnet run --project src/RoleRadar.Api

# 4. Verify read and write parity against the throwaway database:
#    - GET /api/v1/postings, /api/v1/companies, /api/v1/applications, /api/v1/criteria, /api/v1/notes, /api/v1/search-runs
#    - Perform a sample PATCH /api/v1/companies/{id} and POST /api/v1/postings/{id}/transition
#    - Confirm audit_log entries in roleradar_rehearsal_005a record actor = "api-user"

# 5. Clean up the throwaway rehearsal database and temporary SQLite file when satisfied
Remove-Item -Force "./rehearsal-identity.db" -ErrorAction SilentlyContinue
Remove-Item Env:ROLERADAR_MONGO_DATABASE -ErrorAction SilentlyContinue
Remove-Item Env:ROLERADAR_IDENTITY_DB -ErrorAction SilentlyContinue
```
