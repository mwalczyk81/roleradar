import { test, expect } from "@playwright/test";
import { randomBytes } from "node:crypto";

// SCRATCH, for a throwaway draft pull request only (never merge). Fictional data, no app needed.
//  1. "scratch: fails every attempt"  -> the job goes red; trace of both attempts, screenshot, published trace expected.
//  2. "scratch: flaky"               -> fails the first attempt, passes the retry; the job stays green for this test.
//  3. "scratch: oversize trace"      -> fails every attempt with a trace well over 3 MiB; expect a dropped-for-size note.

test("scratch: fails every attempt", async ({ page }) => {
  await page.setContent("<h1>Scratch heading</h1><button>Go</button>");
  await page.getByRole("button", { name: "Go" }).click();
  await expect(page.locator("h1")).toHaveText("A heading that is not there", { timeout: 1_000 });
});

test("scratch: flaky (fails once, then passes)", async ({ page }, testInfo) => {
  await page.setContent("<h1>Flaky heading</h1>");
  await expect(page.locator("h1")).toHaveText(testInfo.retry === 0 ? "wrong on the first attempt" : "Flaky heading", { timeout: 1_000 });
});

test("scratch: oversize trace", async ({ page }) => {
  // Random text does not compress, so the DOM snapshots in the trace stay large.
  const filler = randomBytes(6 * 1024 * 1024).toString("base64");
  await page.setContent(`<h1>Big</h1><pre>${filler}</pre>`);
  await page.evaluate((text) => {
    document.body.append(Object.assign(document.createElement("pre"), { textContent: text }));
  }, filler);
  await expect(page.locator("h1")).toHaveText("Not the big heading", { timeout: 1_000 });
});
