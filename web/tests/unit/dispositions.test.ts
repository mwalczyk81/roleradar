import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  DISPOSITION_ORDER,
  RESURFACE_POLICY_ORDER,
  TRANSITIONS,
  getValidTransitionTargets,
  canCreateApplication,
  DISPOSITION_METADATA,
  RESURFACE_POLICY_LABELS,
  isPostingDisposition,
  isResurfacePolicy,
} from "../../app/lib/dispositions.js";
import { hasUniqueMembers } from "../../app/lib/enum-order.js";

describe("dispositions.ts (values come from the generated API types, ADR 0035)", () => {
  it("lists every disposition and resurface policy once in the UI order lists", () => {
    expect(hasUniqueMembers(DISPOSITION_ORDER)).toBe(true);
    expect(hasUniqueMembers(RESURFACE_POLICY_ORDER)).toBe(true);
  });

  it("matches the transition table in contracts/disposition-matrix.json exactly", () => {
    const matrixPath = path.resolve(process.cwd(), "../contracts/disposition-matrix.json");
    const altPath = path.resolve(process.cwd(), "contracts/disposition-matrix.json");
    const targetPath = fs.existsSync(matrixPath) ? matrixPath : altPath;

    expect(fs.existsSync(targetPath)).toBe(true);

    const json = JSON.parse(fs.readFileSync(targetPath, "utf8"));

    // Transitions match (the transition table is behaviour, not enum membership)
    expect(TRANSITIONS).toEqual(json.transitions);

    // Invariant: Applied is NEVER a target disposition
    for (const t of TRANSITIONS) {
      expect(t.to).not.toBe("applied");
    }
  });

  it("filters valid transition targets based on current disposition and resurface policy", () => {
    // From lead: scored, screened_out, closed, unscored (no policy required)
    const leadTargets = getValidTransitionTargets("lead", "never").map((t) => t.to);
    expect(leadTargets).toEqual(["scored", "screened_out", "closed", "unscored"]);

    // From screened_out with 'never': no transitions allowed
    const screenedOutNever = getValidTransitionTargets("screened_out", "never");
    expect(screenedOutNever).toEqual([]);

    // From screened_out with 'if_reopened': transition to 'lead' is allowed
    const screenedOutReopened = getValidTransitionTargets("screened_out", "if_reopened");
    expect(screenedOutReopened).toHaveLength(1);
    expect(screenedOutReopened[0].to).toBe("lead");

    // From closed with 'never': no transitions allowed
    const closedNever = getValidTransitionTargets("closed", "never");
    expect(closedNever).toEqual([]);

    // From closed with 'if_reopened': transition to 'lead' is allowed
    const closedReopened = getValidTransitionTargets("closed", "if_reopened");
    expect(closedReopened).toHaveLength(1);
    expect(closedReopened[0].to).toBe("lead");

    // From rejected: transition to 'lead' is allowed with required reason
    const rejectedTargets = getValidTransitionTargets("rejected", "never");
    expect(rejectedTargets).toHaveLength(1);
    expect(rejectedTargets[0].to).toBe("lead");
    expect(rejectedTargets[0].requiresReason).toBe(true);
  });

  it("defines distinctive badge tones for each disposition", () => {
    for (const disp of DISPOSITION_ORDER) {
      const meta = DISPOSITION_METADATA[disp];
      expect(meta).toBeDefined();
      expect(meta.label).toBeTruthy();
      expect(["neutral", "primary", "success", "warning", "danger"]).toContain(meta.tone);
    }

    // Verify distinct tones
    expect(DISPOSITION_METADATA.lead.tone).toBe("primary");
    expect(DISPOSITION_METADATA.scored.tone).toBe("warning");
    expect(DISPOSITION_METADATA.applied.tone).toBe("success");
    expect(DISPOSITION_METADATA.rejected.tone).toBe("danger");
    expect(DISPOSITION_METADATA.screened_out.tone).toBe("neutral");
    expect(DISPOSITION_METADATA.closed.tone).toBe("neutral");
    expect(DISPOSITION_METADATA.unscored.tone).toBe("neutral");
  });

  it("evaluates canCreateApplication correctly based on API semantic invariants", () => {
    // Applied: cannot apply again
    expect(canCreateApplication("applied")).toEqual({ allowed: false, requiresOverride: false });

    // Rejected: cannot apply without explicit reopen first
    expect(canCreateApplication("rejected")).toEqual({ allowed: false, requiresOverride: false });

    // Decided dispositions (screened_out, closed): allowed ONLY with override
    expect(canCreateApplication("screened_out")).toEqual({ allowed: true, requiresOverride: true });
    expect(canCreateApplication("closed")).toEqual({ allowed: true, requiresOverride: true });

    // Active dispositions (lead, scored, unscored): allowed without override
    expect(canCreateApplication("lead")).toEqual({ allowed: true, requiresOverride: false });
    expect(canCreateApplication("scored")).toEqual({ allowed: true, requiresOverride: false });
    expect(canCreateApplication("unscored")).toEqual({ allowed: true, requiresOverride: false });
  });

  it("isPostingDisposition accepts exactly the known dispositions and nothing inherited or untyped", () => {
    for (const value of DISPOSITION_ORDER) {
      expect(isPostingDisposition(value)).toBe(true);
    }
    for (const value of ["", "Lead", "unknown", "constructor", "__proto__", "toString", "hasOwnProperty"]) {
      expect(isPostingDisposition(value), value).toBe(false);
    }
    for (const value of [null, undefined, 0, 1, true, {}, [], ["lead"]]) {
      expect(isPostingDisposition(value)).toBe(false);
    }
  });

  it("isResurfacePolicy accepts exactly the known policies and nothing inherited or untyped", () => {
    for (const value of RESURFACE_POLICY_ORDER) {
      expect(isResurfacePolicy(value)).toBe(true);
    }
    for (const value of ["", "Never", "always", "constructor", "__proto__", "toString"]) {
      expect(isResurfacePolicy(value), value).toBe(false);
    }
    for (const value of [null, undefined, 0, true, {}, ["never"]]) {
      expect(isResurfacePolicy(value)).toBe(false);
    }
  });

  it("keeps each disposition's own value, the resurface policy order and its labels", () => {
    for (const key of DISPOSITION_ORDER) {
      expect(DISPOSITION_METADATA[key].value).toBe(key);
    }
    expect([...RESURFACE_POLICY_ORDER]).toEqual(["never", "if_reopened"]);
    expect(RESURFACE_POLICY_LABELS).toEqual({ never: "Never", if_reopened: "If Reopened" });
  });
});
