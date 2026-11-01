import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  APPLICATION_OUTCOME_VALUES,
  APPLICATION_EVENT_TYPE_VALUES,
  RESUME_VARIANT_VALUES,
  APPLICATION_OUTCOME_OPTIONS,
  RESUME_VARIANT_OPTIONS,
  APPENDABLE_EVENT_OPTIONS,
  APPLICATION_OUTCOME_METADATA,
  APPLICATION_EVENT_TYPE_METADATA,
  isApplicationOutcome,
  isApplicationEventType,
  isResumeVariant,
} from "../../app/lib/application-enums.js";

describe("application-enums.ts contract drift guard", () => {
  it("matches contracts/application-enums.json exactly", () => {
    const jsonPath = path.resolve(process.cwd(), "../contracts/application-enums.json");
    const altPath = path.resolve(process.cwd(), "contracts/application-enums.json");
    const targetPath = fs.existsSync(jsonPath) ? jsonPath : altPath;

    expect(fs.existsSync(targetPath)).toBe(true);

    const json = JSON.parse(fs.readFileSync(targetPath, "utf8")) as {
      outcomes: string[];
      eventTypes: string[];
      resumeVariants: string[];
    };

    // 1. Outcomes match Domain snapshot
    expect([...APPLICATION_OUTCOME_VALUES]).toEqual(json.outcomes);

    // 2. Event types match Domain snapshot
    expect([...APPLICATION_EVENT_TYPE_VALUES]).toEqual(json.eventTypes);

    // 3. Resume variants match Domain snapshot
    expect([...RESUME_VARIANT_VALUES]).toEqual(json.resumeVariants);
  });

  it("provides options and metadata for all outcome and event types", () => {
    const outcomeOptionValues = APPLICATION_OUTCOME_OPTIONS.map((o) => o.value);
    for (const val of APPLICATION_OUTCOME_VALUES) {
      expect(outcomeOptionValues).toContain(val);
      expect(APPLICATION_OUTCOME_METADATA[val]).toBeDefined();
    }

    for (const val of APPLICATION_EVENT_TYPE_VALUES) {
      expect(APPLICATION_EVENT_TYPE_METADATA[val]).toBeDefined();
      expect(typeof APPLICATION_EVENT_TYPE_METADATA[val].resultingStatusDescription).toBe("string");
    }

    const appendableValues = APPENDABLE_EVENT_OPTIONS.map((o) => o.value);
    for (const val of APPLICATION_EVENT_TYPE_VALUES) {
      expect(appendableValues).toContain(val);
    }

    const variantOptionValues = RESUME_VARIANT_OPTIONS.map((o) => o.value);
    for (const val of RESUME_VARIANT_VALUES) {
      expect(variantOptionValues).toContain(val);
    }
  });

  it("isApplicationOutcome acts as a safe runtime type guard", () => {
    expect(isApplicationOutcome("applied")).toBe(true);
    expect(isApplicationOutcome("rejected")).toBe(true);
    expect(isApplicationOutcome("closed")).toBe(true);

    expect(isApplicationOutcome("invalid")).toBe(false);
    expect(isApplicationOutcome("")).toBe(false);
    expect(isApplicationOutcome(null)).toBe(false);
    expect(isApplicationOutcome(undefined)).toBe(false);
    expect(isApplicationOutcome(123)).toBe(false);
  });

  it("isApplicationEventType acts as a safe runtime type guard", () => {
    expect(isApplicationEventType("applied")).toBe(true);
    expect(isApplicationEventType("follow_up")).toBe(true);
    expect(isApplicationEventType("interview")).toBe(true);
    expect(isApplicationEventType("offer")).toBe(true);
    expect(isApplicationEventType("offer_accepted")).toBe(true);
    expect(isApplicationEventType("offer_declined")).toBe(true);
    expect(isApplicationEventType("rejected")).toBe(true);
    expect(isApplicationEventType("withdrawn")).toBe(true);

    expect(isApplicationEventType("invalid")).toBe(false);
    expect(isApplicationEventType(null)).toBe(false);
  });

  it("isResumeVariant acts as a safe runtime type guard", () => {
    expect(isResumeVariant("tailored")).toBe(true);
    expect(isResumeVariant("as_is")).toBe(true);
    expect(isResumeVariant("ic_framed")).toBe(true);

    expect(isResumeVariant("unknown")).toBe(false);
    expect(isResumeVariant(null)).toBe(false);
  });
});
