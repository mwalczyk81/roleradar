import { test, expect } from "@playwright/test";
import crypto from "node:crypto";

function base32Decode(base32: string): Buffer {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const cleaned = base32.toUpperCase().replace(/=+$/, "");
  let bits = 0;
  let value = 0;
  const bytes: number[] = [];

  for (let i = 0; i < cleaned.length; i++) {
    const idx = alphabet.indexOf(cleaned[i]);
    if (idx === -1) continue;
    value = (value << 5) | idx;
    bits += 5;
    if (bits >= 8) {
      bytes.push((value >>> (bits - 8)) & 255);
      bits -= 8;
    }
  }
  return Buffer.from(bytes);
}

function computeTotp(base32Secret: string): string {
  const key = base32Decode(base32Secret);
  const epoch = Math.floor(Date.now() / 1000);
  const timeStep = Math.floor(epoch / 30);
  const buffer = Buffer.alloc(8);
  buffer.writeBigInt64BE(BigInt(timeStep));
  const hmac = crypto.createHmac("sha1", key);
  hmac.update(buffer);
  const digest = hmac.digest();
  const offset = digest[digest.length - 1] & 0x0f;
  const code = ((digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).toString().padStart(6, "0");
  return code;
}

test.describe("Real-Stack End-to-End User Journey (T062)", () => {
  const baseUrl = process.env.E2E_BASE_URL;
  const username = process.env.E2E_USERNAME;
  const password = process.env.E2E_PASSWORD;
  const totpKey = process.env.E2E_TOTP_KEY;

  test("signs in with 2FA, manages postings, applications, companies, signs out, and re-challenges", async ({ page, context }) => {
    test.setTimeout(60_000);

    test.skip(!process.env.E2E_BASE_URL, "Skipped: E2E_BASE_URL not configured (real-stack runner required)");

    if (!baseUrl || !username || !password || !totpKey) {
      throw new Error(
        "Missing required E2E environment variables: E2E_BASE_URL, E2E_USERNAME, E2E_PASSWORD, E2E_TOTP_KEY must all be provided."
      );
    }

    // 1. Visit /postings without session -> redirect to Identity login
    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });
    await expect(page.locator("h1")).toContainText("RoleRadar Authentication", { timeout: 15000 });

    // 2. Submit primary credentials (username + password)
    await page.fill('input[name="Input.Username"]', username);
    await page.fill('input[name="Input.Password"]', password);
    await page.click('button[type="submit"]:has-text("Sign In")');

    // 3. Challenge with TOTP 2FA
    await expect(page.getByText("6-Digit Authenticator Code")).toBeVisible({ timeout: 10000 });
    const totpCode = computeTotp(totpKey);
    await page.fill('input[name="TwoFactorInput.Code"]', totpCode);
    await page.click('button[type="submit"]:has-text("Verify Code")');

    // 4. Land on authenticated /postings
    await page.waitForURL("**/postings");
    await expect(page.locator("h1")).toContainText("Job Postings");
    await expect(page.getByText("Senior Cloud Architect")).toBeVisible();
    await expect(page.getByText("Staff Platform Engineer")).toBeVisible();

    // 5. Search and filter postings
    const searchInput = page.getByPlaceholder("Search postings...");
    await searchInput.fill("Senior Cloud");
    await expect(page.getByText("Senior Cloud Architect")).toBeVisible();
    await expect(page.getByText("Staff Platform Engineer")).not.toBeVisible();
    await searchInput.fill("");
    await expect(page.getByText("Staff Platform Engineer")).toBeVisible();

    // 6. Open posting 1 and make a disposition transition
    await page.getByText("Senior Cloud Architect").first().click();
    await page.waitForURL("**/postings/*");
    await expect(page.locator("h1")).toContainText("Senior Cloud Architect");

    await page.getByRole("button", { name: "Change Disposition" }).click();
    await page.selectOption('select[name="toDisposition"]', "scored");
    await page.fill('input[name="reason"]', "Real-stack transition verification to scored");
    await page.getByRole("button", { name: "Apply Transition" }).click();

    // Verify disposition update reflected in UI
    await expect(page.getByTestId("disposition-badge")).toContainText("Scored", { ignoreCase: true });

    // 7. Navigate back to /postings, open posting 2, and create an application
    await page.goto(`${baseUrl}/postings`);
    await page.getByText("Staff Platform Engineer").first().click();
    await page.waitForURL("**/postings/*");
    await expect(page.locator("h1")).toContainText("Staff Platform Engineer");

    await page.getByRole("button", { name: "Create Application" }).click();
    await page.fill('input[name="desiredSalaryMin"]', "180000");
    await page.fill('input[name="desiredSalaryMax"]', "220000");
    await page.getByRole("button", { name: "Record Application" }).click();

    // Verify application created and disposition updated to applied
    await expect(page.getByTestId("disposition-badge")).toContainText("Applied", { ignoreCase: true });

    // Verify application is present in applications list
    await page.goto(`${baseUrl}/applications`);
    await expect(page.locator("h1")).toContainText("Applications");
    await expect(page.getByText("Staff Platform Engineer")).toBeVisible();

    // 8. Navigate to Companies, open Acme Cloud Innovations, and edit
    await page.goto(`${baseUrl}/companies`);
    await expect(page.locator("h1")).toContainText("Companies");
    await page.getByText("Acme Cloud Innovations").first().click();
    await page.waitForURL("**/companies/*");

    const descInput = page.locator('textarea[name="description"]');
    await descInput.fill("Enterprise cloud infrastructure observability leader (updated in real stack)");
    await page.getByRole("button", { name: "Save Changes" }).click();
    await expect(page.getByText("Changes Saved")).toBeVisible({ timeout: 10000 });

    // 9. Sign out
    await page.getByRole("button", { name: "Sign Out" }).click();

    // Observable condition: wait for /auth/logout and the "Signed Out" heading
    await page.waitForURL("**/auth/logout", { timeout: 15000 });
    await expect(page.getByRole("heading", { name: "Signed Out" })).toBeVisible({ timeout: 15000 });

    // Assert web session cookie is gone from cookie jar
    const cookies = await context.cookies();
    const sessionCookie = cookies.find(
      (c) => c.name === "__Host-roleradar_session" || c.name === "roleradar_session"
    );
    expect(sessionCookie).toBeUndefined();

    // 10. Re-challenge invariant: visiting /postings requires login again without clearCookies
    // Leaving the cookie jar intact proves Identity ended the SSO session rather than relying on context clearing
    await page.goto(`${baseUrl}/postings`, { waitUntil: "networkidle" });
    await expect(page.locator("h1")).toContainText("RoleRadar Authentication");
    await expect(page.locator('input[name="Input.Username"]')).toBeVisible();
    await expect(page.locator('input[name="Input.Password"]')).toBeVisible();
    await expect(page.locator('input[name="TwoFactorInput.Code"]')).not.toBeVisible();
    await expect(page.locator(".ds-appshell-title")).not.toBeVisible();
  });
});
