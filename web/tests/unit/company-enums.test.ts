import { describe, it, expect } from "vitest";
import {
  COMPANY_STATUS_ORDER,
  OWNERSHIP_TYPE_ORDER,
  COMPANY_STATUS_OPTIONS,
  OWNERSHIP_TYPE_OPTIONS,
  COMPANY_STATUS_METADATA,
  isCompanyStatus,
  isOwnershipType,
} from "../../app/lib/company-enums.js";
import { hasUniqueMembers } from "../../app/lib/enum-order.js";

describe("company-enums.ts (values come from the generated API types, ADR 0035)", () => {
  it("lists every value once in the UI order lists", () => {
    expect(hasUniqueMembers(COMPANY_STATUS_ORDER)).toBe(true);
    expect(hasUniqueMembers(OWNERSHIP_TYPE_ORDER)).toBe(true);
  });

  it("provides options for all status and ownership values", () => {
    const statusOptionValues = COMPANY_STATUS_OPTIONS.map((o) => o.value);
    for (const val of COMPANY_STATUS_ORDER) {
      expect(statusOptionValues).toContain(val);
      expect(COMPANY_STATUS_METADATA[val]).toBeDefined();
    }

    const ownershipOptionValues = OWNERSHIP_TYPE_OPTIONS.map((o) => o.value);
    for (const val of OWNERSHIP_TYPE_ORDER) {
      expect(ownershipOptionValues).toContain(val);
    }
  });

  it("isCompanyStatus acts as a safe runtime type guard", () => {
    // Valid values
    expect(isCompanyStatus("target")).toBe(true);
    expect(isCompanyStatus("watch")).toBe(true);
    expect(isCompanyStatus("avoid")).toBe(true);
    expect(isCompanyStatus("neutral")).toBe(true);

    // Invalid values
    expect(isCompanyStatus("unknown_status")).toBe(false);
    expect(isCompanyStatus("TARGET")).toBe(false);
    expect(isCompanyStatus("")).toBe(false);
    expect(isCompanyStatus(null)).toBe(false);
    expect(isCompanyStatus(undefined)).toBe(false);
    expect(isCompanyStatus(123)).toBe(false);
    expect(isCompanyStatus({})).toBe(false);
  });

  it("isOwnershipType acts as a safe runtime type guard", () => {
    // Valid values
    expect(isOwnershipType("public")).toBe(true);
    expect(isOwnershipType("private")).toBe(true);
    expect(isOwnershipType("pe_backed")).toBe(true);
    expect(isOwnershipType("unknown")).toBe(true);

    // Invalid values
    expect(isOwnershipType("invalid_ownership")).toBe(false);
    expect(isOwnershipType(null)).toBe(false);
    expect(isOwnershipType(undefined)).toBe(false);
    expect(isOwnershipType(42)).toBe(false);
  });
});
