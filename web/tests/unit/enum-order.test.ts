import { describe, it, expect } from "vitest";
import { hasUniqueMembers } from "../../app/lib/enum-order.js";

describe("hasUniqueMembers", () => {
  it("is true for distinct members and for an empty list", () => {
    expect(hasUniqueMembers(["a", "b", "c"])).toBe(true);
    expect(hasUniqueMembers([])).toBe(true);
  });

  it("is false when a member repeats", () => {
    expect(hasUniqueMembers(["a", "b", "a"])).toBe(false);
    expect(hasUniqueMembers(["x", "x"])).toBe(false);
  });
});
