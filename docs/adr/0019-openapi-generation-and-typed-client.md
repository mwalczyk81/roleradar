# ADR 0019: OpenAPI Contract Generation, Committed Snapshot Verification, and Typed Client Generation

- **Status**: Accepted
- **Date**: 2026-10-25
- **Feature**: `005a-api-and-login`

## Context and Problem Statement

Feature 005a establishes the API surface that will be consumed in Feature 005b by a TypeScript/React management user interface.

During previous MCP tool integration, clients experienced deserialization errors when schemas contained untyped or loosely typed parameters (e.g. numeric values supplied to untyped string fields). To eliminate schema ambiguity and prevent client-server drift between backend .NET services and frontend web applications:

1. The API contract must be fully documented using OpenAPI v3.1 with explicit, non-null scalar types for every parameter and property.
2. The generated OpenAPI contract must be committed to the repository and protected against unreviewed or accidental drift by an automated contract snapshot test.
3. A strongly-typed TypeScript client must be generated from the verified OpenAPI schema for consumption by Feature 005b.

What tooling and build workflow should generate the OpenAPI specification, verify contract stability, and generate client types?

## Decision Drivers

- **Zero Untyped Fields**: Every query parameter, path parameter, request body property, and response field must declare an explicit scalar type (`string`, `integer`, `boolean`, `number`, `array`, `object`) and appropriate nullability.
- **Contract Drift Prevention**: The OpenAPI document must be committed under version control, and CI must fail if an endpoint changes without an updated contract snapshot.
- **Native .NET 10 Tooling**: Prefer Microsoft's built-in OpenAPI generator introduced in .NET 9/10 over third-party runtime reflection packages (such as Swashbuckle or NSwag) that require runtime startup.
- **Type-Safe Frontend Generation**: Produce idiomatic TypeScript interfaces matching OpenAPI schema models.

## Decision Outcome

Chosen design: **Microsoft.AspNetCore.OpenApi (10.0.14) + Microsoft.Extensions.ApiDescription.Server (10.0.14) + Contract Snapshot Test + openapi-typescript Generator**.

### 1. Build-Time OpenAPI Generation

`RoleRadar.Api` integrates built-in ASP.NET Core 10 OpenAPI generation:

- **Packages**:
  - `Microsoft.AspNetCore.OpenApi` (version `10.0.14`)
  - `Microsoft.Extensions.ApiDescription.Server` (version `10.0.14`)
- **Build Configuration**:

  ```xml
  <PropertyGroup>
    <OpenApiDocumentsDirectory>$(MSBuildThisFileDirectory)../../contracts/openapi</OpenApiDocumentsDirectory>
    <OpenApiGenerateDocumentsOnBuild>true</OpenApiGenerateDocumentsOnBuild>
  </PropertyGroup>
  ```

  On every build of `RoleRadar.Api`, `Microsoft.Extensions.ApiDescription.Server` generates `contracts/openapi/roleradar-api.json` directly from endpoint metadata without requiring the application to listen on network ports.

### 2. Explicit Type Enforcement

Minimal API endpoints apply explicit OpenAPI transformers to ensure zero untyped schema fields:

- Every GUID identifier is explicitly typed as `"type": "string", "format": "uuid"`.
- Every numeric parameter is explicitly typed as `"type": "integer"`.
- Every optional parameter explicitly specifies `"nullable": true`.
- Every enum property includes an exhaustive string enum array with descriptive summaries.

### 3. Committed Contract Snapshot Test

A dedicated contract test in `RoleRadar.Tests.Contract` (`ApiOpenApiContractSnapshotTests`) executes during test runs:

1. Loads the build-generated `contracts/openapi/roleradar-api.json`.
2. Compares the generated JSON structural AST against the committed baseline snapshot.
3. Fails the build with a detailed diff report if any endpoint path, HTTP method, parameter type, status code, or response schema has changed without updating the committed snapshot.

### 4. Typed TypeScript Client Generation for 005b

Using `openapi-typescript` (version `7.x`), a Node script in `contracts/` generates compile-time TypeScript type definitions:

```bash
npx openapi-typescript contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts
```

This generates pure TypeScript `paths`, `operations`, and `components['schemas']` definitions that Feature 005b's React components and typed fetch clients consume with complete type safety.

## Consequences

- **Good**: Fully automated, build-time contract generation with zero runtime reflection overhead.
- **Good**: The committed snapshot guarantees that no API endpoint or contract model changes unnoticed during code reviews or pull requests.
- **Good**: Seamless TypeScript type generation for Feature 005b eliminates manual API typing errors.
