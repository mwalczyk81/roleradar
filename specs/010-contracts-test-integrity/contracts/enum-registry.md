# Contract: Enum registry and generated schema

## Registry rules

1. One registry (`EnumContractRegistry`) maps Domain enum properties to the DTO properties that carry them.
2. The schema transformer sets `enum` on each carried property from the registry; no other code writes enum lists into the schema (the stats `window` enum may stay as is).
3. Values are the serialized snake_case names, in declaration order. Nullable properties keep `null` allowed; array properties apply the enum to `items`.
4. Allowlist entries need a non-empty reason.

## Naming

The Domain enum behind "application status" is `ApplicationOutcome`; the registry and tests use that name. The five mandatory properties are posting disposition (`PostingDisposition`), application status (`ApplicationOutcome`), company status (`CompanyStatus`), ownership type (`OwnershipType`) and application event type (`ApplicationEventType`). Every other Domain enum the API exposes as a string is registered too; unexposed ones are allowlisted with a reason.

## Tests (all fail first)

| Test | Fails when |
|------|-----------|
| Registry completeness | a Domain enum-typed property is not in `Entries` or `Allowlist`; the message names it |
| Allowlist reasons | any reason is blank |
| OpenAPI carries enums | a `CarriedBy` property in `contracts/openapi/roleradar-api.json` has no `enum`, or its list differs from the Domain enum |
| Contract snapshot | the existing `ApiOpenApiContractSnapshotTests` (live `/openapi/v1.json` versus committed file) |
| Client types in sync | CI step: `npx --yes openapi-typescript@7.10.1 contracts/openapi/roleradar-api.json -o contracts/client/api-types.ts` then `git diff --exit-code -- contracts` |
| OData unchanged | the existing `ODataMetadataDriftTests` (reused, not rewritten) fails |
| Web typing | a type-level assertion fails while a property is `string` (for example `string extends T ? false : true` via `expectTypeOf`), so a `Record<string, ...>` cannot satisfy it; an exhaustive `Record<Union, ...>` helper missing a member fails typecheck |

## Generated output expectations

- `components.schemas.<Dto>.properties.<prop>.enum` present for the five mandatory properties: posting disposition, application status (outcome), company status, ownership type, application event type.
- `api-types.ts` types those properties as string-literal unions.

## Web consumption

Helpers define UI facts only (label, tone, order), as `Record<GeneratedUnion, ...>` so a new member is a compile error. Type guards use the record keys. No value list is typed by hand.
