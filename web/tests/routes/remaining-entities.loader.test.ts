import { describe, it, expect, beforeEach, vi } from "vitest";
import crypto from "node:crypto";
import * as client from "openid-client";
import { loader as companiesLoader } from "../../app/routes/companies._index.js";
import { loader as notesLoader } from "../../app/routes/notes._index.js";
import { loader as criteriaLoader } from "../../app/routes/criteria.js";
import { loader as searchRunsLoader } from "../../app/routes/search-runs.js";
import * as apiClientModule from "../../app/lib/api-client.server.js";
import { setOidcConfigForTesting } from "../../app/lib/oidc.server.js";
import { callLoader } from "../test-helpers.js";
import {
  saveSession,
  setSessionEncryptionKeyForTesting,
  SECURE_COOKIE_NAME,
  type UserSession,
} from "../../app/lib/session.server.js";

type AnyLoader = Parameters<typeof callLoader>[0];

interface EntityCase {
  name: string;
  route: string;
  path: string;
  loader: AnyLoader;
  defaultOrder: string;
  sortable: string;
  failureMessage: string;
}

const CASES: EntityCase[] = [
  {
    name: "Companies",
    route: "companies",
    path: "/odata/Companies",
    loader: companiesLoader as unknown as AnyLoader,
    defaultOrder: "$orderby=name asc",
    sortable: "ownershipType",
    failureMessage: "Failed to load companies",
  },
  {
    name: "Notes",
    route: "notes",
    path: "/odata/Notes",
    loader: notesLoader as unknown as AnyLoader,
    defaultOrder: "$orderby=updatedAt desc",
    sortable: "heading",
    failureMessage: "Failed to load notes",
  },
  {
    name: "Criteria",
    route: "criteria",
    path: "/odata/Criteria",
    loader: criteriaLoader as unknown as AnyLoader,
    defaultOrder: "$orderby=name asc",
    sortable: "effectiveTo",
    failureMessage: "Failed to load screening criteria",
  },
  {
    name: "SearchRuns",
    route: "search-runs",
    path: "/odata/SearchRuns",
    loader: searchRunsLoader as unknown as AnyLoader,
    defaultOrder: "$orderby=runDate desc",
    sortable: "newPostings",
    failureMessage: "Failed to load search runs",
  },
];

describe("Remaining entities OData list loaders (T050)", () => {
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
    vi.spyOn(apiClientModule, "fetchOData").mockImplementation(mockFetchOData);
  });

  function createTestSession(): string {
    const sessionId = crypto.randomUUID();
    const session: UserSession = {
      sessionId,
      ownerSub: "owner-test-001",
      ownerUsername: "testowner",
      accessToken: "mock-access-token-remaining",
      refreshToken: "mock-refresh-token-remaining",
      idToken: "mock-id-token-remaining",
      expiresAt: Date.now() + 600_000,
      createdAt: Date.now(),
      csrfToken: "mock-csrf-token-remaining",
    };
    saveSession(session);
    return sessionId;
  }

  function requestFor(route: string, suffix: string, sessionId: string): Request {
    return new Request(`https://localhost:5003/${route}${suffix}`, {
      headers: { Cookie: `${SECURE_COOKIE_NAME}=${sessionId}` },
    });
  }

  function ok(body: unknown = { value: [], "@odata.count": 0 }) {
    mockFetchOData.mockResolvedValueOnce({ data: body, error: undefined, response: { status: 200 } as Response });
  }

  // Runs a loader and returns the result plus the decoded OData query string it sent.
  async function run(testCase: EntityCase, suffix = "", body?: unknown) {
    const sessionId = createTestSession();
    ok(body);
    const result = (await callLoader(testCase.loader, requestFor(testCase.route, suffix, sessionId))) as Record<string, unknown> & {
      items: unknown[];
      totalCount: number;
      limit: number;
      offset: number;
      sort?: string;
      direction?: string;
    };
    const [path, queryString, token] = mockFetchOData.mock.calls[0] as [string, string, string];
    return { result, path, queryString, decoded: decodeURIComponent(queryString), token };
  }

  async function runExpectingThrow(testCase: EntityCase, response: unknown): Promise<Response | null> {
    const sessionId = createTestSession();
    mockFetchOData.mockResolvedValueOnce(response);
    try {
      await callLoader(testCase.loader, requestFor(testCase.route, "", sessionId));
    } catch (err) {
      return err instanceof Response ? err : null;
    }
    return null;
  }

  describe.each(CASES)("$name loader", (testCase) => {
    it("queries the OData entity set with the access token, a count, and the default order", async () => {
      const { path, token, decoded, queryString } = await run(testCase);

      expect(path).toBe(testCase.path);
      expect(token).toBe("mock-access-token-remaining");
      expect(decoded).toContain(testCase.defaultOrder);
      expect(queryString).toContain("$top=25");
      expect(queryString).toContain("$skip=0");
      expect(queryString).toContain("$count=true");
      expect(mockFetchOData).toHaveBeenCalledTimes(1);
    });

    it("clamps limit to 1..100 and offset to >= 0", async () => {
      const { queryString, result } = await run(testCase, "?limit=500&offset=-10");

      expect(queryString).toContain("$top=100");
      expect(queryString).toContain("$skip=0");
      expect(result.limit).toBe(100);
      expect(result.offset).toBe(0);
    });

    it("floors fractional paging values and falls back to defaults for non-numeric or non-positive input", async () => {
      const fractional = await run(testCase, "?limit=12.9&offset=30.4");
      expect(fractional.queryString).toContain("$top=12");
      expect(fractional.queryString).toContain("$skip=30");

      mockFetchOData.mockReset();
      const junk = await run(testCase, "?limit=abc&offset=xyz");
      expect(junk.queryString).toContain("$top=25");
      expect(junk.queryString).toContain("$skip=0");

      mockFetchOData.mockReset();
      const zero = await run(testCase, "?limit=0");
      expect(zero.queryString).toContain("$top=25");
    });

    it("applies an allowlisted sort with the requested direction and defaults the direction to desc", async () => {
      const asc = await run(testCase, `?sort=${testCase.sortable}&direction=asc`);
      expect(asc.decoded).toContain(`$orderby=${testCase.sortable} asc`);
      expect(asc.result.sort).toBe(testCase.sortable);
      expect(asc.result.direction).toBe("asc");

      mockFetchOData.mockReset();
      const desc = await run(testCase, `?sort=${testCase.sortable}&direction=sideways`);
      expect(desc.decoded).toContain(`$orderby=${testCase.sortable} desc`);
      expect(desc.result.direction).toBe("desc");
    });

    it("ignores a sort field that is not on the allowlist and keeps the default order", async () => {
      const { decoded, result } = await run(testCase, "?sort=ownerId&direction=asc");

      expect(decoded).toContain(testCase.defaultOrder);
      expect(decoded).not.toContain("ownerId");
      expect(result.sort).toBeDefined();
      expect(result.sort).not.toBe("ownerId");
    });

    it("never requests $select, note bodies, or an owner filter", async () => {
      const { decoded } = await run(testCase, "?select=content&owner=x");

      expect(decoded).not.toContain("$select");
      expect(decoded).not.toContain("content");
      expect(decoded).not.toContain("owner");
    });

    it("falls back to the row count when the response has no @odata.count", async () => {
      const { result } = await run(testCase, "", { value: [{ id: "a" }, { id: "b" }] });

      expect(result.items).toHaveLength(2);
      expect(result.totalCount).toBe(2);
    });

    it("treats a response with neither value nor count as an empty page", async () => {
      const { result } = await run(testCase, "", {});

      expect(result.items).toEqual([]);
      expect(result.totalCount).toBe(0);
    });

    it("returns the session csrf token and user label for the shell, and never leaks OAuth tokens", async () => {
      const { result } = await run(testCase);

      expect(result.csrfToken).toBe("mock-csrf-token-remaining");
      expect(result.userLabel).toBe("testowner");

      const serialized = JSON.stringify(result);
      expect(serialized).not.toContain("mock-access-token-remaining");
      expect(serialized).not.toContain("mock-refresh-token-remaining");
      expect(serialized).not.toContain("mock-id-token-remaining");
    });

    it("falls back to a generic user label when the session has no username", async () => {
      const sessionId = crypto.randomUUID();
      saveSession({
        sessionId,
        ownerSub: "owner-test-002",
        accessToken: "t",
        refreshToken: "r",
        idToken: "i",
        expiresAt: Date.now() + 600_000,
        createdAt: Date.now(),
        csrfToken: "c",
      } as UserSession);
      ok();

      const result = (await callLoader(testCase.loader, requestFor(testCase.route, "", sessionId))) as { userLabel: string };

      expect(result.userLabel).toBe("Signed in");
    });

    it("re-challenges authentication once on 401", async () => {
      const thrown = await runExpectingThrow(testCase, {
        data: undefined,
        error: "Unauthorized",
        response: { status: 401 } as Response,
      });

      expect(thrown).not.toBeNull();
      expect(thrown?.status).toBe(302);
      expect(thrown?.headers.get("Location")).toContain("/connect/authorize");
      expect(thrown?.headers.get("Set-Cookie")).toBeDefined();
      expect(mockFetchOData).toHaveBeenCalledTimes(1);
    });

    it("redirects to /not-authorized on 403", async () => {
      const thrown = await runExpectingThrow(testCase, {
        data: undefined,
        error: "Forbidden",
        response: { status: 403 } as Response,
      });

      expect(thrown?.status).toBe(302);
      expect(thrown?.headers.get("Location")).toBe("/not-authorized");
    });

    it("throws a Response with the API status and message for other failures", async () => {
      const thrown = await runExpectingThrow(testCase, {
        data: undefined,
        error: { message: "OData exploded" },
        response: { status: 500 } as Response,
      });

      expect(thrown?.status).toBe(500);
      expect(await thrown?.text()).toBe("OData exploded");
    });

    it("uses a generic message and status 500 when the failure carries neither", async () => {
      const thrown = await runExpectingThrow(testCase, {
        data: undefined,
        error: undefined,
        response: { status: 0 } as Response,
      });

      expect(thrown?.status).toBe(500);
      expect(await thrown?.text()).toBe(testCase.failureMessage);
    });
  });

  describe("Companies specifics", () => {
    const companies = CASES[0];

    it("turns q into a case-insensitive contains filter on name", async () => {
      const { decoded, result } = await run(companies, "?q=Acme%20Robotics");

      expect(decoded).toContain("$filter=contains(tolower(name),'acme robotics')");
      expect(result.q).toBe("Acme Robotics");
    });

    it("escapes single quotes in the search term and ignores a blank q", async () => {
      const quoted = await run(companies, "?q=O'Brien");
      expect(quoted.decoded).toContain("contains(tolower(name),'o''brien')");

      mockFetchOData.mockReset();
      const blank = await run(companies, "?q=%20%20");
      expect(blank.decoded).not.toContain("$filter");
      expect(blank.result.q).toBe("");
    });

    it("sorts by status and ownershipType, and keeps search with the sort", async () => {
      const { decoded } = await run(companies, "?q=bank&sort=status&direction=asc");

      expect(decoded).toContain("contains(tolower(name),'bank')");
      expect(decoded).toContain("$orderby=status asc");
    });
  });

  describe("Notes specifics", () => {
    const notes = CASES[1];

    it("lists recent notes with no search term and never asks for content", async () => {
      const { decoded, result } = await run(notes);

      expect(decoded).not.toContain("$filter");
      expect(decoded).toContain("$orderby=updatedAt desc");
      expect(result.q).toBe("");
    });

    it("turns q into a heading contains filter", async () => {
      const { decoded, result } = await run(notes, "?q=Interview%20Prep");

      expect(decoded).toContain("$filter=contains(tolower(heading),'interview prep')");
      expect(result.q).toBe("Interview Prep");
    });

    it("sorts by createdAt, updatedAt and heading but not by content or tags", async () => {
      const created = await run(notes, "?sort=createdAt&direction=asc");
      expect(created.decoded).toContain("$orderby=createdAt asc");

      mockFetchOData.mockReset();
      const content = await run(notes, "?sort=content");
      expect(content.decoded).toContain("$orderby=updatedAt desc");

      mockFetchOData.mockReset();
      const tags = await run(notes, "?sort=tags");
      expect(tags.decoded).toContain("$orderby=updatedAt desc");
    });
  });

  describe("Criteria specifics", () => {
    const criteria = CASES[2];

    it("maps effectiveDate to the effective-interval filter", async () => {
      const { decoded, result } = await run(criteria, "?effectiveDate=2026-10-01");

      expect(decoded).toContain(
        "$filter=(effectiveFrom le 2026-10-01T00:00:00Z) and ((effectiveTo eq null) or (effectiveTo ge 2026-10-01T00:00:00Z))"
      );
      expect(result.effectiveDate).toBe("2026-10-01");
    });

    it("accepts a full ISO timestamp for effectiveDate and normalizes it to UTC", async () => {
      const { decoded } = await run(criteria, "?effectiveDate=2026-10-01T12:30:00Z");

      expect(decoded).toContain("effectiveFrom le 2026-10-01T12:30:00Z");
    });

    it("ignores an invalid effectiveDate instead of forwarding it", async () => {
      const bad = await run(criteria, "?effectiveDate=not-a-date");
      expect(bad.decoded).not.toContain("$filter");
      expect(bad.result.effectiveDate).toBe("");

      mockFetchOData.mockReset();
      const injected = await run(criteria, "?effectiveDate=2026-10-01'%20or%20true");
      expect(injected.decoded).not.toContain("$filter");

      mockFetchOData.mockReset();
      const impossible = await run(criteria, "?effectiveDate=2026-13-45");
      expect(impossible.decoded).not.toContain("$filter");
    });

    it("sends no filter without effectiveDate", async () => {
      const { decoded } = await run(criteria);

      expect(decoded).not.toContain("$filter");
    });
  });

  describe("Search runs specifics", () => {
    const runs = CASES[3];

    it("defaults to newest run first and reports that as the active sort", async () => {
      const { result, decoded } = await run(runs);

      expect(decoded).toContain("$orderby=runDate desc");
      expect(result.sort).toBe("runDate");
      expect(result.direction).toBe("desc");
    });

    it("sorts by runDate ascending when asked", async () => {
      const { decoded } = await run(runs, "?sort=runDate&direction=asc");

      expect(decoded).toContain("$orderby=runDate asc");
    });

    it("does not allow sorting by the unstored counters or the summary", async () => {
      for (const field of ["companiesSearched", "postingsFound", "errorsCount", "summary"]) {
        mockFetchOData.mockReset();
        const { decoded } = await run(runs, `?sort=${field}`);
        expect(decoded).toContain("$orderby=runDate desc");
      }
    });
  });
});
