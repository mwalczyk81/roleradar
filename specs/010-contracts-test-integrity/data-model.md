# Data Model: Contracts and Test Integrity

No persisted data changes. No Mongo collection, index or `owner_id` change. The entities below are build-time and test-time structures.

## EnumContractEntry (Api, `EnumContractRegistry`)

| Field | Meaning |
|-------|---------|
| DomainProperty | The Domain type and property that holds the enum (for example `Company.Status`). Used by the registry test to find unregistered properties. |
| EnumType | The Domain enum. Serialized values are `ToSnakeCase()` of each member, in declaration order. |
| CarriedBy | List of `(DtoType, jsonProperty)` that expose the value as a string in responses or request bodies. |
| Shape | scalar, nullable scalar, or array of values. Drives how `enum` is placed in the schema. |

Validation: every `CarriedBy` pair must exist as a schema property in the generated document; every Domain enum-typed property must appear in `Entries` or `Allowlist`.

## EnumAllowlistEntry

`DomainProperty` plus a non-empty `Reason`. A blank reason fails the registry test.

## TrivyReport (read only, produced by CI)

File `TestResults/trivy-report-<target>.json` for target in cli, mcp, identity, api, web. Fields used: `ArtifactName`, `Results[].Vulnerabilities[].{VulnerabilityID, Severity, FixedVersion}`. Valid only if it parses and `ArtifactName` matches the expected image for that target.

## ScanVerdict (test-side)

`Pass`, `Fail(findings)`, `Skip(reason)` (local, no reports), `Fail(reason)` (CI, no reports, malformed, wrong image).

## TestLane (xUnit trait)

Trait key `Lane`. Values: `images`, `web-container`, `real-stack`. Untagged classes belong to `default`. Used by scripts and CI filters and by the coverage test.

## State transitions

None. `disposition-matrix.json` transition table is unchanged behaviour data.
