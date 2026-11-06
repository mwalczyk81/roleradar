import type { Locator, Page } from "@playwright/test";
import { test, expect } from "./support/csp-guard.js";
import { startAppHarness, defaultStubState, type AppHarness } from "./support/app-harness.js";
import { expectColourInheritedFromParent, expectDeclarations, expectNoStyleAttribute } from "./support/computed-style.js";

/**
 * 009 phase 3: pins for the routes whose inline styles became classes. Each pin lists the declarations the element
 * carried as an inline style (verbatim, tokens included) and asserts the element, reached through its new class, still
 * computes to exactly those values and no longer has a style attribute. Fictional loopback stub only.
 */

const SESSION_ID = "e2e-converted-styles-session";
const state = defaultStubState();
let harness: AppHarness;

test.beforeAll(async () => {
  harness = await startAppHarness(state, SESSION_ID);
});

test.afterAll(async () => {
  await harness.stop();
});

test.beforeEach(async ({ context }) => {
  Object.assign(state, defaultStubState());
  await harness.signIn(context);
});

async function open(page: Page, route: string): Promise<void> {
  await page.goto(`${harness.baseUrl}${route}`, { waitUntil: "networkidle" });
}

/** A pin: the class is present, the style attribute is gone, and the old declarations still compute the same. */
async function pin(locator: Locator, declarations: Record<string, string>): Promise<void> {
  await expectNoStyleAttribute(locator);
  await expectDeclarations(locator, declarations);
}

const STATUS_PAGE = {
  display: "flex",
  "align-items": "center",
  "justify-content": "center",
  "min-height": "100vh",
};
const STATUS_PAGE_EMPTY = { "background-color": "var(--ds-surface)", padding: "var(--ds-space-6)" };

const PAGE_STACK = { display: "flex", "flex-direction": "column", gap: "var(--ds-space-6)" };
const CELL_NOWRAP = { "white-space": "nowrap" };
const TEXT_EMPHASIS = { "font-weight": "var(--ds-weight-medium)", color: "var(--ds-text)" };
const ENTITY_LINK = { color: "var(--ds-primary)", "text-decoration": "none", "font-weight": "var(--ds-weight-medium)" };
const TAG_ROW = { display: "flex", gap: "var(--ds-space-1)", "flex-wrap": "wrap" };
const SEARCH_FORM = { display: "flex", gap: "var(--ds-space-2)", "max-width": "30rem" };

test.describe("slice 3a: small routes", () => {
  test("root error boundary body", async ({ page }) => {
    await open(page, "/no-such-route");
    await pin(page.locator("body.app-error-body"), { padding: "2rem", "font-family": "sans-serif" });
  });

  test("/not-authorized", async ({ page }) => {
    await open(page, "/not-authorized");
    await pin(page.locator("main.status-page.status-page-empty"), { ...STATUS_PAGE, ...STATUS_PAGE_EMPTY });
    await pin(page.locator("main button.button-neutral"), {
      display: "inline-flex",
      "align-items": "center",
      "justify-content": "center",
      padding: "var(--ds-space-2) var(--ds-space-4)",
      "border-radius": "var(--ds-radius-md)",
      "background-color": "var(--ds-surface-hover)",
      color: "var(--ds-text)",
      border: "thin solid var(--ds-border)",
      cursor: "pointer",
      "font-weight": "var(--ds-weight-medium)",
      "font-size": "var(--ds-text-sm)",
    });
  });

  test("/service-unavailable", async ({ page }) => {
    await open(page, "/service-unavailable");
    await pin(page.locator("main.status-page.status-page-empty"), { ...STATUS_PAGE, ...STATUS_PAGE_EMPTY });
    await pin(page.locator("main .stack-centered"), {
      display: "flex",
      "flex-direction": "column",
      "align-items": "center",
      gap: "var(--ds-space-3)",
    });
    // The snippet is a flex item, so its inline-block display computes to block (as it did when it was inline style).
    await pin(page.locator("main code.command-snippet"), {
      display: "block",
      padding: "var(--ds-space-2) var(--ds-space-4)",
      "background-color": "var(--ds-surface-hover)",
      color: "var(--ds-text)",
      "border-radius": "var(--ds-radius-md)",
      "font-family": "var(--ds-font-mono)",
      "font-size": "var(--ds-text-sm)",
      border: "thin solid var(--ds-border)",
    });
  });

  test("/auth/logout (signed-out page)", async ({ page }) => {
    await open(page, "/auth/logout");
    await pin(page.locator("main.signed-out-page"), { padding: "2rem", "text-align": "center" });
    await pin(page.locator("main .signed-out-link"), { "margin-top": "1rem" });
  });

  test("/auth/callback replay notice", async ({ page }) => {
    await open(page, "/auth/callback?code=fictional-code&state=fictional-state");
    await pin(page.locator("main.status-page.status-page-notice"), {
      ...STATUS_PAGE,
      padding: "var(--ds-space-4)",
      background: "var(--ds-bg)",
      color: "var(--ds-text)",
    });
    await pin(page.locator("main .notice-card"), {
      "max-width": "28rem",
      width: "28rem",
      background: "var(--ds-surface)",
      border: "var(--ds-border-thin) solid var(--ds-border)",
      "border-radius": "var(--ds-radius-lg)",
      padding: "var(--ds-space-6)",
      "text-align": "center",
      "box-shadow": "var(--ds-shadow-md)",
    });
    await pin(page.locator("main h1.notice-title"), {
      "font-size": "var(--ds-text-xl)",
      "font-weight": "var(--ds-weight-bold)",
      "margin-bottom": "var(--ds-space-3)",
    });
    await pin(page.locator("main p.notice-text"), {
      color: "var(--ds-text-muted)",
      "margin-bottom": "var(--ds-space-6)",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator("main a.link-button-primary"), {
      display: "inline-block",
      padding: "var(--ds-space-2) var(--ds-space-4)",
      background: "var(--ds-primary-solid)",
      color: "var(--ds-text-on-accent)",
      "border-radius": "var(--ds-radius-md)",
      "text-decoration": "none",
      "font-weight": "var(--ds-weight-semibold)",
      "font-size": "var(--ds-text-sm)",
    });
  });

  test("/search-runs", async ({ page }) => {
    state.currentSearchRunsList = [
      { id: "run-e2e-1", runDate: "2026-10-31T08:00:00Z", newPostings: 3, summary: "Fictional batch run with 3 new postings." },
    ];
    await open(page, "/search-runs");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("td .text-emphasis.cell-nowrap").first(), { ...TEXT_EMPHASIS, ...CELL_NOWRAP });
    await pin(page.locator("td .cell-prewrap.cell-summary-text").first(), {
      "white-space": "pre-wrap",
      display: "inline-block",
      color: "var(--ds-text-secondary)",
      "max-width": "24rem",
    });
  });

  test("/criteria", async ({ page }) => {
    await open(page, "/criteria");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("td .text-emphasis").first(), TEXT_EMPHASIS);
    await pin(page.locator("td .cell-prewrap.cell-rule-text").first(), {
      "white-space": "pre-wrap",
      display: "inline-block",
      color: "var(--ds-text)",
      "max-width": "25rem",
    });
    await pin(page.locator("td .cell-nowrap").first(), CELL_NOWRAP);
    await pin(page.locator("td .cell-nowrap").nth(1), CELL_NOWRAP);
  });
});

test.describe("slice 3b: list routes", () => {
  test("/companies", async ({ page }) => {
    await open(page, "/companies");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("form.search-form"), SEARCH_FORM);
    await pin(page.locator("td a.entity-link.cell-nowrap").first(), { ...ENTITY_LINK, ...CELL_NOWRAP });
    await pin(page.locator("td .tag-row").first(), TAG_ROW);
  });

  test("/applications", async ({ page }) => {
    await open(page, "/applications");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator(".filter-bar"), {
      display: "flex",
      "align-items": "center",
      "justify-content": "space-between",
      "flex-wrap": "wrap",
      gap: "var(--ds-space-4)",
    });
    await pin(page.locator(".filter-group").first(), { display: "flex", "align-items": "center", gap: "var(--ds-space-2)" });
    await pin(page.locator("label.field-caption"), { "font-size": "var(--ds-text-sm)", color: "var(--ds-text-secondary)" });
    await pin(page.locator(".filter-select-width"), { width: "12rem" });
    await pin(page.locator("div.field-caption"), { "font-size": "var(--ds-text-sm)", color: "var(--ds-text-secondary)" });
    await pin(page.locator("td a.entity-link").first(), ENTITY_LINK);
    // The posting title link named an undefined token for its colour, so it has always inherited its colour.
    const title = page.locator("td a.entity-title-link").first();
    await pin(title, { "font-weight": "var(--ds-weight-medium)", "text-decoration": "none" });
    await expectColourInheritedFromParent(title);
    await pin(page.locator("td a.row-action-link").first(), {
      "font-size": "var(--ds-text-sm)",
      color: "var(--ds-primary)",
      "text-decoration": "none",
      "white-space": "nowrap",
    });
  });

  test("/postings", async ({ page }) => {
    await open(page, "/postings");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("td a.entity-link").first(), ENTITY_LINK);
    await pin(page.locator("td .cell-nowrap").first(), CELL_NOWRAP);
    await pin(page.locator(".filter-group").first(), { display: "flex", gap: "var(--ds-space-2)", "align-items": "center" });
    // The Min Score input keeps its inline width: the kit Input sets an inline width of 100% that no class can override
    // (kit request K6). It is the one remaining style attribute in this file.
    const minScore = page.getByPlaceholder("Min Score");
    await expectDeclarations(minScore, { width: "7rem" });
    expect(await minScore.getAttribute("style")).toContain("width:7rem");
  });

  test("/notes", async ({ page }) => {
    state.currentNotesList = [
      {
        id: "note-e2e-1",
        heading: "Fictional architecture review",
        tags: ["distributed-systems", "sre"],
        linkedEntityType: "architecture",
        linkedEntityId: null,
        createdAt: "2026-10-15T12:00:00Z",
        updatedAt: "2026-10-16T12:00:00Z",
      },
    ];
    await open(page, "/notes");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("form.search-form"), SEARCH_FORM);
    await pin(page.locator("td a.entity-link").first(), ENTITY_LINK);
    await pin(page.locator("td .tag-row").first(), TAG_ROW);
    await pin(page.locator("td .cell-nowrap").first(), CELL_NOWRAP);
    await pin(page.locator("td .cell-nowrap").nth(1), CELL_NOWRAP);
  });

  test("/notes/:id", async ({ page }) => {
    state.currentNoteDetail = {
      id: "note-e2e-1",
      heading: "Fictional architecture review",
      content: "A short fictional note.",
      tags: ["distributed-systems", "sre"],
      linkedEntityType: "posting",
      linkedEntityId: "post-e2e-1",
      createdAt: "2026-10-15T12:00:00Z",
      updatedAt: "2026-10-16T12:00:00Z",
    };
    await open(page, "/notes/note-e2e-1");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("a.back-link"), {
      display: "inline-flex",
      "align-items": "center",
      gap: "var(--ds-space-1)",
      color: "var(--ds-text-secondary)",
      "text-decoration": "none",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator(".card-stack"), { display: "flex", "flex-direction": "column", gap: "var(--ds-space-4)" });
    await pin(page.locator("h2.note-title"), {
      margin: "0",
      "font-size": "var(--ds-text-2xl)",
      color: "var(--ds-text)",
      "font-weight": "var(--ds-weight-bold)",
    });
    await pin(page.locator(".meta-row"), {
      display: "flex",
      "flex-wrap": "wrap",
      gap: "var(--ds-space-3)",
      "align-items": "center",
      color: "var(--ds-text-secondary)",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator(".meta-row a.entity-link"), ENTITY_LINK);
    await pin(page.locator(".tag-row.tag-row-roomy"), { display: "flex", gap: "var(--ds-space-2)", "flex-wrap": "wrap" });
  });
});
