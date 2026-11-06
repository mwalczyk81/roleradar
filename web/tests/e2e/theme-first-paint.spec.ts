import { test, expect } from "./support/csp-guard.js";
import { startAppHarness, defaultStubState, type AppHarness } from "./support/app-harness.js";

/**
 * 009 US2: the first server-rendered response already carries the right data-theme, before any script runs.
 * The HTML is fetched through the browser context's request client, which executes nothing, so what is asserted is
 * exactly what the browser receives before hydration. Fictional loopback stub only.
 */

const SESSION_ID = "e2e-theme-first-paint-session";
let harness: AppHarness;

test.beforeAll(async () => {
  harness = await startAppHarness(defaultStubState(), SESSION_ID);
});

test.afterAll(async () => {
  await harness.stop();
});

function htmlTag(html: string): string {
  const match = /<html\b[^>]*>/.exec(html);
  expect(match, "the response has an <html> tag").not.toBeNull();
  return match?.[0] ?? "";
}

async function firstResponseHtml(
  context: import("@playwright/test").BrowserContext,
  themeCookie: string | null,
  route = "/postings"
): Promise<string> {
  await harness.signIn(context);
  if (themeCookie) {
    await context.addCookies([{ name: "roleradar_theme", value: themeCookie, url: harness.baseUrl }]);
  }
  const response = await context.request.get(`${harness.baseUrl}${route}`, { failOnStatusCode: false, maxRedirects: 0 });
  expect(response.status()).toBe(200);
  return await response.text();
}

test.describe("first paint theme (server-rendered, no script)", () => {
  for (const theme of ["dark", "light"] as const) {
    test(`cookie ${theme}: the first response has data-theme="${theme}"`, async ({ context }) => {
      const html = await firstResponseHtml(context, theme);
      expect(htmlTag(html)).toContain(`data-theme="${theme}"`);
    });
  }

  test("an invalid cookie value is ignored: no data-theme is rendered by the server", async ({ context }) => {
    const html = await firstResponseHtml(context, "purple");
    expect(htmlTag(html)).not.toContain("data-theme");
  });

  test("no cookie: the server renders no data-theme and the head script decides before first paint", async ({ context }) => {
    const html = await firstResponseHtml(context, null);
    expect(htmlTag(html)).not.toContain("data-theme");
    const head = html.slice(0, html.indexOf("</head>"));
    expect(head).toMatch(/<script[^>]*nonce="[^"]+"[^>]*>[^<]*roleradar_theme[^<]*<\/script>/);
  });

  test("the head script runs before the body and sets the system theme when there is no cookie", async ({ browser }) => {
    const context = await browser.newContext({ ignoreHTTPSErrors: true, colorScheme: "dark" });
    try {
      await harness.signIn(context);
      const page = await context.newPage();
      // domcontentloaded: the document is parsed and the blocking head script has run, but React has not hydrated.
      await page.route("**/*.js", (route) => route.abort());
      await page.goto(`${harness.baseUrl}/postings`, { waitUntil: "domcontentloaded" });
      await expect(page.locator("html")).toHaveAttribute("data-theme", "dark");
    } finally {
      await context.close();
    }
  });
});
