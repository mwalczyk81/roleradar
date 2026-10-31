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

# Kestrel in both containers reads the PFX password from this variable. It MUST match the -passout value above.
$env:ROLERADAR_HTTPS_CERT_PASSWORD = "DevCertPassword123!"

# Confirm both certificate files exist as FILES before running `docker compose up` (see warning below).
foreach ($f in "./deploy/certs/dev-cert.pfx", "./deploy/certs/dev-cert.crt") {
    if (-not (Test-Path -LiteralPath $f -PathType Leaf)) { throw "Missing certificate FILE: $f" }
}
```

> [!WARNING]
> `deploy/docker-compose.apps.yml` bind-mounts `./certs/dev-cert.pfx` and `./certs/dev-cert.crt`. If either path does not exist
> when you run `docker compose up`, Docker creates an **empty directory** at that path and Identity (or the API) then fails to
> start with a certificate-loading error. If that happens, stop the stack, delete the empty `dev-cert.*` directories under
> `deploy/certs/`, regenerate the files with the commands above, and start again.

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
7. Confirmation message: `Success: Single owner account provisioned and 2FA verified successfully!`

**Where the owner `sub` appears**: the command prints the exact `sub` value the owner's access tokens will carry, in two places:

- in the TOTP setup block, on a line labelled `Owner subject (sub): <value>`, and
- again after the success message as `ROLERADAR_OWNER_ALLOWED_SUBJECT=<value>`.

The value is the user id in lowercase `D` GUID format (for example `0192e4a1-abcd-7000-8000-00000000face`), exactly as Identity emits it. **Use it verbatim** as `ROLERADAR_OWNER_ALLOWED_SUBJECT` in Step 3. Do not retype it in another case or format (uppercase, braces, or no hyphens): the API compares it with the token's `sub` using strict, case-sensitive ordinal equality, and a mismatch means every request from the owner is rejected.

> [!IMPORTANT]
> **Host CLI vs. container store.** `dotnet run --project src/RoleRadar.Cli -- identity create-owner` writes to `./identity.db` (or the file named by `ROLERADAR_IDENTITY_DB`) **on your machine**. The Identity container reads a different store: `/data/identity/identity.db` on the `roleradar-identity-data` named volume. An owner created with the host command is therefore **not visible** to the containerised Identity host, and login will fail with "Invalid username or password". Use the host command only for the non-container rehearsal in section 8. For the Docker Compose stack, provision into the container's volume as described next.

### Provisioning the owner into the container's store

Run the CLI image against the same named volume. Order matters: the volume must first be created by the Identity image, whose `/data/identity` directory is owned by `1654:1654`. A volume first created by the CLI image (which has no such directory) would be root-owned and Identity could not write to it.

```powershell
# 1. Compose interpolates every service in the file, so all required variables must be set before any compose command.
#    Generate the client secrets once here and keep this PowerShell window: Step 3 reuses them. (Identity reads them at
#    start, so regenerating them later means recreating the identity container.)
$env:ROLERADAR_WEB_CLIENT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:ROLERADAR_HEADLESS_CLIENT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
#    The owner subject does not exist yet; use a placeholder and replace it with the printed value in Step 3.
$env:ROLERADAR_OWNER_ALLOWED_SUBJECT = "pending-owner-provisioning"

# 2. Build the CLI image and start ONLY Identity so the volume is created with the right ownership.
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps build cli
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps up -d identity

# 3. Provision the owner into the container's SQLite store (-it is required: passwords are read with hidden input).
docker run --rm -it --user 1654:1654 `
    -v roleradar-identity-data:/data/identity `
    -e ROLERADAR_IDENTITY_DB="Data Source=/data/identity/identity.db" `
    roleradar-cli:local identity create-owner --username matt

# 4. Copy the printed `Owner subject (sub):` value into ROLERADAR_OWNER_ALLOWED_SUBJECT (Step 3), then start the API.
```

This writes only to the `roleradar-identity-data` volume you intend to use. Do not run it against a volume holding an owner you want to keep: `create-owner` refuses to run when an owner already exists.

> [!NOTE]
> **Backup Policy Note**: The identity store (`/data/identity/identity.db`) is intentionally excluded from RoleRadar database backups. Losing the identity file only invalidates active sessions; domain job-search data in MongoDB remains completely unaffected, and you can re-create the owner account at any time using this command.

---

## 4. Step 3: Generate Required Secrets and Launch Services via Docker Compose

`deploy/docker-compose.apps.yml` fails closed when client secrets or the owner subject are unset (`${ROLERADAR_WEB_CLIENT_SECRET:?set it}`, `${ROLERADAR_HEADLESS_CLIENT_SECRET:?set it}`, `${ROLERADAR_OWNER_ALLOWED_SUBJECT:?set it}`). Generate cryptographically random secrets and export the required environment variables before starting the stack. If you already generated the secrets in the container provisioning block of Step 2 and are still in the same PowerShell window, skip the two secret lines below (regenerating them would not match the running Identity container):

```powershell
# Generate 32-byte random secrets (base64) for the web and headless OIDC clients
$env:ROLERADAR_WEB_CLIENT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:ROLERADAR_HEADLESS_CLIENT_SECRET = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))

# Set the owner's `sub` EXACTLY as printed by `identity create-owner` (the `Owner subject (sub):` line from Step 2).
# This becomes Owner__AllowedSubjects__0 and is compared with strict, case-sensitive ordinal equality.
$env:ROLERADAR_OWNER_ALLOWED_SUBJECT = "<value printed by create-owner, verbatim>"

# (Optional) Override the read-only headless client subject. Identity and the API both read this one variable and default to "roleradar-headless".
$env:ROLERADAR_HEADLESS_ALLOWED_SUBJECT = "roleradar-headless"

# Start MongoDB, the Duende Identity host, and the REST API
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps up -d
```

Verify service health:
```powershell
# Verify container health status reported by internal exec-form --healthcheck probe
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps ps

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
        client_secret = $env:ROLERADAR_HEADLESS_CLIENT_SECRET
        scope         = "roleradar.api.read"
    }

$accessToken = $tokenResponse.access_token
```

> [!NOTE]
> This token comes from the read-only headless client (`roleradar.api.read` scope), so it can call `GET` endpoints but writes are rejected with `403`. The next section verifies a write using the owner's interactive login.

---

## 5a. Step 4a: Verify Interactive Login (Authorization Code + PKCE + TOTP)

This proves the owner can sign in through the login page and that an owner token (scope `roleradar.api`) is authorized for a write.

1. Create a PKCE verifier and challenge, and open the authorize URL against Identity on `https://localhost:5001`:

```powershell
$verifier  = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(48)) -replace '\+','-' -replace '/','_' -replace '=',''
$challenge = [Convert]::ToBase64String([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::ASCII.GetBytes($verifier))) -replace '\+','-' -replace '/','_' -replace '=',''
$redirect  = "https://localhost:5002/signin-oidc"   # default registered redirect URI for roleradar-web

$authorizeUrl = "https://localhost:5001/connect/authorize?client_id=roleradar-web" +
    "&redirect_uri=$([uri]::EscapeDataString($redirect))" +
    "&response_type=code" +
    "&scope=$([uri]::EscapeDataString('openid profile roleradar.api offline_access'))" +
    "&code_challenge=$challenge&code_challenge_method=S256&state=quickstart"

Start-Process $authorizeUrl   # accept the self-signed certificate warning in the browser
```

2. On the login page, enter the owner username and password, then enter the current 6-digit authenticator code on the second screen.
3. The browser is redirected to `https://localhost:5002/signin-oidc?code=...&state=quickstart`. **A `404 Not Found` page at `/signin-oidc` is expected**: the REST API does not implement a sign-in callback. Copy the `code` value from the address bar.
4. **The authorization code lives for 60 seconds.** Exchange it immediately, or repeat from step 1:

```powershell
$code = "<code from the address bar>"
$owner = Invoke-RestMethod -Uri "https://localhost:5001/connect/token" -Method Post -SkipCertificateCheck -Body @{
    grant_type    = "authorization_code"
    client_id     = "roleradar-web"
    client_secret = $env:ROLERADAR_WEB_CLIENT_SECRET
    code          = $code
    redirect_uri  = $redirect
    code_verifier = $verifier
}
$ownerHeaders = @{ Authorization = "Bearer $($owner.access_token)" }
```

5. Expect a write to pass authorization. For example, update a company's description (an authorization failure would be `403`; any `2xx`, or a `404`/`400` for a bad id, means authorization passed):

```powershell
$company = (Invoke-RestMethod -Uri "https://localhost:5002/api/v1/companies?limit=1" -Headers $ownerHeaders -SkipCertificateCheck).items[0]
Invoke-RestMethod -Uri "https://localhost:5002/api/v1/companies/$($company.id)" -Method Patch -Headers $ownerHeaders `
    -SkipCertificateCheck -ContentType "application/json" -Body (@{ description = "quickstart check" } | ConvertTo-Json)
```

If this returns `403`, the token's `sub` does not match `ROLERADAR_OWNER_ALLOWED_SUBJECT`; compare it with the value printed by `create-owner` (decode the access token payload and read `sub`).

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
