# ADR 0035: Enum Values in the API Contract Are Generated, Not Snapshotted

- **Status**: Accepted
- **Date**: 2026-11-06
- **Feature**: `010-contracts-test-integrity`

## Context and Problem Statement

The API exposes disposition, application status, company status, ownership type and application event type as plain strings, so `contracts/openapi/roleradar-api.json` and the generated `contracts/client/api-types.ts` typed them as `string`. To keep the web app honest, each Domain enum had a hand-maintained snapshot in `contracts/*.json`, a .NET drift test that rewrote the snapshot on mismatch, and a web equality test. A new enum value therefore touched several files that could silently disagree, and the contract itself never said what the allowed values were.

## Decision Outcome

- **One registry drives the schema.** `EnumContractRegistry` (`src/RoleRadar.Api/OpenApi/`) maps each Domain enum-typed property to the DTO properties that carry it. The OpenAPI schema transformer writes `enum` on those properties from the Domain enum's serialized (snake_case) values, in declaration order. A nullable property also allows `null`. The existing stats `window` enum keeps its own declaration.
- **The contract carries the values.** `contracts/openapi/roleradar-api.json` has `enum` for every registered property and `contracts/client/api-types.ts` has string-literal unions. The web enum modules (`dispositions.ts`, `company-enums.ts`, `application-enums.ts`) take their types from the generated types and hand-write only labels, tones and order. `exhaustiveOrder<T>()` makes an ordered list a compile error when it misses a member of the generated union.
- **The registry is complete by test.** `EnumContractRegistryTests` walks every enum-typed property on the Domain types. Each must be registered or on an explicit allowlist with a reason ("not exposed by the API"). Adding a Domain enum property without deciding fails the test and names it.
- **The generated files stay guarded.** `ApiOpenApiContractSnapshotTests` keeps the committed OpenAPI file equal to the live document, `OpenApiEnumContractTests` checks the enum values against Domain, and the CI step that regenerates `api-types.ts` ends in `git diff --exit-code -- contracts`.
- **Snapshots removed.** `contracts/company-enums.json`, `contracts/application-enums.json` and their drift and equality tests are deleted. `contracts/operational-enums.json` keeps only `criterionCategories`: criteria are not in the REST schema, so those values cannot be generated, and the snapshot still guards the Domain to web copy. Its `supportedEntityRoutes` list was a literal inside the test with no production source, and is removed. `contracts/disposition-matrix.json` keeps the transition table only (behaviour, not membership). `contracts/insights-lenses.json` is unchanged (lens definitions, ADR 0029).
- **OData is deliberately unchanged.** `$filter` on these properties stays `Edm.String` with the lowercase-literal rule of ADR 0028, and `contracts/odata/metadata.xml` does not change. Only the OpenAPI schema and the client types gain the constraint.
- **Bindings are unchanged.** The DTOs still bind these values as strings, so a bad value is answered by the handler with the ErrorEnvelope as before. The schema is documentation and client typing, not a model-binding constraint.

### Consequences

- Good: adding or renaming a Domain enum value changes the schema and the client types in one regeneration, and the web typecheck shows every place that needs a decision.
- Good: no snapshot can be silently rewritten by a drift test; the committed files are generated and compared.
- Good: web actions can no longer pass a raw form string into a typed field. They validate with a type guard and answer an unknown value with a 400 inline field error, as the API would.
- Bad: the registry is one more list to edit when a Domain enum property is added, but the registry test fails until it is done.
- Bad: `contracts/operational-enums.json` still exists for the one enum the REST schema does not expose. It can go when the REST API exposes criteria and `CriterionCategory` is registered.
