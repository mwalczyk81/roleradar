# ADR 0015: Identity Operational Store Selection — SQLite File Volume vs. PostgreSQL Container

- **Status**: Accepted
- **Date**: 2026-10-25
- **Feature**: `005a-api-and-login`

## Context and Problem Statement

Feature 005a introduces a dedicated identity host (`RoleRadar.Identity`) using Duende IdentityServer to authenticate the single system owner and issue cryptographically signed OpenID Connect / OAuth2 access and refresh tokens.

Duende IdentityServer and ASP.NET Core Identity require operational persistence for:

1. Operational grants (persisted refresh tokens, authorization codes, user consent).
2. Cryptographic signing keys (automatic key management with periodic rotation).
3. Single owner credential state (username, Argon2id/Identity password hash, TOTP authenticator secret, failed attempt lockout counters).

Per Constitution Principle IV (Named Seams Only) and ADR-0001, MongoDB is the sole persistence store for RoleRadar domain aggregates (job postings, companies, applications, notes). Storing identity operational state and signing keys in the domain MongoDB was considered and rejected to avoid coupling identity lifecycle with domain schema backups.

The feature specification also establishes that the identity datastore is **intentionally excluded from RoleRadar domain backups**: losing identity state only invalidates active sessions and transient grants, and the single owner account can be recreated immediately via the CLI command `roleradar identity create-owner`.

What storage engine should back the identity host's operational grants, automatic signing keys, and user store?

## Decision Drivers

- **Domain Isolation (ADR-0001 & Principle IV)**: MongoDB remains the sole domain datastore; relational or key-value storage for identity must not leak into domain repositories.
- **Backup Boundary Simplicity**: Identity operational state is ephemeral relative to domain records and excluded from domain backups.
- **Local-First & Container Simplicity**: Minimal runtime overhead, zero unnecessary network listeners, zero extra background daemon containers for local development.
- **Single-Owner Scale**: Only one user account exists; concurrency is limited to the single owner and headless automation clients.
- **Hosting Migration Path**: Must easily run in Docker Compose and transition smoothly to containerized cloud environments (ADR-0012).

## Options Considered

### Option 1: SQLite File Volume via Entity Framework Core (Chosen)

- **Packages**:
  - `Microsoft.EntityFrameworkCore.Sqlite` (version `10.0.14`)
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (version `10.0.14`)
  - `Duende.IdentityServer.EntityFramework` (version `8.0.10`)
  - `Duende.IdentityServer.AspNetIdentity` (version `8.0.10`)
- **Mechanism**: An embedded SQLite database file stored on a dedicated named volume (e.g. `/data/identity/roleradar-identity.db` in Linux containers or a local directory during CLI development).
- **Configuration**: Configured with Write-Ahead Logging (`PRAGMA journal_mode=WAL;`), foreign keys enabled, and automatic EF Core migration execution on identity host startup.

### Option 2: Dedicated PostgreSQL Container Service in Docker Compose

- **Packages**:
  - `Npgsql.EntityFrameworkCore.PostgreSQL` (version `10.0.3`)
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (version `10.0.14`)
  - `Duende.IdentityServer.EntityFramework` (version `8.0.10`)
- **Mechanism**: A separate `postgres:17-alpine` container running in Docker Compose alongside MongoDB, listening on internal network ports.
- **Evaluation**: While highly capable, introducing a third database container (alongside MongoDB and MongoDB tools) adds unnecessary memory consumption (~150MB baseline), extra networking ports, health check orchestration, and operational complexity purely to store one owner user record, signing keys, and rotating refresh tokens.

## Decision Outcome

Chosen design: **Option 1: SQLite File Volume via Entity Framework Core**.

### Technical Rationale

1. **Perfect Fit for Single-Owner Scope**: A single-owner system experiences low concurrency. SQLite in WAL mode handles concurrent readers and serialized writes with sub-millisecond latency and zero network latency.
2. **Zero Extra Containers**: Avoids adding a PostgreSQL container to Docker Compose, keeping the compose stack lean and fast to initialize.
3. **Clean Backup Boundary**: The SQLite file sits on an independent named volume (`identity-data`), cleanly isolated from MongoDB's domain volume (`mongo-data`). Because it is excluded from backup archives per specification, a lost identity volume simply invalidates active refresh tokens; running `roleradar identity create-owner` restores access immediately.
4. **Cloud Portability**: When migrating to Azure Container Apps or AWS (ADR-0012), SQLite can mount to an Azure Files volume or be switched to Azure SQL/PostgreSQL by swapping the EF Core provider configuration in `RoleRadar.Identity` alone, without touching any domain or API projects.

### Exact API Signatures

```csharp
// Program.cs in RoleRadar.Identity
builder.Services.AddDbContext<IdentityAppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("IdentityConnection")
        ?? "Data Source=/data/identity/identity.db"));

builder.Services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
{
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<IdentityAppDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddIdentityServer(options =>
{
    options.Events.RaiseErrorEvents = true;
    options.Events.RaiseInformationEvents = true;
    options.Events.RaiseFailureEvents = true;
    options.Events.RaiseSuccessEvents = true;
    options.KeyManagement.Enabled = true;
    options.KeyManagement.RotationInterval = TimeSpan.FromDays(30);
})
.AddAspNetIdentity<IdentityUser<Guid>>()
.AddConfigurationStore<IdentityAppDbContext>()
.AddOperationalStore<IdentityAppDbContext>(options =>
{
    options.EnableTokenCleanup = true;
    options.TokenCleanupInterval = 3600;
});
```

## Consequences

- **Good**: Zero additional container dependencies; low memory footprint (<30MB for identity storage).
- **Good**: Complete isolation between domain data in MongoDB and authentication state in SQLite.
- **Good**: Excluded from RoleRadar domain backups without complex collection filtering.
- **Neutral**: File locking requires volume mounting permissions in read-only container rootfs environments (addressed by mounting `/data/identity` as an explicit persistent read-write volume while root remains read-only).
