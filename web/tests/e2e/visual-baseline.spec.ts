import fs from "node:fs";
import path from "node:path";
import type { Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startAppHarness, defaultStubState, type AppHarness, type StubState } from "./support/app-harness.js";
import { startInsightsHarness, loadNoteFixture, loadSnapshot, type InsightsHarness } from "./support/insights-stub.js";
import { assertNotRealStackProject } from "./support/isolation.js";
import { styleAttributeElements } from "./support/style-census.js";
import { settleForCapture } from "./support/capture-settle.js";

/**
 * 009 phase 1: the before/after screenshot harness and the style-attribute census.
 *
 * Skipped unless ROLERADAR_VISUAL_DIR is set. When it is set, every screenshot is written to (or compared with) a
 * folder under that directory (see snapshotPathTemplate in playwright.config.ts), which must be outside the
 * repository. Nothing here is ever committed. Data is the same fictional data the other e2e specs use, served by a
 * loopback stub API; nothing talks to a real API, Identity, database or compose project.
 *
 *   baseline : ROLERADAR_VISUAL_DIR=<dir> pnpm exec playwright test tests/e2e/visual-baseline.spec.ts --update-snapshots
 *   compare  : ROLERADAR_VISUAL_DIR=<dir> pnpm exec playwright test tests/e2e/visual-baseline.spec.ts
 *
 * The census writes <dir>/census.json: for every route, the elements in the server-rendered HTML (no JavaScript
 * runs, so it is what the browser receives before hydration) that carry a `style` attribute, as tag.class.
 */

const VISUAL_DIR = process.env.ROLERADAR_VISUAL_DIR;
const THEMES = ["light", "dark"] as const;
type Theme = (typeof THEMES)[number];

const APP_SESSION_ID = "e2e-visual-app-session";
const INSIGHTS_SESSION_ID = "e2e-visual-insights-session";
const FIXED_NOW = new Date("2026-11-05T12:00:00Z");

if (VISUAL_DIR) {
  const resolved = path.resolve(VISUAL_DIR);
  const repoRoot = path.resolve(process.cwd(), "..");
  if (resolved === repoRoot || resolved.startsWith(repoRoot + path.sep)) {
    throw new Error("ROLERADAR_VISUAL_DIR must be outside the repository so no screenshot can be committed.");
  }
}

interface CensusEntry {
  count: number;
  elements: string[];
}
const census: Record<string, CensusEntry> = {};

async function capture(page: Page, name: string, theme: Theme, keepScroll = false): Promise<void> {
  // Waits on real conditions (scroll offset 0, geometry unchanged for several frames), see support/capture-settle.ts.
  // A hover capture keeps the scroll position the hover left, so the pointer stays on its target.
  await settleForCapture(page, { keepScroll });
  await expect(page).toHaveScreenshot(`${name}-${theme}.png`, { fullPage: true, animations: "disabled", caret: "hide" });
}

test.describe.configure({ mode: "serial" });

test.describe("Visual baseline: app routes (fictional loopback stub)", () => {
  test.skip(!VISUAL_DIR, "Set ROLERADAR_VISUAL_DIR (outside the repository) to run the visual baseline.");
  test.use({ viewport: { width: 1280, height: 800 } });

  let harness: AppHarness;
  const state: StubState = defaultStubState();

  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    harness = await startAppHarness(state, APP_SESSION_ID);
  });

  test.afterAll(async () => {
    await harness?.stop();
  });

  interface RouteCase {
    name: string;
    path: string;
    /** Sets the stub state for this route before it loads. */
    prepare?: (s: StubState) => void;
    /** Extra steps after load, before the screenshot (opening a dialog). */
    after?: (page: Page) => Promise<void>;
    /** Whether the route is part of the census (server-rendered HTML only; dialogs are not). */
    census?: boolean;
    /** A pointer-dependent capture (hover): do not scroll to the top before capturing. */
    hover?: boolean;
  }

  const routes: RouteCase[] = [
    { name: "postings-list", path: "/postings", census: true },
    { name: "posting-lead", path: "/postings/post-e2e-1", census: true },
    {
      name: "posting-applied",
      path: "/postings/post-e2e-1",
      prepare: (s) => {
        s.currentPostingDisposition = "applied";
      },
      census: true,
    },
    {
      name: "posting-transition-dialog",
      path: "/postings/post-e2e-1",
      prepare: (s) => {
        s.currentPostingDisposition = "lead";
      },
      after: async (page) => {
        await page.getByRole("button", { name: "Change Disposition" }).click();
        await page.getByRole("dialog").waitFor();
      },
    },
    {
      name: "posting-create-application-dialog",
      path: "/postings/post-e2e-1",
      prepare: (s) => {
        s.currentPostingDisposition = "lead";
      },
      after: async (page) => {
        await page.getByRole("button", { name: "Create Application" }).click();
        await page.getByRole("dialog").waitFor();
      },
    },
    {
      // 009 phase 4 (J7): the role-title link under the pointer and under keyboard focus.
      name: "postings-role-link-hover",
      path: "/postings",
      hover: true,
      after: async (page) => {
        await page.locator('table a[href^="/postings/"]').first().hover();
      },
    },
    {
      name: "postings-role-link-focus",
      path: "/postings",
      after: async (page) => {
        await page.locator('table a[href^="/postings/"]').first().focus();
      },
    },
    {
      name: "applications-title-link-hover",
      path: "/applications",
      hover: true,
      after: async (page) => {
        await page.locator('table a[href^="/applications/"]').first().hover();
      },
    },
    { name: "companies-list", path: "/companies", census: true },
    { name: "company-detail", path: "/companies/comp-e2e-1", census: true },
    {
      // The edit form with typed values: a renamed company, a new tag chip, a half-filled career source, another status.
      name: "company-detail-edit",
      path: "/companies/comp-e2e-1",
      after: async (page) => {
        await page.locator("#company-name-input").fill("Acme Cloud Services (edited)");
        await page.locator("#company-status-select").selectOption({ index: 1 });
        await page.locator("#category-tag-input").fill("fictional-tag");
        await page.locator("#category-tag-input").press("Enter");
        await page.locator("#new-source-url").fill("https://careers.example.com/fictional");
        await page.locator("#new-ats-provider").fill("greenhouse");
      },
    },
    {
      // No career sources registered: the empty caption instead of the source list.
      name: "company-detail-no-sources",
      path: "/companies/comp-e2e-1",
      prepare: (s) => {
        s.currentCompanyCareerSources = [];
      },
    },
    {
      // A rejected save: the error banner and the inline field errors.
      name: "company-detail-errors",
      path: "/companies/comp-e2e-1",
      prepare: (s) => {
        s.nextCompanyResponseStatus = 400;
        s.nextCompanyResponseBody = {
          errorCode: "VALIDATION_ERROR",
          message: "Validation failed.",
          details: [
            { field: "name", issue: "Company name cannot be blank." },
            { field: "status", issue: "Status is not recognised." },
            { field: "ownershipType", issue: "Ownership type is not recognised." },
          ],
        };
      },
      after: async (page) => {
        await page.getByRole("button", { name: "Save Changes" }).click();
        await page.getByText("Company name cannot be blank.").waitFor();
      },
    },
    { name: "applications-list", path: "/applications", census: true },
    { name: "application-detail", path: "/applications/app-e2e-1", census: true },
    {
      name: "application-append-event-dialog",
      path: "/applications/app-e2e-1",
      after: async (page) => {
        await page.getByRole("button", { name: "Append Event" }).click();
        await page.getByRole("dialog").waitFor();
      },
    },
    {
      // A timeline with a posting_transition event (terminal, with the 008 source line), a note event and an event
      // with no notes and no source.
      name: "application-detail-timeline",
      path: "/applications/app-e2e-1",
      prepare: (s) => {
        s.currentApplicationStatus = "rejected";
        s.currentApplicationEvents = [
          { eventType: "applied", eventDate: "2026-10-31", notes: "Initial application submitted", eventSource: "web" },
          { eventType: "follow_up", eventDate: "2026-11-01", notes: null },
          { eventType: "note", eventDate: "2026-11-02", notes: "Recruiter called.\nSecond line of a fictional note." },
          { eventType: "rejected", eventDate: "2026-11-03", notes: "Rejected by email", eventSource: "posting_transition" },
        ];
      },
      census: true,
    },
    {
      // The append-event dialog with a field error: the server rejects the date and the inline error shows.
      name: "application-append-event-dialog-error",
      path: "/applications/app-e2e-1",
      prepare: (s) => {
        s.nextApplicationResponseStatus = 400;
        s.nextApplicationResponseBody = {
          errorCode: "VALIDATION_ERROR",
          message: "Validation failed.",
          details: [
            { field: "eventDate", message: "Event date cannot be in the future." },
            { field: "notes", message: "Notes are too long." },
          ],
        };
      },
      after: async (page) => {
        await page.getByRole("button", { name: "Append Event" }).click();
        const dialog = page.getByRole("dialog");
        await dialog.waitFor();
        await dialog.getByRole("button", { name: "Append Event" }).click();
        await dialog.getByText("Event date cannot be in the future.").waitFor();
      },
    },
    {
      // A rejected event type shows the posting sync note under the select.
      name: "application-append-event-dialog-sync-note",
      path: "/applications/app-e2e-1",
      after: async (page) => {
        await page.getByRole("button", { name: "Append Event" }).click();
        const dialog = page.getByRole("dialog");
        await dialog.waitFor();
        await dialog.getByLabel("Event Type *").selectOption("rejected");
        await dialog.getByTestId("posting-sync-note").waitFor();
      },
    },
    {
      // The create dialog on a closed posting: the override alert and the override panel.
      name: "posting-create-application-dialog-override",
      path: "/postings/post-e2e-1",
      prepare: (s) => {
        s.currentPostingDisposition = "closed";
      },
      after: async (page) => {
        await page.getByRole("button", { name: "Create Application" }).click();
        await page.getByRole("dialog").waitFor();
      },
    },
    {
      // The create dialog after a rejected submit: the override panel with its error, inline field errors and the banner.
      name: "posting-create-application-dialog-errors",
      path: "/postings/post-e2e-1",
      prepare: (s) => {
        s.currentPostingDisposition = "closed";
        s.nextApplicationResponseStatus = 400;
        s.nextApplicationResponseBody = {
          errorCode: "VALIDATION_ERROR",
          message: "Validation failed.",
          details: [
            { field: "desiredSalaryMin", message: "Minimum salary must be positive." },
            { field: "confirmationCode", message: "Confirmation code is too long." },
          ],
        };
      },
      after: async (page) => {
        await page.getByRole("button", { name: "Create Application" }).click();
        const dialog = page.getByRole("dialog");
        await dialog.waitFor();
        await dialog.getByLabel("Override Reason *").fill("Fictional reason to reopen this posting.");
        await dialog.getByLabel("Desired Min").fill("-5");
        await dialog.getByRole("button", { name: "Record Application" }).click();
        await dialog.getByText("Minimum salary must be positive.").waitFor();
      },
    },
    {
      // 009 phase 4 (J5): h3 to h6 next to body text, a list and a table, fictional content.
      name: "note-headings",
      path: "/notes/note-e2e-1",
      prepare: (s) => {
        s.currentNoteDetail = {
          id: "note-e2e-1",
          heading: "Fictional heading levels",
          content: [
            "Opening paragraph in body text, before any heading.",
            "",
            "# Heading three",
            "",
            "Body text under heading three.",
            "",
            "## Heading four",
            "",
            "Body text under heading four, with a [fictional link](https://example.com/fictional).",
            "",
            "### Heading five",
            "",
            "Body text under heading five.",
            "",
            "#### Heading six",
            "",
            "Body text under heading six.",
            "",
            "- first point",
            "- second point",
            "",
          ].join("\n"),
          tags: ["fictional"],
          linkedEntityType: "posting",
          linkedEntityId: "post-e2e-1",
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-16T12:00:00Z",
        };
      },
    },
    { name: "criteria", path: "/criteria", census: true },
    {
      name: "notes-list",
      path: "/notes",
      prepare: (s) => {
        s.currentNotesList = [
          {
            id: "note-e2e-1",
            heading: "Fictional architecture review",
            tags: ["distributed-systems", "sre"],
            linkedEntityType: "architecture",
            linkedEntityId: null,
            createdAt: "2026-10-15T12:00:00Z",
            updatedAt: "2026-10-16T12:00:00Z",
          },
          {
            id: "note-e2e-2",
            heading: "Fictional planning note",
            tags: [],
            linkedEntityType: "",
            linkedEntityId: null,
            createdAt: "2026-10-20T12:00:00Z",
            updatedAt: "2026-10-21T12:00:00Z",
          },
        ];
      },
      census: true,
    },
    {
      name: "note-detail",
      path: "/notes/note-e2e-1",
      prepare: (s) => {
        s.currentNoteDetail = {
          id: "note-e2e-1",
          heading: "Fictional architecture review",
          content: "## Summary\n\nA short fictional note with **bold** text and a list:\n\n- first point\n- second point\n",
          tags: ["distributed-systems", "sre"],
          linkedEntityType: "posting",
          linkedEntityId: "post-e2e-1",
          createdAt: "2026-10-15T12:00:00Z",
          updatedAt: "2026-10-16T12:00:00Z",
        };
      },
      census: true,
    },
    {
      name: "search-runs",
      path: "/search-runs",
      prepare: (s) => {
        s.currentSearchRunsList = [
          { id: "run-e2e-1", runDate: "2026-10-31T08:00:00Z", newPostings: 3, summary: "Fictional batch run with 3 new postings." },
          { id: "run-e2e-2", runDate: "2026-10-24T08:00:00Z", newPostings: 7, summary: "Fictional batch run with 7 new postings." },
        ];
      },
      census: true,
    },
    { name: "not-authorized", path: "/not-authorized", census: true },
    { name: "service-unavailable", path: "/service-unavailable", census: true },
    { name: "auth-logout", path: "/auth/logout", census: true },
    { name: "auth-callback-replay", path: "/auth/callback?code=fictional-code&state=fictional-state", census: true },
    { name: "root-error-boundary", path: "/no-such-route", census: true },
  ];

  for (const route of routes) {
    for (const theme of THEMES) {
      test(`${route.name} (${theme})`, async ({ page, context }) => {
        Object.assign(state, defaultStubState());
        route.prepare?.(state);

        await context.addCookies([{ name: "roleradar_theme", value: theme, url: harness.baseUrl }]);
        await harness.signIn(context);
        await page.clock.setFixedTime(FIXED_NOW);
        await page.emulateMedia({ colorScheme: theme, reducedMotion: "reduce" });

        if (route.census && theme === "light") {
          // Server-rendered HTML as the browser receives it: nothing runs, so inline styles are exactly what the
          // markup carries. A redirect or error status is still recorded (the root error boundary is a 404).
          const response = await context.request.get(`${harness.baseUrl}${route.path}`, { maxRedirects: 0, failOnStatusCode: false });
          const html = await response.text();
          const elements = styleAttributeElements(html);
          census[route.name] = { count: elements.length, elements };
        }

        await page.goto(`${harness.baseUrl}${route.path}`, { waitUntil: "networkidle" });
        if (route.after) await route.after(page);
        await capture(page, route.name, theme, route.hover === true);
      });
    }
  }

  test.afterAll(() => {
    if (!VISUAL_DIR) return;
    fs.mkdirSync(VISUAL_DIR, { recursive: true });
    const sorted = Object.fromEntries(Object.entries(census).sort(([a], [b]) => a.localeCompare(b)));
    fs.writeFileSync(path.join(VISUAL_DIR, "census.json"), JSON.stringify(sorted, null, 2) + "\n", "utf8");
  });
});

test.describe("Visual baseline: Insights and the wide-table note (fictional loopback stub)", () => {
  test.skip(!VISUAL_DIR, "Set ROLERADAR_VISUAL_DIR (outside the repository) to run the visual baseline.");
  test.use({ viewport: { width: 1280, height: 800 } });

  const snapshot = loadSnapshot();
  const AS_OF = snapshot.asOf;
  let insights: InsightsHarness;

  test.beforeAll(async ({ browserName: _browserName }, testInfo) => {
    assertNotRealStackProject(testInfo.project.name);
    insights = await startInsightsHarness(INSIGHTS_SESSION_ID);
  });

  test.afterAll(async () => {
    await insights?.close();
  });

  interface InsightsCase {
    name: string;
    path: string;
    zeroedLens?: string;
    hover?: boolean;
    after?: (page: Page) => Promise<void>;
  }
  const pages: InsightsCase[] = [
    { name: "insights-90d", path: `/insights?window=90d&asOf=${AS_OF}` },
    { name: "insights-all", path: `/insights?window=all&asOf=${AS_OF}` },
    { name: "note-wide-table", path: `/notes/${loadNoteFixture().id}` },
    // 009 phase 4 (J6): a "No data" tile in the same grid row as ordinary tiles.
    { name: "insights-no-data", path: `/insights?window=90d&asOf=${AS_OF}`, zeroedLens: "ghost-eligible" },
    // 009 phase 4 (J1): a drill-down link under the pointer and under keyboard focus.
    {
      name: "insights-drill-link-hover",
      path: `/insights?window=90d&asOf=${AS_OF}`,
      hover: true,
      after: async (page) => {
        await page.locator('a[data-role="numerator"]').first().hover();
      },
    },
    {
      name: "insights-drill-link-focus",
      path: `/insights?window=90d&asOf=${AS_OF}`,
      after: async (page) => {
        await page.locator('a[data-role="numerator"]').first().focus();
      },
    },
  ];

  for (const entry of pages) {
    for (const theme of THEMES) {
      test(`${entry.name} (${theme})`, async ({ page, context }) => {
        await context.addCookies([{ name: "roleradar_theme", value: theme, url: insights.baseUrl }]);
        await insights.signIn(context);
        await page.clock.setFixedTime(FIXED_NOW);
        await page.emulateMedia({ colorScheme: theme, reducedMotion: "reduce" });

        if (theme === "light") {
          const response = await context.request.get(`${insights.baseUrl}${entry.path}`, { maxRedirects: 0, failOnStatusCode: false });
          const elements = styleAttributeElements(await response.text());
          census[entry.name] = { count: elements.length, elements };
        }

        insights.setZeroedLens(entry.zeroedLens ?? null);
        await page.goto(`${insights.baseUrl}${entry.path}`, { waitUntil: "networkidle" });
        if (entry.after) await entry.after(page);
        await capture(page, entry.name, theme, entry.hover === true);
        insights.setZeroedLens(null);
      });
    }
  }

  test.afterAll(() => {
    if (!VISUAL_DIR) return;
    fs.mkdirSync(VISUAL_DIR, { recursive: true });
    const sorted = Object.fromEntries(Object.entries(census).sort(([a], [b]) => a.localeCompare(b)));
    fs.writeFileSync(path.join(VISUAL_DIR, "census.json"), JSON.stringify(sorted, null, 2) + "\n", "utf8");
  });
});
