# ADR 0020: Transport Security, Local Compose Certificates, and Self-Contained Health Probing

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
6. Container images are distroless/chiseled (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`), which contain **zero shell binaries (`/bin/sh`, `/bin/bash`) and zero auxiliary network utilities (`curl`, `wget`)**.

How should Kestrel TLS certificates, health check probing, and network routing be structured across local development and containerized Docker Compose environments?

## Decision Drivers

- **Zero Unencrypted Token Traffic (Principle I)**: All bearer tokens, client secrets, and passwords must be protected by TLS in transit.
- **Local Dev Parity**: Local Docker Compose execution must match production container TLS behavior without requiring public domain registrations.
- **Chiseled Container Compatibility**: Distroless/chiseled base images lack shell and curl; container health checks must operate via direct executable invocation without shell interpolation.
- **Container Health Orchestration**: Docker Compose and container runtime orchestrators must be able to probe service readiness without bypassing TLS security or requiring bearer tokens.
- **CORS Rejection**: The API is exclusively a backend for the web application (server-to-server) and headless tools; browser script cross-origin invocations must be refused.

## Decision Outcome

Chosen design: **Mounted .NET Dev-Certs / PKCS#12 Volume + Multi-Binding Kestrel Configuration + Self-Contained Binary Health Probe + Exec-Form HEALTHCHECK + Zero CORS**.

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

### 2. Multi-Binding Internal Health Endpoint & Self-Contained Probe Mode

Because chiseled base images have no shell and no curl utility, standard shell health checks (`curl -f http://...`) fail immediately.

To support robust, native health checking in distroless containers:

1. **Internal Loopback HTTP Binding**: Kestrel binds an unauthenticated health probe strictly to internal loopback (`http://127.0.0.1:8080/health`). This port is never mapped or published in Docker Compose host `ports`.
2. **Self-Contained CLI Probe (`--healthcheck`)**:
   Both `RoleRadar.Identity` and `RoleRadar.Api` implement an internal CLI probe mode in `Program.cs`. When invoked with `--healthcheck`:

   ```csharp
   if (args.Contains("--healthcheck"))
   {
       using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2) };
       using var client = new HttpClient(handler);
       try
       {
           var response = await client.GetAsync("http://127.0.0.1:8080/health");
           Environment.Exit(response.IsSuccessStatusCode ? 0 : 1);
       }
       catch
       {
           Environment.Exit(1);
       }
       return;
   }
   ```

3. **Exec-Form HEALTHCHECK in Dockerfile and Compose**:

   ```dockerfile
   # Dockerfile.identity and Dockerfile.api
   HEALTHCHECK --interval=5s --timeout=3s --retries=3 --start-period=4s CMD ["/app/RoleRadar.Identity", "--healthcheck"]
   ```

   Or in `docker-compose.apps.yml`:

   ```yaml
   healthcheck:
     test: ["CMD", "/app/RoleRadar.Identity", "--healthcheck"]
     interval: 5s
     timeout: 3s
     retries: 3
     start_period: 4s
   ```

4. **Launched-Host Test Verification**:
   A dedicated launched-host test in the test suite launches each container under the supported read-only run mode (`docker run --read-only --tmpfs /tmp ...`) and asserts that `docker inspect` reports state `healthy` within the start period.

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
- **Good**: Self-contained probe mode works seamlessly in distroless/chiseled images with zero external shell or package requirements.
- **Good**: Clean separation between secure external HTTPS endpoints and internal loopback health probes.
- **Good**: Eliminates browser cross-origin attack vectors by refusing CORS.
- **Neutral**: Developers must execute a one-time `dotnet dev-certs` export command prior to launching Docker Compose (documented in `quickstart.md`).
