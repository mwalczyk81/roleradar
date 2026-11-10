import fs from "node:fs";
import crypto from "node:crypto";
import { test, expect } from "@playwright/test";
import { nextUnusedTotpCode } from "./support/totp";

// Feature 014 US2 (SC-006, ADR 0044), replacing the 013 X-Correlation-ID check: one W3C trace id follows a request across
// services. This spec only runs inside the isolated real stack (RealStackEndToEndTests, scripts/test-e2e-real.ps1). It
// supplies the trace ids itself, as a traceparent header on the browser's requests, because the web returns no trace
// header. The web honours an inbound traceparent and forwards it to the API (and, for the sign-in callback, to Identity),
// so the harness can then find each id:
//   line 1: the trace id of a failing page request (an unknown posting). The web calls the API, which answers 404 with a
//           problem document carrying the same traceId; the harness finds the id in the web and API container logs and in
//           the audit entry of a write made with it.
//   line 2: the trace id of the sign-in, whose callback the web completes with server-side calls to Identity; the harness
//           finds it in the web and Identity container logs.
// Both are written to E2E_OPERABILITY_ID_FILE, one per line.

const HEX32 = /^[0-9a-f]{32}$/;

function randomV7LikeId(): string {
  return crypto.randomUUID().replace(/^(.{14})./, "$17");
}

function newTraceparent(): { traceId: string; header: string } {
  const traceId = crypto.randomBytes(16).toString("hex");
  const spanId = crypto.randomBytes(8).toString("hex");
  return { traceId, header: `00-${traceId}-${spanId}-01` };
}

test.describe("Operability: one trace id across services (014)", () => {
  const baseUrl = process.env.E2E_BASE_URL;
  const username = process.env.E2E_USERNAME;
  const password = process.env.E2E_PASSWORD;
  const totpKey = process.env.E2E_TOTP_KEY;

  test("a failing posting request and the sign-in each carry a trace id the harness can follow", async ({ page, context }) => {
    test.setTimeout(60_000);
    test.skip(!baseUrl, "Skipped: E2E_BASE_URL not configured (real-stack runner required)");
    if (!baseUrl || !username || !password || !totpKey) {
      throw new Error("Missing E2E_BASE_URL, E2E_USERNAME, E2E_PASSWORD or E2E_TOTP_KEY.");
    }

    // Sign in under one trace: the browser sends it to the web and to Identity, and the web's callback forwards it.
    const signIn = newTraceparent();
    await context.setExtraHTTPHeaders({ traceparent: signIn.header });
    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });
    await page.fill('input[name="Input.Username"]', username);
    await page.fill('input[name="Input.Password"]', password);
    await page.click('button[type="submit"]:has-text("Sign In")');
    await expect(page.getByText("6-Digit Authenticator Code")).toBeVisible({ timeout: 10_000 });
    await page.fill('input[name="TwoFactorInput.Code"]', await nextUnusedTotpCode(totpKey));
    await page.click('button[type="submit"]:has-text("Verify Code")');
    await page.waitForURL("**/postings");

    // A failing page request under another trace: the API answers 404 with a problem document carrying this traceId.
    const failing = newTraceparent();
    await context.setExtraHTTPHeaders({ traceparent: failing.header });
    const response = await page.goto(`${baseUrl}/postings/${randomV7LikeId()}`);
    expect(response).not.toBeNull();

    // The web returns no trace header and no X-Correlation-ID: the id is the one this spec supplied.
    expect(response?.headers()["x-correlation-id"]).toBeUndefined();
    expect(response?.headers()["traceparent"]).toBeUndefined();
    expect(failing.traceId).toMatch(HEX32);
    expect(signIn.traceId).toMatch(HEX32);
    expect(failing.traceId).not.toBe(signIn.traceId);

    const idFile = process.env.E2E_OPERABILITY_ID_FILE;
    if (idFile) {
      fs.writeFileSync(idFile, `${failing.traceId}\n${signIn.traceId}\n`, "utf8");
    }
  });
});
