import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as postingsLoader } from "../../app/routes/postings._index.js";
import { loader as applicationsLoader } from "../../app/routes/applications._index.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";
import {
  LENS_KEYS,
  lensCountQuery,
  lensEntity,
  lensFilterString,
  lensListHref,
  type LensContext,
  type LensKey,
} from "../../app/lib/insights-lenses.js";

const AS_OF = "2026-09-30";
const NINETY: LensContext = { window: "90d", asOf: AS_OF };
const ALL: LensContext = { window: "all", asOf: AS_OF };

const emptyPage = { data: { value: [], "@odata.count": 0 }, error: undefined, response: { status: 200 } as Response };

describe("List pages apply the Insights lens (T041): the count and the list use the same filter", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockFetchOData = vi.fn();
  const mockOidcConfig = new client.Configuration(
    {
      issuer: "https://identity:5001",
      authorization_endpoint: "https://identity:5001/connect/authorize",
      token_endpoint: "https://identity:5001/connect/token",
      end_session_endpoint: "https://identity:5001/connect/endsession",
    },
    "roleradar-web",
    "test-secret"
  );

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    setOidcConfigForTesting(mockOidcConfig);
    vi.restoreAllMocks();
    mockFetchOData.mockReset();
    mockFetchOData.mockResolvedValue(emptyPage);
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
  });

  function sessionCookie(): string {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-001",
      ownerUsername: "fictional-owner",
      accessToken: "mock-access-token-lens",
      refreshToken: "mock-refresh-token-lens",
      idToken: "mock-id-token-lens",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-lens",
    };
    saveSession(session);
    return `${SECURE_COOKIE_NAME}=${sessionId}`;
  }

  async function listFilter(path: string, loader: typeof postingsLoader | typeof applicationsLoader): Promise<{ filter: string | null; query: URLSearchParams; data: Record<string, unknown> }> {
    mockFetchOData.mockClear();
    const request = new Request(`https://localhost:5003${path}`, { headers: { Cookie: sessionCookie() } });
    const data = (await callLoader(loader, request)) as Record<string, unknown>;
    expect(mockFetchOData).toHaveBeenCalledTimes(1);
    const [, queryString] = mockFetchOData.mock.calls[0];
    const query = new URLSearchParams(queryString);
    return { filter: query.get("$filter"), query, data };
  }

  const loaderFor = (key: LensKey) => (lensEntity(key) === "postings" ? postingsLoader : applicationsLoader);

  for (const ctx of [NINETY, ALL]) {
    describe(`window ${ctx.window}`, () => {
      for (const key of LENS_KEYS) {
        it(`${key}: the list sends exactly the filter the dashboard counted with`, async () => {
          const href = lensListHref(key, ctx);
          const { filter, query } = await listFilter(href, loaderFor(key));

          const countFilter = new URLSearchParams(lensCountQuery(key, ctx)).get("$filter");
          expect(filter).toBe(countFilter);
          expect(filter).toBe(lensFilterString(key, ctx));
          // Paging and the total are the list's own.
          expect(query.get("$top")).toBe("25");
          expect(query.get("$count")).toBe("true");
        });
      }
    });
  }

  it("ANDs the lens with the page's own filters and keeps paging and sorting", async () => {
    const lens = lensFilterString("applied-to", NINETY);
    const { filter, query } = await listFilter(
      `/postings?lens=applied-to&window=90d&asOf=${AS_OF}&disposition=applied&q=Cloud&limit=50&offset=10&sort=title&direction=asc`,
      postingsLoader
    );

    expect(filter).toContain(lens);
    expect(filter).toContain("disposition eq 'applied'");
    expect(filter).toContain("contains(tolower(title),'cloud')");
    expect(query.get("$top")).toBe("50");
    expect(query.get("$skip")).toBe("10");
    expect(query.get("$orderby")).toBe("title asc");
  });

  it("returns the lens for the removable chip", async () => {
    const { data } = await listFilter(`/applications?lens=ghosted&window=all&asOf=${AS_OF}`, applicationsLoader);

    expect(data.lens).toMatchObject({ key: "ghosted", window: "all", asOf: AS_OF });
    expect(String((data.lens as { label: string }).label)).toMatch(/Ghosted/);
  });

  it("returns no lens when none was asked for", async () => {
    const { data, filter } = await listFilter("/postings", postingsLoader);

    expect(data.lens).toBeNull();
    expect(filter).toBeNull();
  });

  it("ignores a lens that belongs to the other list", async () => {
    const onPostings = await listFilter(`/postings?lens=ghosted&window=90d&asOf=${AS_OF}`, postingsLoader);
    expect(onPostings.filter).toBeNull();
    expect(onPostings.data.lens).toBeNull();

    const onApplications = await listFilter(`/applications?lens=leads&window=90d&asOf=${AS_OF}`, applicationsLoader);
    expect(onApplications.filter).toBeNull();
    expect(onApplications.data.lens).toBeNull();
  });

  it("never forwards a raw $filter from the URL", async () => {
    const postings = await listFilter(`/postings?%24filter=${encodeURIComponent("title eq 'x'")}&filter=${encodeURIComponent("1 eq 1")}`, postingsLoader);
    expect(postings.filter).toBeNull();

    const withLens = await listFilter(
      `/applications?lens=open&window=90d&asOf=${AS_OF}&%24filter=${encodeURIComponent("1 eq 1")}`,
      applicationsLoader
    );
    expect(withLens.filter).toBe(lensFilterString("open", NINETY));
    expect(withLens.filter).not.toContain("1 eq 1");
  });

  it("falls back to the default window and today's date for a bad window or asOf instead of forwarding them", async () => {
    const { filter, data } = await listFilter(`/applications?lens=applied&window=forever&asOf=${encodeURIComponent("' or 1 eq 1")}`, applicationsLoader);

    expect(filter).not.toContain("or 1 eq 1");
    expect(data.lens).toMatchObject({ key: "applied", window: "90d" });
    expect(String((data.lens as { asOf: string }).asOf)).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});

describe("Postings 'has application' filter (T043): the postings I applied to", () => {
  const dummyKey = crypto.randomBytes(32);
  const mockFetchOData = vi.fn();

  beforeEach(() => {
    setSessionEncryptionKeyForTesting(dummyKey);
    setOidcConfigForTesting(
      new client.Configuration(
        {
          issuer: "https://identity:5001",
          authorization_endpoint: "https://identity:5001/connect/authorize",
          token_endpoint: "https://identity:5001/connect/token",
          end_session_endpoint: "https://identity:5001/connect/endsession",
        },
        "roleradar-web",
        "test-secret"
      )
    );
    vi.restoreAllMocks();
    mockFetchOData.mockReset();
    mockFetchOData.mockResolvedValue(emptyPage);
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
  });

  async function filterFor(path: string): Promise<{ filter: string | null; data: Record<string, unknown> }> {
    const sessionId = crypto.randomUUID();
    saveSession({
      sessionId,
      ownerSub: "owner-test-001",
      ownerUsername: "fictional-owner",
      accessToken: "mock-access-token-has-app",
      refreshToken: "r",
      idToken: "i",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "c",
    });
    const request = new Request(`https://localhost:5003${path}`, { headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` } });
    const data = (await callLoader(postingsLoader, request)) as Record<string, unknown>;
    return { filter: new URLSearchParams(mockFetchOData.mock.calls[0][1]).get("$filter"), data };
  }

  it("hasApplication=yes sends hasApplication eq true", async () => {
    expect((await filterFor("/postings?hasApplication=yes")).filter).toBe("hasApplication eq true");
  });

  it("hasApplication=no sends hasApplication eq false", async () => {
    expect((await filterFor("/postings?hasApplication=no")).filter).toBe("hasApplication eq false");
  });

  it("ignores any other value", async () => {
    for (const value of ["maybe", "true", "1", "", "yes%20or%201%20eq%201"]) {
      mockFetchOData.mockClear();
      expect((await filterFor(`/postings?hasApplication=${value}`)).filter).toBeNull();
    }
  });

  it("combines with the other filters", async () => {
    const { filter } = await filterFor("/postings?hasApplication=yes&disposition=applied");
    expect(filter).toContain("hasApplication eq true");
    expect(filter).toContain("disposition eq 'applied'");
  });

  it("round-trips the choice to the page so the select and chip can show it", async () => {
    expect((await filterFor("/postings?hasApplication=yes")).data.hasApplication).toBe("yes");
    expect((await filterFor("/postings?hasApplication=no")).data.hasApplication).toBe("no");
    expect((await filterFor("/postings")).data.hasApplication).toBeNull();
  });
});
