import { describe, it, expect, afterEach, vi } from "vitest";
import { fetchOData } from "../../app/lib/api-client.server.js";

describe("fetchOData abort signal", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("passes an optional AbortSignal through to fetch so a slow request can be cancelled", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ value: [], "@odata.count": 3 }), { status: 200, headers: { "Content-Type": "application/json" } })
    );
    vi.stubGlobal("fetch", fetchMock);
    const controller = new AbortController();

    const result = await fetchOData<unknown>("/odata/Postings", "$top=0&$count=true", "token", controller.signal);

    expect(result.data?.["@odata.count"]).toBe(3);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    // Feature 013: fetchOData sends a Request (the correlation header is added in one place); its signal follows ours.
    const request: Request = fetchMock.mock.calls[0][0];
    expect(request.signal.aborted).toBe(false);
    controller.abort();
    expect(request.signal.aborted).toBe(true);
  });

  it("sends no signal when none is given, as before", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ value: [] }), { status: 200, headers: { "Content-Type": "application/json" } })
    );
    vi.stubGlobal("fetch", fetchMock);

    await fetchOData<unknown>("/odata/Postings", "$top=1", "token");

    const request: Request = fetchMock.mock.calls[0][0];
    expect(request.signal.aborted).toBe(false);
  });
});
