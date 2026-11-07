import { describe, expectTypeOf, it } from "vitest";
import type { components } from "../../../contracts/client/api-types.js";
import { APPLICATION_EVENT_TYPE_METADATA, APPLICATION_OUTCOME_METADATA } from "../../app/lib/application-enums.js";
import { COMPANY_STATUS_METADATA, OWNERSHIP_TYPE_OPTIONS } from "../../app/lib/company-enums.js";
import { DISPOSITION_METADATA } from "../../app/lib/dispositions.js";

type Schemas = components["schemas"];

/** True only for a string-literal union. `string extends T` holds for plain `string`, so a plain string fails. */
type IsLiteralUnion<T> = string extends T ? false : true;

type Disposition = NonNullable<Schemas["PostingDetailDto"]["disposition"]>;
type ApplicationStatus = NonNullable<Schemas["ApplicationDetailDto"]["status"]>;
type CompanyStatus = NonNullable<Schemas["CompanyDetailDto"]["status"]>;
type OwnershipType = NonNullable<Schemas["CompanyDetailDto"]["ownershipType"]>;
type EventType = NonNullable<Schemas["ApplicationEventDto"]["eventType"]>;

describe("generated enum types (contracts/client/api-types.ts)", () => {
  it("types the five mandatory properties as string-literal unions, not plain string", () => {
    expectTypeOf<IsLiteralUnion<Disposition>>().toEqualTypeOf<true>();
    expectTypeOf<IsLiteralUnion<ApplicationStatus>>().toEqualTypeOf<true>();
    expectTypeOf<IsLiteralUnion<CompanyStatus>>().toEqualTypeOf<true>();
    expectTypeOf<IsLiteralUnion<OwnershipType>>().toEqualTypeOf<true>();
    expectTypeOf<IsLiteralUnion<EventType>>().toEqualTypeOf<true>();
  });

  it("keys the UI metadata records by exactly the generated unions", () => {
    expectTypeOf<keyof typeof DISPOSITION_METADATA>().toEqualTypeOf<Disposition>();
    expectTypeOf<keyof typeof APPLICATION_OUTCOME_METADATA>().toEqualTypeOf<ApplicationStatus>();
    expectTypeOf<keyof typeof COMPANY_STATUS_METADATA>().toEqualTypeOf<CompanyStatus>();
    expectTypeOf<keyof typeof APPLICATION_EVENT_TYPE_METADATA>().toEqualTypeOf<EventType>();
    expectTypeOf<(typeof OWNERSHIP_TYPE_OPTIONS)[number]["value"]>().toEqualTypeOf<OwnershipType>();
  });
});
