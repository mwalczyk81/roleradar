import { describe, expect, it } from "vitest";
import { mapApiError } from "../../app/lib/api-client.server";

// Feature 014 US2 (RR-64 absorbed, contracts/problem-details.md): mapApiError reads an RFC 9457 problem document.
// Field errors are `errors` (field -> issues), the code is the lower-case `errorCode`, the message is `detail`, and
// `traceId` is carried through for logging. The web never switches on `type`.

describe("mapApiError reads problem documents", () => {
  it("(a) turns errors into one inline message per field, joining several issues with a space", () => {
    const result = mapApiError(
      {
        status: 400,
        errorCode: "validation_error",
        detail: "x",
        errors: { title: ["too long"], company: ["required", "unknown"] },
      },
      400
    );

    expect(result.fieldErrors).toEqual({ title: "too long", company: "required unknown" });
    expect(result.errorCode).toBe("validation_error");
    expect(result.message).toBe("x");
  });

  it("(b) derives isConflict from the lower-case code, whatever the status", () => {
    expect(mapApiError({ errorCode: "concurrency_conflict", detail: "stale" }).isConflict).toBe(true);
  });

  it("(b) derives isNotFound from the lower-case code", () => {
    expect(mapApiError({ errorCode: "not_found", detail: "gone" }).isNotFound).toBe(true);
  });

  it("(b) derives isForbidden from the lower-case code", () => {
    expect(mapApiError({ errorCode: "forbidden" }).isForbidden).toBe(true);
  });

  it("(b) the upper-case literals no longer matter", () => {
    const result = mapApiError({ errorCode: "CONCURRENCY_CONFLICT" });

    expect(result.isConflict).toBe(false);
    expect(result.isNotFound).toBe(false);
    expect(result.isForbidden).toBe(false);
  });

  it("(b) status 409, 404 and 403 alone also set the flags", () => {
    expect(mapApiError(undefined, 409).isConflict).toBe(true);
    expect(mapApiError(undefined, 404).isNotFound).toBe(true);
    expect(mapApiError(undefined, 403).isForbidden).toBe(true);
    expect(mapApiError(undefined, 400)).toMatchObject({ isConflict: false, isNotFound: false, isForbidden: false });
  });

  it("takes the message from detail", () => {
    expect(mapApiError({ errorCode: "not_found", detail: "Posting not found." }, 404).message).toBe("Posting not found.");
  });

  it("with no body the code is HTTP_<status> and the message is the generic text", () => {
    const result = mapApiError(undefined, 502);

    expect(result.errorCode).toBe("HTTP_502");
    expect(result.message).toBe("An unexpected API error occurred.");
    expect(result.fieldErrors).toEqual({});
    expect(result.traceId).toBeUndefined();
  });

  it("with no body and no status the code is UNKNOWN_ERROR", () => {
    expect(mapApiError(undefined).errorCode).toBe("UNKNOWN_ERROR");
  });

  it("carries the traceId through for logging, only when it is a string", () => {
    expect(mapApiError({ errorCode: "internal_error", traceId: "0af7651916cd43dd8448eb211c80319c" }, 500).traceId).toBe(
      "0af7651916cd43dd8448eb211c80319c"
    );
    expect(mapApiError({ errorCode: "internal_error", traceId: 42 }, 500).traceId).toBeUndefined();
  });

  it("ignores the old envelope members: details[].message is not read", () => {
    const result = mapApiError({ ok: false, errorCode: "validation_error", message: "old", details: [{ field: "title", message: "old" }] }, 400);

    expect(result.fieldErrors).toEqual({});
    expect(result.message).toBe("An unexpected API error occurred.");
  });

  it("ignores malformed errors members without throwing", () => {
    expect(mapApiError({ errors: "nope" }, 400).fieldErrors).toEqual({});
    expect(mapApiError({ errors: { title: "not-an-array", company: [1, "ok"] } }, 400).fieldErrors).toEqual({ company: "ok" });
  });
});
