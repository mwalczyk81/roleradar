import { describe, it, expect } from "vitest";
import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { render } from "@testing-library/react";
import { MarkdownBody } from "../../app/components/MarkdownBody.js";
import { safeMarkdownUrl } from "../../app/lib/markdown-url.js";

/**
 * T080: a note body is untrusted text (Constitution V, ADR 0026). Whatever the body says, the DOM never gains a script,
 * an image, a frame, a style element, an event handler, an id, or a link that is not an absolute http or https URL.
 */

function renderBody(content: string) {
  return render(React.createElement(MarkdownBody, { content })).container;
}

const FORBIDDEN_ELEMENTS = ["script", "img", "iframe", "style", "link", "object", "embed", "form", "svg", "video", "audio", "source", "base", "meta"];

/** Attributes any element in the output may carry. Anything else, including id, name, style, srcset and on*, fails. */
const ALLOWED_ATTRIBUTES = new Set(["href", "rel", "class", "type", "checked", "disabled", "readonly", "aria-label", "start", "role", "tabindex", "scope"]);

function expectInert(container: HTMLElement) {
  for (const tag of FORBIDDEN_ELEMENTS) {
    expect(container.querySelectorAll(tag).length, `<${tag}> in the DOM`).toBe(0);
  }
  for (const element of Array.from(container.querySelectorAll("*"))) {
    for (const attribute of Array.from(element.attributes)) {
      expect(attribute.name.startsWith("on"), `${element.tagName} has ${attribute.name}`).toBe(false);
      expect(ALLOWED_ATTRIBUTES.has(attribute.name), `${element.tagName} has the attribute ${attribute.name}`).toBe(true);
    }
  }
  for (const anchor of Array.from(container.querySelectorAll("a"))) {
    expect(anchor.getAttribute("href") ?? "").toMatch(/^https?:\/\//);
    expect(anchor.getAttribute("rel")).toBe("noopener noreferrer");
    expect(anchor.hasAttribute("target")).toBe(false);
  }
}

describe("MarkdownBody hostile input (T080)", () => {
  describe("raw HTML is visible inert text, never elements", () => {
    it.each([
      ["a script block", "<script>alert(1)</script>\n\nafter", "<script>alert(1)</script>"],
      ["an inline script", "before <script>alert(2)</script> after", "<script>alert(2)</script>"],
      ["an image with an onerror handler", "x <img src=x onerror=alert(3)> y", "<img src=x onerror=alert(3)>"],
      ["a div with an onclick handler", '<div onclick="steal()">\n\ninside\n\n</div>', '<div onclick="steal()">'],
      ["an iframe", '<iframe src="https://evil.example/frame"></iframe>', '<iframe src="https://evil.example/frame">'],
      ["a style block", "<style>body{display:none}</style>", "<style>body{display:none}</style>"],
      ["an anchor with a javascript href", "<a href='javascript:alert(4)'>Click</a>", "<a href='javascript:alert(4)'>Click</a>"],
      ["an html comment", "before <!-- hidden --> after", "<!-- hidden -->"],
      ["an svg with onload", "<svg onload=alert(5)></svg>", "<svg onload=alert(5)>"],
      ["a form", '<form action="https://evil.example/post"><input name=x></form>', '<form action="https://evil.example/post">'],
    ])("%s", (_name, body, visible) => {
      const container = renderBody(body);

      expectInert(container);
      // The author's content is not silently lost: the markup shows up as text.
      expect(container.textContent).toContain(visible);
    });
  });

  describe("links other than absolute http and https render as plain text", () => {
    it.each([
      ["javascript:", "[x](javascript:alert(1))"],
      ["data:", "[x](data:text/html,<script>alert(1)</script>)"],
      ["vbscript:", "[x](vbscript:msgbox(1))"],
      ["mixed-case JaVaScRiPt:", "[x](JaVaScRiPt:alert(1))"],
      ["leading whitespace before javascript:", "[x](  javascript:alert(1))"],
      ["a tab inside the scheme, as an entity", "[x](java&#x09;script:alert(1))"],
      ["a newline inside the scheme, as an entity", "[x](java&#10;script:alert(1))"],
      ["a numeric entity for the j", "[x](&#106;avascript:alert(1))"],
      ["an angle-bracketed javascript destination", "[x](<javascript:alert(1)>)"],
      ["a protocol-relative url", "[x](//evil.example/track)"],
      ["a root-relative path", "[x](/postings/123)"],
      ["a relative path", "[x](relative/path)"],
      ["a fragment", "[x](#section)"],
      ["mailto:", "[x](mailto:someone@example.com)"],
      ["tel:", "[x](tel:+15550100)"],
      ["ftp:", "[x](ftp://example.com/file)"],
      ["file:", "[x](file:///etc/hosts)"],
      ["an http scheme with no slashes", "[x](https:evil.example)"],
      ["an http scheme with one slash", "[x](http:/evil.example)"],
      ["an http scheme with three slashes", "[x](https:///evil.example)"],
      ["credentials in the url", "[x](https://user:secret@evil.example/)"],
      ["an empty destination", "[x]()"],
    ])("%s", (_name, body) => {
      const container = renderBody(body);

      expect(container.querySelectorAll("a").length).toBe(0);
      expect(container.textContent).toContain("x");
      expectInert(container);
    });

    it("keeps a tab-obfuscated scheme as literal text, with no link", () => {
      const container = renderBody("[x](java\tscript:alert(1))");

      expect(container.querySelectorAll("a").length).toBe(0);
      expectInert(container);
    });

    it("a reference-style definition with a javascript url renders no link", () => {
      const container = renderBody("[x][ref]\n\n[ref]: javascript:alert(1)");

      expect(container.querySelectorAll("a").length).toBe(0);
      expectInert(container);
    });
  });

  describe("allowed links", () => {
    it("renders absolute http and https links with rel noopener noreferrer, in the same tab", () => {
      const container = renderBody(
        "[plain](http://ok.example/a) and [secure](https://ok.example/b?q=1#frag) and <https://ok.example/c> and https://ok.example/d"
      );

      const anchors = Array.from(container.querySelectorAll("a"));
      expect(anchors.map((a) => a.getAttribute("href"))).toEqual([
        "http://ok.example/a",
        "https://ok.example/b?q=1#frag",
        "https://ok.example/c",
        "https://ok.example/d",
      ]);
      for (const anchor of anchors) {
        expect(anchor.getAttribute("rel")).toBe("noopener noreferrer");
        expect(anchor.hasAttribute("target")).toBe(false);
      }
      expectInert(container);
    });

    it("accepts an upper-case scheme and surrounding whitespace, and writes the normalised url", () => {
      const container = renderBody("[x](  HTTPS://OK.EXAMPLE/Path  )");

      const anchor = container.querySelector("a");
      expect(anchor?.getAttribute("href")).toBe("https://ok.example/Path");
      expect(anchor?.textContent).toBe("x");
    });
  });

  describe("autolink literals: only an explicit http or https url becomes a link", () => {
    it("renders a bare www. address as plain text, because it has no scheme of its own", () => {
      const container = renderBody("visit www.example.com now");

      expect(container.querySelectorAll("a").length).toBe(0);
      expect(container.textContent).toContain("visit www.example.com now");
    });

    it("renders an email address as plain text", () => {
      const container = renderBody("write to someone@example.com today");

      expect(container.querySelectorAll("a").length).toBe(0);
      expect(container.textContent).toContain("write to someone@example.com today");
    });

    it("keeps a link the author wrote explicitly, even when its text is a www. address", () => {
      const container = renderBody("[www.example.com](https://www.example.com/)");

      const anchor = container.querySelector("a");
      expect(anchor?.getAttribute("href")).toBe("https://www.example.com/");
      expect(anchor?.textContent).toBe("www.example.com");
    });

    it("keeps an explicit http literal", () => {
      const container = renderBody("see http://www.example.com/page");

      expect(container.querySelector("a")?.getAttribute("href")).toBe("http://www.example.com/page");
    });
  });

  describe("images never render an element", () => {
    it("renders only the alt text for image syntax", () => {
      const container = renderBody("![alt text](https://img.example/a.png)");

      expect(container.querySelectorAll("img").length).toBe(0);
      expect(container.querySelectorAll("[src], [srcset]").length).toBe(0);
      expect(container.textContent).toContain("alt text");
      expect(container.textContent).not.toContain("img.example");
      expectInert(container);
    });

    it("renders only the alt text for a reference-style image", () => {
      const container = renderBody("![the logo][r]\n\n[r]: https://img.example/b.png");

      expect(container.querySelectorAll("img").length).toBe(0);
      expect(container.textContent).toContain("the logo");
    });

    it("renders nothing for an image with no alt text", () => {
      const container = renderBody("before ![](https://img.example/c.png) after");

      expect(container.querySelectorAll("img").length).toBe(0);
      expect(container.textContent).toContain("before");
      expect(container.textContent).toContain("after");
    });

    it("keeps the link when an image is wrapped in one, with the alt text as its label", () => {
      const container = renderBody("[![logo](https://img.example/l.png)](https://ok.example/home)");

      expect(container.querySelectorAll("img").length).toBe(0);
      const anchor = container.querySelector("a");
      expect(anchor?.getAttribute("href")).toBe("https://ok.example/home");
      expect(anchor?.textContent).toBe("logo");
    });

    it("emits no resource hint to the server-rendered markup either (React hoists a preload for any img)", () => {
      const html = renderToStaticMarkup(
        React.createElement(MarkdownBody, { content: "![t](https://img.example/pixel.gif)\n\n![u][r]\n\n[r]: https://img.example/u.png" })
      );

      expect(html).not.toMatch(/<img/i);
      expect(html).not.toMatch(/<link/i);
      expect(html).not.toContain("preload");
      expect(html).not.toContain("img.example");
    });
  });

  describe("no ids or names, so nothing can clobber the page (DOM clobbering)", () => {
    it("emits no id, name or fragment link from headings, footnotes or anything else", () => {
      const container = renderBody(
        "# Heading\n\n## Another\n\nText with a footnote[^1] and a name.\n\n[^1]: The note body.\n\n- [x] done"
      );

      expect(container.querySelectorAll("[id]").length).toBe(0);
      expect(container.querySelectorAll("[name]").length).toBe(0);
      expect(container.querySelectorAll('a[href^="#"]').length).toBe(0);
      expectInert(container);
    });

    it("cannot be used to shadow a global with an id such as 'location' or 'cookie'", () => {
      const container = renderBody('<a id="cookie" href="x">a</a>\n\n<form name="location"></form>\n\n# cookie');

      expect(container.querySelectorAll("[id], [name]").length).toBe(0);
      expectInert(container);
    });
  });

  describe("footnotes degrade to plain text and an element the allowlist lacks keeps its text", () => {
    it("keeps the reference and the definition as plain text (unwrapDisallowed), with no section, sup or link", () => {
      const container = renderBody("Claim[^1] stands.\n\n[^1]: Source: the fictional handbook.");

      expect(container.querySelectorAll("section, sup").length).toBe(0);
      expect(container.querySelectorAll("a").length).toBe(0);
      expect(container.textContent).toContain("Claim[1] stands.");
      expect(container.textContent).toContain("Source: the fictional handbook.");
      expect(container.textContent).not.toContain("↩");
      expectInert(container);
    });
  });

  describe("safeMarkdownUrl (the urlTransform)", () => {
    it.each([
      ["javascript:alert(1)"],
      ["  JavaScript:alert(1)"],
      ["JAVASCRIPT:alert(1)"],
      ["java\tscript:alert(1)"],
      ["java\nscript:alert(1)"],
      ["java\rscript:alert(1)"],
      ["java\u0000script:alert(1)"],
      ["\u0001https://ok.example/"],
      ["https://ok.example/\u0007"],
      ["https://ok.example/a b"],
      [`https://ok.example/a${String.fromCharCode(0xa0)}b`],
      [`https://ok.example/a${String.fromCharCode(0x2028)}b`],
      ["https://ok.example/a\\b"],
      ["data:text/html,<script>alert(1)</script>"],
      ["vbscript:msgbox(1)"],
      ["//evil.example/a"],
      ["///evil.example/a"],
      ["\\\\evil.example\\a"],
      ["/relative"],
      ["relative/path"],
      ["./relative"],
      ["../relative"],
      ["#fragment"],
      ["?query=1"],
      ["mailto:someone@example.com"],
      ["tel:+15550100"],
      ["ftp://example.com/f"],
      ["file:///etc/hosts"],
      ["https:evil.example"],
      ["http:/evil.example"],
      ["https:///evil.example"],
      ["https://"],
      ["https://user@evil.example/"],
      ["https://user:pw@evil.example/"],
      [""],
      ["   "],
    ])("rejects %j", (url) => {
      expect(safeMarkdownUrl(url)).toBeNull();
    });

    it.each([
      ["http://ok.example/a", "http://ok.example/a"],
      ["https://ok.example/a?b=1&c=2#d", "https://ok.example/a?b=1&c=2#d"],
      ["  https://ok.example/a  ", "https://ok.example/a"],
      ["HTTPS://OK.EXAMPLE/a", "https://ok.example/a"],
      ["HtTp://ok.example:8080/x", "http://ok.example:8080/x"],
      ["https://[::1]:8443/x", "https://[::1]:8443/x"],
    ])("accepts %j as %j", (url, expected) => {
      expect(safeMarkdownUrl(url)).toBe(expected);
    });
  });
});
