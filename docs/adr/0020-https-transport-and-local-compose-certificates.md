# ADR 0020: Transport Security, Local Compose Certificates, and Internal Health Probing

- **Status**: Accepted
- **Date**: 2026-10-25
- **Feature**: `005a-api-and-login`

## Context and Problem Statement

OpenID Connect / OAuth2 token exchange and secure API operations require strict transport layer security (TLS/HTTPS). Under modern security standards, transmitting authentication tokens, authorization codes, or user credentials over unencrypted HTTP is unacceptable, even in local development or container environments.

The specification establishes that:

1. HTTPS must be enforced everywhere across all host environments, including local Docker Compose.
2. Local Docker Compose must use a locally trusted development certificate mounted into container filesystems.
3. No plain HTTP listeners may exist, except for an internal-only health probe endpoint.
4. The identity authority URL must be fixed and immutable from configuration.
5. Cross-Origin Resource Sharing (CORS) is disabled: the API is strictly called server-to-server; browser cross-origin requests are rejected.

How should Kestrel TLS certificates, health check probing, and network routing be structured across local development and containerized Docker Compose environments?

## Decision Drivers

- **Zero Unencrypted Token Traffic (Principle I)**: All bearer tokens, client secrets, and passwords must be protected by TLS in transit.
- **Local Dev Parity**: Local Docker Compose execution must match production container TLS behavior without requiring public domain registrations.
- **Container Health Orchestration**: Docker Compose and container runtime orchestrators must be able to probe service readiness without bypassing TLS security or requiring bearer tokens.
- **CORS Rejection**: The API is exclusively a backend for the web application (server-to-server) and headless tools; browser script cross-origin invocations must be refused.

## Decision Outcome

Chosen design: **Mounted .NET Dev-Certs / PKCS#12 Volume + Multi-Binding Kestrel Configuration + Internal Anonymous Health Probe + Zero CORS**.

### 1. Local Compose HTTPS Certificate Strategy

1. **Certificate Generation**: Local development uses the standard .NET dev-certs tool:

   ```bash
   dotnet dev-certs https -ep ./deploy/certs/dev-cert.pfx -p "DevCertPassword123!" --trust
   ```

2. **Volume Mounting**: In `docker-compose.yml`, the certificate file is mounted read-only into `/https/cert.pfx` for both `identity` and `api` service containers.
3. **Environment Configuration**: Kestrel is configured via environment variables:
   - `Kestrel__Endpoints__Https__Url=https://*:5001` (Identity) / `https://*:5002` (Api)
   - `Kestrel__Endpoints__Https__Certificate__Path=/https/cert.pfx`
   - `Kestrel__Endpoints__Https__Certificate__Password=DevCertPassword123!`
4. **CA Trust Inside Containers**: The public certificate (.crt) is mounted into `/usr/local/share/ca-certificates/` and trusted inside the API container so that the API's OIDC discovery client can validate the Identity host's TLS certificate without `ServerCertificateCustomValidationCallback` bypasses.

### 2. Multi-Binding Internal Health Endpoint

Container health checks need an unauthenticated, non-sensitive probe that can be invoked by Docker daemon without managing TLS handshakes or certificates:

- Kestrel binds a second, dedicated plain HTTP endpoint on an internal loopback port (`http://localhost:8080/health` or `http://*:8080/health`).
- This port is NOT exposed in Docker Compose `ports` mapping to the host system.
- The Dockerfile and Compose file specify:

  ```yaml
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
    interval: 5s
    timeout: 3s
    retries: 3
    start_period: 4s
  ```

- All other endpoints (auth, token, API data) are bound exclusively to the HTTPS port.

### 3. Fixed Authority URL

The identity authority is configured explicitly in both services:

- In `RoleRadar.Identity`: `IdentityServerOptions.IssuerUri = builder.Configuration["Identity:IssuerUri"]` (e.g. `https://localhost:5001`).
- In `RoleRadar.Api`: `JwtBearerOptions.Authority = builder.Configuration["Authentication:Authority"]` (matching the issuer URI).
- Mismatched issuer claims (`iss`) in incoming tokens are rejected with HTTP 401 Unauthorized.

### 4. Zero CORS Enforcement

Because `RoleRadar.Api` is strictly consumed server-to-server by the web application host (Feature 005b) and headless jobs:

- No CORS middleware (`UseCors`) is registered.
- Requests carrying an `Origin` header receive standard responses without `Access-Control-Allow-Origin` headers, causing browsers to reject any direct client-side script calls.

## Consequences

- **Good**: 100% encrypted traffic across all environments, eliminating unencrypted token transmission.
- **Good**: Clean separation between secure external HTTPS endpoints and internal container health probes.
- **Good**: Eliminates browser cross-origin attack vectors by refusing CORS.
- **Neutral**: Developers must execute a one-time `dotnet dev-certs` export command prior to launching Docker Compose (documented in `quickstart.md`).
