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
    await pin(page.locator("main code.command-snippet"), {
      display: "inline-block",
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
      width: "100%",
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

test.describe("slice 3c: posting detail and the transition dialog", () => {
  const FIELD = { display: "flex", "flex-direction": "column", gap: "var(--ds-space-1)" };
  const LABEL = { "font-size": "var(--ds-text-sm)", "font-weight": "var(--ds-weight-medium)" };

  test("/postings/:id", async ({ page }) => {
    state.currentPostingAssessmentNote = { summary: "Fictional assessment summary.", updatedAt: "2026-10-31T11:00:00Z" };
    await open(page, "/postings/post-e2e-1");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("a.back-link"), {
      display: "inline-flex",
      "align-items": "center",
      gap: "var(--ds-space-1)",
      color: "var(--ds-text-secondary)",
      "text-decoration": "none",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator(".detail-header-row"), {
      display: "flex",
      "justify-content": "space-between",
      "align-items": "flex-start",
      "flex-wrap": "wrap",
      gap: "var(--ds-space-4)",
    });
    await pin(page.locator("h2.page-heading.detail-title"), { margin: "0 0 var(--ds-space-1) 0", "font-size": "var(--ds-text-xl)" });
    await pin(page.locator(".detail-meta"), {
      display: "flex",
      gap: "var(--ds-space-3)",
      "align-items": "center",
      color: "var(--ds-text-secondary)",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator(".action-group"), { display: "flex", gap: "var(--ds-space-2)", "align-items": "center" });
    await pin(page.locator(".score-label"), {
      "font-family": "var(--ds-font-mono)",
      "font-size": "var(--ds-text-sm)",
      "font-weight": "var(--ds-weight-bold)",
    });
    await pin(page.locator(".detail-grid"), {
      display: "grid",
      "grid-template-columns": "repeat(auto-fit, minmax(20rem, 1fr))",
      gap: "var(--ds-space-6)",
    });
    for (const index of [0, 1, 2]) {
      await pin(page.locator("h3.page-heading.section-title").nth(index), {
        margin: "0 0 var(--ds-space-3) 0",
        "font-size": "var(--ds-text-base)",
      });
    }
    await pin(page.locator("dl.detail-list"), {
      display: "grid",
      "grid-template-columns": "max-content 1fr",
      gap: "var(--ds-space-2)",
      margin: "0",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator("dt.detail-term").first(), { color: "var(--ds-text-secondary)" });
    await pin(page.locator("dd.detail-value").first(), { margin: "0" });
    await pin(page.locator("a.break-link"), { color: "var(--ds-primary)", "word-break": "break-all" });
    await pin(page.locator(".text-small").first(), { "font-size": "var(--ds-text-sm)" });
    await pin(page.locator("p.note-summary"), { margin: "0 0 var(--ds-space-2) 0", "white-space": "pre-wrap" });
    await pin(page.locator(".caption-small").first(), { color: "var(--ds-text-secondary)", "font-size": "var(--ds-text-xs)" });
    await pin(page.locator(".signal-list"), { display: "flex", "flex-direction": "column", gap: "var(--ds-space-2)" });
    await pin(page.locator(".signal-row"), {
      padding: "var(--ds-space-3)",
      "background-color": "var(--ds-surface-hover)",
      "border-radius": "var(--ds-radius-md)",
      display: "flex",
      "justify-content": "space-between",
      "align-items": "center",
      gap: "var(--ds-space-2)",
    });
    await pin(page.locator(".signal-evidence"), { color: "var(--ds-text-secondary)", "margin-left": "var(--ds-space-2)" });
  });

  test("/postings/:id with no assessment note shows the empty caption", async ({ page }) => {
    await open(page, "/postings/post-e2e-1");
    await pin(page.locator(".field-caption", { hasText: "No assessment note recorded" }), {
      color: "var(--ds-text-secondary)",
      "font-size": "var(--ds-text-sm)",
    });
  });

  test("/postings/:id keeps the four Card padding overrides as inline style (kit request K6)", async ({ page }) => {
    await open(page, "/postings/post-e2e-1");
    // The kit Card applies its own inline padding after the consumer's, so no class can change it. Four cards ask for
    // space-5 instead of the default space-6; they keep the style prop until the kit offers a padding prop (K6).
    const cards = page.locator('div[style*="padding:var(--ds-space-5)"]');
    expect(await cards.count()).toBe(4);
  });

  test("transition dialog: form, fields, labels, actions", async ({ page }) => {
    await open(page, "/postings/post-e2e-1");
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await pin(dialog.locator("form.dialog-form"), {
      display: "flex",
      "flex-direction": "column",
      gap: "var(--ds-space-4)",
      "font-family": "var(--ds-font-sans)",
    });
    await pin(dialog.locator("div.field-caption").first(), { "font-size": "var(--ds-text-sm)", color: "var(--ds-text-secondary)" });
    await pin(dialog.locator(".dialog-field").first(), FIELD);
    await pin(dialog.locator("label.dialog-field-label").first(), LABEL);
    await pin(dialog.locator(".dialog-actions"), {
      display: "flex",
      "justify-content": "flex-end",
      gap: "var(--ds-space-3)",
      "margin-top": "var(--ds-space-3)",
    });
  });

  test("transition dialog: general and field errors", async ({ page }) => {
    await open(page, "/postings/post-e2e-1");
    state.nextTransitionResponseStatus = 400;
    state.nextTransitionResponseBody = {
      errorCode: "INVALID_TRANSITION",
      message: "Transition to this disposition is not allowed by policy.",
      details: [
        { field: "toDisposition", message: "Transition not allowed." },
        { field: "reason", message: "Reason is required." },
      ],
    };
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Reason *").fill("Attempting invalid transition");
    await dialog.getByRole("button", { name: "Apply Transition" }).click();
    await expect(dialog.getByRole("alert")).toBeVisible();
    await pin(dialog.locator('[role="alert"].dialog-alert-danger'), {
      padding: "var(--ds-space-3)",
      "background-color": "var(--ds-danger-subtle)",
      color: "var(--ds-danger-text)",
      "border-radius": "var(--ds-radius-md)",
      "font-size": "var(--ds-text-sm)",
      "font-weight": "var(--ds-weight-medium)",
    });
    const errors = dialog.locator("span.dialog-field-error");
    if ((await errors.count()) > 0) {
      await pin(errors.first(), { color: "var(--ds-danger-text)", "font-size": "var(--ds-text-xs)" });
    }
  });

  test("transition dialog: no permitted transitions notice", async ({ page }) => {
    state.currentPostingDisposition = "screened_out";
    await open(page, "/postings/post-e2e-1");
    await page.getByRole("button", { name: "Change Disposition" }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await pin(dialog.locator(".dialog-notice"), {
      padding: "var(--ds-space-3)",
      "background-color": "var(--ds-surface-hover)",
      color: "var(--ds-text-secondary)",
      "border-radius": "var(--ds-radius-md)",
      "font-size": "var(--ds-text-sm)",
    });
  });
});

test.describe("slice 3d: company detail", () => {
  const FORM_LABEL = {
    display: "block",
    "font-size": "var(--ds-text-sm)",
    "font-weight": "600",
    "margin-bottom": "var(--ds-space-1)",
  };
  const FORM_ERROR = { color: "var(--ds-danger-text)", "font-size": "var(--ds-text-xs)", "margin-top": "var(--ds-space-1)" };
  const BORDER = "var(--ds-border-thin) solid var(--ds-border)";

  test("/companies/:id header, form and sources", async ({ page }) => {
    await open(page, "/companies/comp-e2e-1");
    await pin(page.locator(".page-stack").first(), PAGE_STACK);
    await pin(page.locator("a.back-link"), {
      display: "inline-flex",
      "align-items": "center",
      gap: "var(--ds-space-1)",
      color: "var(--ds-text-secondary)",
      "text-decoration": "none",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator(".detail-header-row"), {
      display: "flex",
      "justify-content": "space-between",
      "align-items": "flex-start",
      gap: "var(--ds-space-4)",
      "flex-wrap": "wrap",
    });
    await pin(page.locator("h2.page-heading.detail-heading-xl"), {
      margin: "0",
      "font-size": "var(--ds-text-2xl)",
      "font-weight": "700",
    });
    await pin(page.locator(".detail-badges"), {
      display: "flex",
      gap: "var(--ds-space-2)",
      "margin-top": "var(--ds-space-2)",
      "align-items": "center",
      "flex-wrap": "wrap",
    });
    await pin(page.locator(".detail-badges .field-caption"), { color: "var(--ds-text-secondary)", "font-size": "var(--ds-text-sm)" });
    await pin(page.locator("form.form-stack"), { display: "flex", "flex-direction": "column", gap: "var(--ds-space-5)" });
    await pin(page.locator("h2.page-heading.form-heading"), { margin: "0", "font-size": "var(--ds-text-lg)", "font-weight": "600" });
    for (const id of ["company-name-input", "company-status-select", "company-ownership-select", "company-hq-input", "company-desc-input", "category-tag-input"]) {
      await pin(page.locator(`label.form-label[for="${id}"]`), FORM_LABEL);
    }
    await pin(page.locator(".form-grid"), {
      display: "grid",
      "grid-template-columns": "repeat(auto-fit, minmax(12rem, 1fr))",
      gap: "var(--ds-space-4)",
    });
    await pin(page.locator(".chip-row"), {
      display: "flex",
      gap: "var(--ds-space-2)",
      "flex-wrap": "wrap",
      "align-items": "center",
      "margin-bottom": "var(--ds-space-2)",
    });
    await pin(page.locator(".chip").first(), {
      display: "inline-flex",
      "align-items": "center",
      gap: "var(--ds-space-1)",
      "background-color": "var(--ds-surface-hover)",
      padding: "var(--ds-space-1) var(--ds-space-2)",
      "border-radius": "var(--ds-radius-sm)",
      "font-size": "var(--ds-text-xs)",
    });
    // `background: none` is pinned as its longhands: the CSS minifier may write the shorthand as `0 0`, which computes to
    // the same transparent, image-less background but serialises the (unused) position differently.
    await pin(page.locator("button.chip-remove").first(), {
      "background-color": "transparent",
      "background-image": "none",
      border: "none",
      color: "var(--ds-text-secondary)",
      cursor: "pointer",
      padding: "0",
      "font-size": "var(--ds-text-xs)",
    });
    await pin(page.locator(".inline-form-row"), { display: "flex", gap: "var(--ds-space-2)", "max-width": "24rem" });
    await pin(page.locator(".form-subheading"), { "font-size": "var(--ds-text-sm)", "font-weight": "600", "margin-bottom": "var(--ds-space-2)" });
    await pin(page.locator(".source-list"), {
      display: "flex",
      "flex-direction": "column",
      gap: "var(--ds-space-2)",
      "margin-bottom": "var(--ds-space-4)",
    });
    await pin(page.locator(".source-row"), {
      display: "flex",
      "justify-content": "space-between",
      "align-items": "center",
      padding: "var(--ds-space-3)",
      "border-radius": "var(--ds-radius-md)",
      border: BORDER,
      gap: "var(--ds-space-3)",
    });
    await pin(page.locator(".source-details"), { display: "flex", "flex-direction": "column", gap: "var(--ds-space-1)" });
    await pin(page.locator(".source-url"), { "font-weight": "600", "font-size": "var(--ds-text-sm)" });
    await pin(page.locator("a.underline-link"), { color: "var(--ds-primary)", "text-decoration": "underline" });
    await pin(page.locator(".source-meta"), {
      display: "flex",
      gap: "var(--ds-space-2)",
      "font-size": "var(--ds-text-xs)",
      color: "var(--ds-text-secondary)",
    });
    await pin(page.locator(".subform-panel"), {
      display: "flex",
      "flex-direction": "column",
      gap: "var(--ds-space-3)",
      padding: "var(--ds-space-4)",
      "background-color": "var(--ds-surface)",
      "border-radius": "var(--ds-radius-md)",
      border: BORDER,
    });
    await pin(page.locator(".subform-title"), { "font-weight": "600", "font-size": "var(--ds-text-xs)", "text-transform": "uppercase" });
    await pin(page.locator(".subform-grid"), {
      display: "grid",
      "grid-template-columns": "repeat(auto-fit, minmax(11rem, 1fr))",
      gap: "var(--ds-space-3)",
    });
    for (const id of ["new-source-url", "new-ats-provider", "new-ats-tenant", "new-fetch-method"]) {
      await pin(page.locator(`label.form-label-small[for="${id}"]`), {
        display: "block",
        "font-size": "var(--ds-text-xs)",
        "margin-bottom": "var(--ds-space-1)",
      });
    }
    await pin(page.locator(".form-actions"), { display: "flex", gap: "var(--ds-space-3)", "margin-top": "var(--ds-space-2)" });
  });

  test("/companies/:id with no career sources shows the empty caption", async ({ page }) => {
    state.currentCompanyCareerSources = [];
    await open(page, "/companies/comp-e2e-1");
    await pin(page.locator(".empty-caption"), {
      color: "var(--ds-text-secondary)",
      "font-size": "var(--ds-text-sm)",
      "margin-bottom": "var(--ds-space-3)",
    });
  });

  test("/companies/:id after a rejected save shows styled field errors", async ({ page }) => {
    await open(page, "/companies/comp-e2e-1");
    state.nextCompanyResponseStatus = 400;
    state.nextCompanyResponseBody = {
      errorCode: "VALIDATION_ERROR",
      message: "Validation failed.",
      details: [
        { field: "name", issue: "Company name cannot be blank." },
        { field: "status", issue: "Status is not recognised." },
        { field: "ownershipType", issue: "Ownership type is not recognised." },
      ],
    };
    await page.getByRole("button", { name: "Save Changes" }).click();
    await expect(page.getByText("Company name cannot be blank.")).toBeVisible();
    for (const id of ["company-name-error", "company-status-error", "company-ownership-error"]) {
      await pin(page.locator(`#${id}.form-error`), FORM_ERROR);
    }
  });

  test("/companies/:id keeps the header Card padding override as inline style (kit request K6); the form Card repeats the kit default", async ({ page }) => {
    await open(page, "/companies/comp-e2e-1");
    expect(await page.locator('div[style*="padding:var(--ds-space-5)"]').count()).toBe(1);
  });
});

// ---------------------------------------------------------------------------------------------------------------
// Slice 3e: /applications/:id (timeline, append-event dialog) and the create-application dialog on /postings/:id.
// ---------------------------------------------------------------------------------------------------------------

const FIELD_LABEL = {
  display: "block",
  "margin-bottom": "var(--ds-space-1)",
  "font-size": "var(--ds-text-sm)",
};
const FIELD_LABEL_MEDIUM = { ...FIELD_LABEL, "font-weight": "var(--ds-weight-medium)" };
const INLINE_FIELD_ERROR = { color: "var(--ds-danger)", "font-size": "var(--ds-text-xs)" };
const MODAL_ACTIONS = {
  display: "flex",
  "justify-content": "flex-end",
  gap: "var(--ds-space-2)",
  "margin-top": "var(--ds-space-2)",
};
const PRIMARY_LINK = { color: "var(--ds-primary)", "text-decoration": "none" };
const COLUMN_GAP_4 = { display: "flex", "flex-direction": "column", gap: "var(--ds-space-4)" };

const FICTIONAL_TIMELINE = [
  { eventType: "applied", eventDate: "2026-10-31", notes: "Initial application submitted", eventSource: "web" },
  { eventType: "follow_up", eventDate: "2026-11-01", notes: null },
  { eventType: "note", eventDate: "2026-11-02", notes: "Recruiter called.\nSecond line of a fictional note." },
  { eventType: "rejected", eventDate: "2026-11-03", notes: "Rejected by email", eventSource: "posting_transition" },
];

test.describe("slice 3e: applications.$id", () => {
  test("/applications/:id page, summary card and timeline", async ({ page }) => {
    state.currentApplicationEvents = FICTIONAL_TIMELINE;
    state.currentApplicationStatus = "rejected";
    await open(page, "/applications/app-e2e-1");

    await pin(page.locator("main .page-stack").first(), PAGE_STACK);
    await pin(page.locator("a.back-link"), {
      display: "inline-flex",
      "align-items": "center",
      gap: "var(--ds-space-1)",
      color: "var(--ds-text-secondary)",
      "text-decoration": "none",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator(".detail-header-row"), {
      display: "flex",
      "justify-content": "space-between",
      "align-items": "flex-start",
      "flex-wrap": "wrap",
      gap: "var(--ds-space-4)",
    });
    await pin(page.locator("h2.page-heading.detail-title"), { margin: "0 0 var(--ds-space-1) 0", "font-size": "var(--ds-text-xl)" });
    await pin(page.locator(".detail-meta"), {
      display: "flex",
      gap: "var(--ds-space-3)",
      "align-items": "center",
      color: "var(--ds-text-secondary)",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(page.locator("a.link-primary").first(), PRIMARY_LINK);
    await pin(page.locator(".detail-header-row > .action-group"), {
      display: "flex",
      gap: "var(--ds-space-2)",
      "align-items": "center",
    });
    await pin(page.locator(".icon-leading").first(), { "margin-right": "var(--ds-space-1)" });

    await pin(page.locator(".timeline-header"), {
      display: "flex",
      "justify-content": "space-between",
      "align-items": "center",
      "margin-bottom": "var(--ds-space-4)",
    });
    await pin(page.locator("h3.page-heading.timeline-heading"), { margin: "0", "font-size": "var(--ds-text-base)" });
    await pin(page.locator(".timeline"), { ...COLUMN_GAP_4, position: "relative" });

    const events = page.getByTestId("timeline-event");
    await expect(events).toHaveCount(4);
    const eventBase = {
      display: "flex",
      "flex-direction": "column",
      gap: "var(--ds-space-1)",
      padding: "var(--ds-space-3)",
      "background-color": "var(--ds-surface-hover)",
      "border-radius": "var(--ds-radius-md)",
    };
    await pin(events.nth(0), { ...eventBase, "border-left": "var(--ds-space-1) solid var(--ds-primary)" });
    await pin(events.nth(3), { ...eventBase, "border-left": "var(--ds-space-1) solid var(--ds-danger)" });
    await pin(events.nth(0).locator(".timeline-event-head"), {
      display: "flex",
      "justify-content": "space-between",
      "align-items": "center",
    });
    await pin(events.nth(0).locator(".timeline-event-head > .action-group"), {
      display: "flex",
      gap: "var(--ds-space-2)",
      "align-items": "center",
    });
    await pin(events.nth(0).locator(".timeline-event-label"), {
      "font-weight": "var(--ds-weight-bold)",
      "font-size": "var(--ds-text-sm)",
    });
    await pin(events.nth(0).locator(".timeline-event-head .caption-small").first(), {
      "font-size": "var(--ds-text-xs)",
      color: "var(--ds-text-secondary)",
    });
    // 008: the source line and its test id are untouched by the conversion.
    await expect(events.nth(3).getByTestId("event-source")).toHaveText("Source: posting_transition");
    await expect(events.nth(0).getByTestId("event-source")).toHaveText("Source: web");
    await expect(events.nth(1).getByTestId("event-source")).toHaveCount(0);
    await pin(events.nth(3).getByTestId("event-source"), { "font-size": "var(--ds-text-xs)", color: "var(--ds-text-secondary)" });
    await pin(events.nth(2).locator("p.timeline-event-notes"), {
      margin: "var(--ds-space-1) 0 0 0",
      "font-size": "var(--ds-text-sm)",
      "white-space": "pre-wrap",
    });
    // The notes colour named an undefined token (--ds-text-primary) and so inherits; the pin states that.
    await expectColourInheritedFromParent(events.nth(2).locator("p.timeline-event-notes"));
  });

  test("/applications/:id with no events shows the empty caption", async ({ page }) => {
    state.currentApplicationEvents = [];
    await open(page, "/applications/app-e2e-1");
    await pin(page.locator("p.field-caption"), { color: "var(--ds-text-secondary)", "font-size": "var(--ds-text-sm)" });
  });

  test("/applications/:id keeps the two Card padding overrides as inline style (kit request K6)", async ({ page }) => {
    await open(page, "/applications/app-e2e-1");
    expect(await page.locator('div[style*="padding:var(--ds-space-5)"]').count()).toBe(2);
  });

  test("/applications/:id append-event dialog", async ({ page }) => {
    await open(page, "/applications/app-e2e-1");
    await page.getByRole("button", { name: "Append Event" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.waitFor();

    await pin(dialog.locator("form > .card-stack"), COLUMN_GAP_4);
    await pin(dialog.locator('label[for="append-event-type"]'), FIELD_LABEL_MEDIUM);
    await pin(dialog.locator('label[for="append-event-date"]'), FIELD_LABEL_MEDIUM);
    await pin(dialog.locator('label[for="append-event-notes"]'), FIELD_LABEL);
    await pin(dialog.locator('label[for="append-event-source"]'), FIELD_LABEL);
    await pin(dialog.locator(".modal-actions"), MODAL_ACTIONS);

    await dialog.getByLabel("Event Type *").selectOption("rejected");
    await pin(dialog.getByTestId("posting-sync-note"), {
      margin: "var(--ds-space-2) 0 0 0",
      "font-size": "var(--ds-text-xs)",
      color: "var(--ds-text-secondary)",
      "background-color": "var(--ds-surface-sunken)",
      padding: "var(--ds-space-2)",
      "border-radius": "var(--ds-radius-sm)",
    });
  });

  test("/applications/:id append-event dialog after a rejected submit shows styled field errors", async ({ page }) => {
    await open(page, "/applications/app-e2e-1");
    state.nextApplicationResponseStatus = 400;
    state.nextApplicationResponseBody = {
      errorCode: "VALIDATION_ERROR",
      message: "Validation failed.",
      details: [
        { field: "eventDate", message: "Event date cannot be in the future." },
        { field: "notes", message: "Notes are too long." },
        { field: "eventType", message: "Event type is not allowed." },
      ],
    };
    await page.getByRole("button", { name: "Append Event" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("button", { name: "Append Event" }).click();
    for (const text of ["Event date cannot be in the future.", "Notes are too long.", "Event type is not allowed."]) {
      await expect(dialog.getByText(text)).toBeVisible();
      await pin(dialog.getByText(text), INLINE_FIELD_ERROR);
    }
  });
});

test.describe("slice 3e: create-application dialog on /postings/:id", () => {
  // The override panel makes the dialog taller than the default viewport; the submit button must be reachable.
  test.use({ viewport: { width: 1280, height: 1100 } });

  test("default dialog on a lead posting", async ({ page }) => {
    state.currentPostingDisposition = "lead";
    await open(page, "/postings/post-e2e-1");
    await page.getByRole("button", { name: "Create Application" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.waitFor();

    await pin(dialog.locator("form > .card-stack"), COLUMN_GAP_4);
    await pin(dialog.locator("p.dialog-subject"), {
      margin: "0",
      "font-size": "var(--ds-text-sm)",
      color: "var(--ds-text-secondary)",
    });
    await pin(dialog.locator('label[for="app-submitted-date"]'), FIELD_LABEL_MEDIUM);
    await pin(dialog.locator('label[for="app-resume-variant"]'), FIELD_LABEL_MEDIUM);
    await pin(dialog.locator(".salary-row"), {
      display: "grid",
      "grid-template-columns": "1fr 1fr 5rem",
      gap: "var(--ds-space-2)",
    });
    for (const id of ["app-salary-min", "app-salary-max", "app-salary-curr", "app-target-start", "app-resume-pointer", "app-confirmation-code"]) {
      await pin(dialog.locator(`label[for="${id}"]`), FIELD_LABEL);
    }
    await pin(dialog.locator(".modal-actions"), MODAL_ACTIONS);
  });

  test("override panel on a closed posting", async ({ page }) => {
    state.currentPostingDisposition = "closed";
    await open(page, "/postings/post-e2e-1");
    await page.getByRole("button", { name: "Create Application" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.waitFor();

    await pin(dialog.locator(".override-panel"), {
      display: "flex",
      "flex-direction": "column",
      gap: "var(--ds-space-2)",
      padding: "var(--ds-space-3)",
      "background-color": "var(--ds-surface-hover)",
      "border-radius": "var(--ds-radius-md)",
    });
    await pin(dialog.locator(".override-panel > .action-group"), {
      display: "flex",
      "align-items": "center",
      gap: "var(--ds-space-2)",
    });
    await pin(dialog.locator('label[for="app-override-decided"]'), {
      "font-size": "var(--ds-text-sm)",
      "font-weight": "var(--ds-weight-medium)",
    });
    await pin(dialog.locator('label[for="app-override-reason"]'), {
      display: "block",
      "margin-bottom": "var(--ds-space-1)",
      "font-size": "var(--ds-text-xs)",
      color: "var(--ds-text-secondary)",
    });
  });

  test("after a rejected submit the field errors are styled", async ({ page }) => {
    state.currentPostingDisposition = "closed";
    state.nextApplicationResponseStatus = 400;
    state.nextApplicationResponseBody = {
      errorCode: "VALIDATION_ERROR",
      message: "Validation failed.",
      details: [
        { field: "desiredSalaryMin", message: "Minimum salary must be positive." },
        { field: "confirmationCode", message: "Confirmation code is too long." },
        { field: "overrideReason", message: "Override reason is too short." },
      ],
    };
    await open(page, "/postings/post-e2e-1");
    await page.getByRole("button", { name: "Create Application" }).click();
    const dialog = page.getByRole("dialog");
    await dialog.getByLabel("Override Reason *").fill("Fictional reason to reopen this posting.");
    await dialog.getByLabel("Desired Min").fill("-5");
    await dialog.getByRole("button", { name: "Record Application" }).click();
    for (const text of ["Minimum salary must be positive.", "Confirmation code is too long.", "Override reason is too short."]) {
      await expect(dialog.getByText(text)).toBeVisible();
      await pin(dialog.getByText(text), INLINE_FIELD_ERROR);
    }
  });
});
