import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  COMPANY_STATUS_VALUES,
  OWNERSHIP_TYPE_VALUES,
  COMPANY_STATUS_OPTIONS,
  OWNERSHIP_TYPE_OPTIONS,
  COMPANY_STATUS_METADATA,
  isCompanyStatus,
  isOwnershipType,
} from "../../app/lib/company-enums.js";

describe("company-enums.ts contract drift guard", () => {
  it("matches contracts/company-enums.json exactly", () => {
    const jsonPath = path.resolve(process.cwd(), "../contracts/company-enums.json");
    const altPath = path.resolve(process.cwd(), "contracts/company-enums.json");
    const targetPath = fs.existsSync(jsonPath) ? jsonPath : altPath;

    expect(fs.existsSync(targetPath)).toBe(true);

    const json = JSON.parse(fs.readFileSync(targetPath, "utf8")) as {
      statuses: string[];
      ownershipTypes: string[];
    };

    // 1. Statuses match Domain snapshot
    expect([...COMPANY_STATUS_VALUES]).toEqual(json.statuses);

    // 2. Ownership types match Domain snapshot
    expect([...OWNERSHIP_TYPE_VALUES]).toEqual(json.ownershipTypes);
  });

  it("provides options for all status and ownership values", () => {
    const statusOptionValues = COMPANY_STATUS_OPTIONS.map((o) => o.value);
    for (const val of COMPANY_STATUS_VALUES) {
      expect(statusOptionValues).toContain(val);
      expect(COMPANY_STATUS_METADATA[val]).toBeDefined();
    }

    const ownershipOptionValues = OWNERSHIP_TYPE_OPTIONS.map((o) => o.value);
    for (const val of OWNERSHIP_TYPE_VALUES) {
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
