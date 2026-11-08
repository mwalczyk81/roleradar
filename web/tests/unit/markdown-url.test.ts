import { describe, it, expect } from "vitest";
import { safeMarkdownUrl } from "../../app/lib/markdown-url.js";

// The only gate a URL from a note body passes through (ADR 0032). Fictional hosts only (example.test).
describe("safeMarkdownUrl", () => {
  it("returns the normalised href for absolute http and https URLs, in any scheme case", () => {
    expect(safeMarkdownUrl("https://example.test")).toBe("https://example.test/");
    expect(safeMarkdownUrl("  http://example.test/a?b=1#c  ")).toBe("http://example.test/a?b=1#c");
    expect(safeMarkdownUrl("HTTPS://Example.TEST/Path")).toBe("https://example.test/Path");
  });

  it("rejects an empty or whitespace-only value", () => {
    expect(safeMarkdownUrl("")).toBeNull();
    expect(safeMarkdownUrl("   ")).toBeNull();
  });

  it("rejects other schemes, protocol-relative, relative and malformed scheme forms", () => {
    for (const raw of [
      "javascript:alert(1)",
      "JaVaScRiPt:alert(1)",
      "data:text/html,<b>x</b>",
      "mailto:someone@example.test",
      "ftp://example.test/file",
      "//example.test/x",
      "/relative/path",
      "#fragment",
      "?query=1",
      "https:example.test",
      "https:/example.test",
      "https:///example.test",
    ]) {
      expect(safeMarkdownUrl(raw), raw).toBeNull();
    }
  });

  it("rejects control characters, whitespace inside the URL and backslashes", () => {
    for (const raw of ["https://exa mple.test", "https://example.test/\tx", "https://example.test\\x", "https://example.test/\u200bx"]) {
      expect(safeMarkdownUrl(raw), JSON.stringify(raw)).toBeNull();
    }
  });

  it("rejects credentials in the URL, whether user, password or both", () => {
    expect(safeMarkdownUrl("https://user:pw@example.test/")).toBeNull();
    expect(safeMarkdownUrl("https://user@example.test/")).toBeNull();
    expect(safeMarkdownUrl("https://:pw@example.test/")).toBeNull();
  });

  it("rejects a URL the platform cannot parse instead of throwing", () => {
    expect(safeMarkdownUrl("https://[")).toBeNull();
  });
});
