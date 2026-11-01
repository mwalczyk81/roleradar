import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  DISPOSITION_VALUES,
  RESURFACE_POLICY_VALUES,
  TRANSITIONS,
  getValidTransitionTargets,
  canCreateApplication,
  DISPOSITION_METADATA,
} from "../../app/lib/dispositions.js";

describe("dispositions.ts contract drift guard", () => {
  it("matches contracts/disposition-matrix.json exactly", () => {
    const matrixPath = path.resolve(process.cwd(), "../contracts/disposition-matrix.json");
    const altPath = path.resolve(process.cwd(), "contracts/disposition-matrix.json");
    const targetPath = fs.existsSync(matrixPath) ? matrixPath : altPath;

    expect(fs.existsSync(targetPath)).toBe(true);

    const json = JSON.parse(fs.readFileSync(targetPath, "utf8"));

    // 1. Dispositions match
    expect([...DISPOSITION_VALUES]).toEqual(json.dispositions);

    // 2. Resurface policies match
    expect([...RESURFACE_POLICY_VALUES]).toEqual(json.resurfacePolicies);

    // 3. Transitions match
    expect(TRANSITIONS).toEqual(json.transitions);

    // 4. Invariant: Applied is NEVER a target disposition
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
    for (const disp of DISPOSITION_VALUES) {
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
});
