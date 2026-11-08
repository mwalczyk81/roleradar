import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  CRITERION_CATEGORY_VALUES,
  CRITERION_CATEGORY_METADATA,
  isCriterionCategory,
  isSupportedEntityRouteType,
  getEntityRoute,
} from "../../app/lib/operational-enums.js";

describe("operational-enums.ts contract drift guard", () => {
  it("matches contracts/operational-enums.json exactly", () => {
    const jsonPath = path.resolve(process.cwd(), "../contracts/operational-enums.json");
    const altPath = path.resolve(process.cwd(), "contracts/operational-enums.json");
    const targetPath = fs.existsSync(jsonPath) ? jsonPath : altPath;

    expect(fs.existsSync(targetPath)).toBe(true);

    const json = JSON.parse(fs.readFileSync(targetPath, "utf8")) as {
      criterionCategories: string[];
    };

    // CriterionCategory is not in the REST contract (criteria come through OData), so the snapshot still guards it.
    expect([...CRITERION_CATEGORY_VALUES]).toEqual(json.criterionCategories);
  });

  it("provides metadata for all criterion categories", () => {
    for (const val of CRITERION_CATEGORY_VALUES) {
      expect(CRITERION_CATEGORY_METADATA[val]).toBeDefined();
      expect(typeof CRITERION_CATEGORY_METADATA[val].label).toBe("string");
    }
  });

  it("isCriterionCategory acts as a safe runtime type guard", () => {
    expect(isCriterionCategory("hard_skip")).toBe(true);
    expect(isCriterionCategory("weight")).toBe(true);
    expect(isCriterionCategory("preference")).toBe(true);

    expect(isCriterionCategory("invalid")).toBe(false);
    expect(isCriterionCategory(null)).toBe(false);
  });

  it("isSupportedEntityRouteType acts as a safe runtime type guard", () => {
    expect(isSupportedEntityRouteType("posting")).toBe(true);
    expect(isSupportedEntityRouteType("company")).toBe(true);
    expect(isSupportedEntityRouteType("application")).toBe(true);

    expect(isSupportedEntityRouteType("contact")).toBe(false);
    expect(isSupportedEntityRouteType("unknown")).toBe(false);
  });

  it("getEntityRoute returns correct route path or null", () => {
    expect(getEntityRoute("posting", "p-123")).toBe("/postings/p-123");
    expect(getEntityRoute("company", "c-456")).toBe("/companies/c-456");
    expect(getEntityRoute("application", "a-789")).toBe("/applications/a-789");
    expect(getEntityRoute("contact", "ct-1")).toBe(null);
    expect(getEntityRoute("unknown", "u-1")).toBe(null);
    expect(getEntityRoute(null, "p-1")).toBe(null);
    expect(getEntityRoute("posting", null)).toBe(null);
  });

  it("getEntityRoute ignores case and surrounding whitespace in the type", () => {
    expect(getEntityRoute("  Company  ", "c-1")).toBe("/companies/c-1");
    expect(getEntityRoute("POSTING", "p-1")).toBe("/postings/p-1");
    expect(getEntityRoute(" \tApplication\n ", "a-1")).toBe("/applications/a-1");
  });

  it("keeps the label and tone shown for each criterion category", () => {
    expect(CRITERION_CATEGORY_METADATA).toEqual({
      hard_skip: { label: "Hard Skip", tone: "danger" },
      weight: { label: "Weighted", tone: "primary" },
      preference: { label: "Preference", tone: "neutral" },
    });
  });
});
