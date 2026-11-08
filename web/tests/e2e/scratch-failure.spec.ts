import { test, expect } from "@playwright/test";

// SCRATCH, never merged (feature 011, T034 and T047). A fictional-lane test that fails on purpose, to prove that a failing
// e2e test uploads a screenshot and a trace and shows both in the combined Allure report. It needs no server: the page
// is built in the browser from fictional markup. It fails on every attempt, so the retry runs and records a trace.
test("scratch: a deliberately failing fictional-lane test", async ({ page }) => {
  await page.setContent("<main><h1>Fictional Example Role</h1><p>Acme Example Co.</p></main>");
  await expect(page.getByRole("heading", { name: "Fictional Example Role" })).toBeVisible();
  await expect(page.getByText("This text does not exist on the page")).toBeVisible({ timeout: 1_000 });
});
