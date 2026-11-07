import { describe, it, expect } from "vitest";
import {
  APPLICATION_OUTCOME_ORDER,
  APPLICATION_EVENT_TYPE_ORDER,
  RESUME_VARIANT_ORDER,
  APPLICATION_OUTCOME_OPTIONS,
  RESUME_VARIANT_OPTIONS,
  APPENDABLE_EVENT_OPTIONS,
  APPLICATION_OUTCOME_METADATA,
  APPLICATION_EVENT_TYPE_METADATA,
  isApplicationOutcome,
  isApplicationEventType,
  isResumeVariant,
} from "../../app/lib/application-enums.js";
import { hasUniqueMembers } from "../../app/lib/enum-order.js";

describe("application-enums.ts (values come from the generated API types, ADR 0035)", () => {
  it("lists every value once in the UI order lists", () => {
    expect(hasUniqueMembers(APPLICATION_OUTCOME_ORDER)).toBe(true);
    expect(hasUniqueMembers(APPLICATION_EVENT_TYPE_ORDER)).toBe(true);
    expect(hasUniqueMembers(RESUME_VARIANT_ORDER)).toBe(true);
  });

  it("lists the nine event types with note last (feature 008)", () => {
    const expected = [
      "applied",
      "follow_up",
      "interview",
      "offer",
      "offer_accepted",
      "offer_declined",
      "rejected",
      "withdrawn",
      "note",
    ];
    expect([...APPLICATION_EVENT_TYPE_ORDER]).toEqual(expected);
    expect(isApplicationEventType("note")).toBe(true);
  });

  it("labels note as a non-terminal 'Note' and offers it when appending an event", () => {
    const meta = (APPLICATION_EVENT_TYPE_METADATA as Record<string, { label: string; terminal: boolean } | undefined>)["note"];
    expect(meta?.label).toBe("Note");
    expect(meta?.terminal).toBe(false);

    const option = APPENDABLE_EVENT_OPTIONS.find((o) => (o.value as string) === "note");
    expect(option?.label).toBe("Note");
  });

  it("provides options and metadata for all outcome and event types", () => {
    const outcomeOptionValues = APPLICATION_OUTCOME_OPTIONS.map((o) => o.value);
    for (const val of APPLICATION_OUTCOME_ORDER) {
      expect(outcomeOptionValues).toContain(val);
      expect(APPLICATION_OUTCOME_METADATA[val]).toBeDefined();
    }

    for (const val of APPLICATION_EVENT_TYPE_ORDER) {
      expect(APPLICATION_EVENT_TYPE_METADATA[val]).toBeDefined();
      expect(typeof APPLICATION_EVENT_TYPE_METADATA[val].resultingStatusDescription).toBe("string");
    }

    const appendableValues = APPENDABLE_EVENT_OPTIONS.map((o) => o.value);
    for (const val of APPLICATION_EVENT_TYPE_ORDER) {
      expect(appendableValues).toContain(val);
    }

    const variantOptionValues = RESUME_VARIANT_OPTIONS.map((o) => o.value);
    for (const val of RESUME_VARIANT_ORDER) {
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
