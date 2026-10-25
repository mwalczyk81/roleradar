# OpenAPI Contract & Snapshot Verification Contract

**Feature**: `005a-api-and-login` | **Date**: 2026-10-25

This document establishes the build-time OpenAPI contract generation and snapshot verification process.

---

## 1. OpenAPI Specification File Location

- **Build Output Target**: `contracts/openapi/roleradar-api.json`
- **Spec Format**: OpenAPI Specification v3.1
- **Generator**: Built-in `Microsoft.AspNetCore.OpenApi` (10.0.14) via `Microsoft.Extensions.ApiDescription.Server` (10.0.14) triggered during MSBuild compilation of `RoleRadar.Api`.

---

## 2. Explicit Type Declaration Invariants

To avoid untyped schemas (which caused deserialization errors during previous MCP tool integrations), all generated schema components must adhere to:

1. **Explicit Scalar Types**: Every schema property MUST declare a non-empty `"type"` attribute:
   - Identifiers: `"type": "string", "format": "uuid"`
   - Numbers & Counts: `"type": "integer"` or `"type": "number"`
   - Booleans: `"type": "boolean"`
   - Dates & Timestamps: `"type": "string", "format": "date-time"`
2. **Explicit Nullability**: Optional fields must declare `"nullable": true`.
3. **Required Fields**: Request bodies and response objects MUST explicitly declare their `"required"` property arrays.
4. **Enums**: Enum parameters must be represented as typed string enums with complete allowed value sets (e.g. `disposition`, `resurfacePolicy`, `ownershipType`, `companyStatus`).

---

## 3. Automated Contract Snapshot Gate

A contract test in `RoleRadar.Tests.Contract` (`ApiOpenApiContractSnapshotTests`) executes on every test run:

1. Resolves the path to `contracts/openapi/roleradar-api.json`.
2. Reads the committed JSON file and normalizes formatting (indentation, line breaks).
3. Compares the structural elements (paths, methods, operation IDs, parameter definitions, request schemas, response codes) against the committed reference document.
4. If discrepancies are found (endpoints added, deleted, or altered without updating the snapshot):
   - Emits a precise structural JSON diff to test output.
   - Fails the test with a descriptive failure message instructing the developer to review and commit the intentional API change.

---

## 4. Typed Client Generation for Feature 005b

A Node/npm script in `contracts/` generates compile-time TypeScript definitions from the committed OpenAPI contract:

```bash
npx openapi-typescript contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts
```

Output:
- `contracts/client/api-types.ts`: Contains strongly-typed `paths`, `operations`, and `components["schemas"]` interfaces used by Feature 005b.
