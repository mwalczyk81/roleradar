import fs from "node:fs";
import crypto from "node:crypto";
import { test, expect } from "@playwright/test";
import { totpCode } from "./support/totp";

// Feature 013 US1 (SC-001): a failing request through the web returns X-Correlation-ID, and the real-stack harness
// (RealStackEndToEndTests) then finds that id on JSON log lines from both the web and the API containers. This spec
// only runs inside the isolated real stack; it writes the id to E2E_OPERABILITY_ID_FILE for the harness to read.

const ACCEPTED = /^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$/;

function randomV7LikeId(): string {
  return crypto.randomUUID().replace(/^(.{14})./, "$17");
}

test.describe("Operability: one correlation id across services (013)", () => {
  const baseUrl = process.env.E2E_BASE_URL;
  const username = process.env.E2E_USERNAME;
  const password = process.env.E2E_PASSWORD;
  const totpKey = process.env.E2E_TOTP_KEY;

  test("a failing posting request returns a correlation id the harness can follow", async ({ page }) => {
    test.setTimeout(60_000);
    test.skip(!baseUrl, "Skipped: E2E_BASE_URL not configured (real-stack runner required)");
    if (!baseUrl || !username || !password || !totpKey) {
      throw new Error("Missing E2E_BASE_URL, E2E_USERNAME, E2E_PASSWORD or E2E_TOTP_KEY.");
    }

    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });
    await page.fill('input[name="Input.Username"]', username);
    await page.fill('input[name="Input.Password"]', password);
    await page.click('button[type="submit"]:has-text("Sign In")');
    await expect(page.getByText("6-Digit Authenticator Code")).toBeVisible({ timeout: 10_000 });
    await page.fill('input[name="TwoFactorInput.Code"]', totpCode(totpKey));
    await page.click('button[type="submit"]:has-text("Verify Code")');
    await page.waitForURL("**/postings");

    const response = await page.goto(`${baseUrl}/postings/${randomV7LikeId()}`);
    expect(response).not.toBeNull();
    const correlationId = response?.headers()["x-correlation-id"] ?? "";
    expect(correlationId).toMatch(ACCEPTED);

    const idFile = process.env.E2E_OPERABILITY_ID_FILE;
    if (idFile) {
      fs.writeFileSync(idFile, correlationId, "utf8");
    }
  });
});
