import { describe, it, expect } from "vitest";
import React from "react";
import { render, within } from "@testing-library/react";
import { MarkdownBody, MAX_MARKDOWN_BYTES } from "../../app/components/MarkdownBody.js";
import { MAX_DELIMITERS_PER_BLOCK, MAX_LEADING_RUN, MAX_MARKDOWN_LINES } from "../../app/lib/markdown-limits.js";

/**
 * T081: what a well-behaved note looks like once rendered. Headings are shifted below the page's own h1 and h2,
 * tables sit in a focusable labelled scroll region, task lists are read-only, and a huge body is shown as plain
 * preformatted text so it cannot stall the server render.
 */

function renderBody(content: string) {
  return render(React.createElement(MarkdownBody, { content }));
}

describe("MarkdownBody formatting (T081)", () => {
  describe("headings", () => {
    it("never emits an h1 (the route owns it) and shifts markdown levels down by two, capped at h6", () => {
      const { container } = renderBody("# One\n\n## Two\n\n### Three\n\n#### Four\n\n##### Five\n\n###### Six");

      expect(MAX_MARKDOWN_BYTES).toBe(256 * 1024);
      expect(container.querySelectorAll("h1, h2").length).toBe(0);
      const headings = Array.from(container.querySelectorAll("h1, h2, h3, h4, h5, h6"));
      expect(headings.map((h) => h.tagName)).toEqual(["H3", "H4", "H5", "H6", "H6", "H6"]);
      expect(headings.map((h) => h.textContent)).toEqual(["One", "Two", "Three", "Four", "Five", "Six"]);
    });

    it("keeps inline formatting inside a heading", () => {
      const { container } = renderBody("# A **bold** title");

      const heading = container.querySelector("h3");
      expect(heading?.querySelector("strong")?.textContent).toBe("bold");
    });

    it("never produces a second h1 on a whole page (a markdown h1 plus setext h1)", () => {
      const { container } = renderBody("Setext title\n============\n\n# Atx title");

      expect(container.querySelectorAll("h1").length).toBe(0);
      expect(container.querySelectorAll("h3").length).toBe(2);
    });
  });

  describe("text structure", () => {
    it("renders paragraphs, emphasis, strong, strikethrough, inline code and rules", () => {
      const { container } = renderBody("One *em* and **strong** and ~~gone~~ and `code`.\n\n---\n\nSecond paragraph.");

      expect(container.querySelectorAll("p").length).toBe(2);
      expect(container.querySelector("em")?.textContent).toBe("em");
      expect(container.querySelector("strong")?.textContent).toBe("strong");
      expect(container.querySelector("del")?.textContent).toBe("gone");
      expect(container.querySelector("p code")?.textContent).toBe("code");
      expect(container.querySelectorAll("hr").length).toBe(1);
    });

    it("turns a single newline into a line break", () => {
      const { container } = renderBody("first line\nsecond line");

      const paragraph = container.querySelector("p");
      expect(paragraph?.querySelectorAll("br").length).toBe(1);
      expect(paragraph?.textContent).toBe("first line\nsecond line");
    });

    it("renders block quotes and fenced code blocks, with no language class or highlighter", () => {
      const { container } = renderBody("> quoted *text*\n\n```js\nconst a = 1;\nconst b = '<b>';\n```");

      expect(container.querySelector("blockquote")?.textContent).toContain("quoted text");
      const code = container.querySelector("pre > code");
      expect(code?.textContent).toBe("const a = 1;\nconst b = '<b>';\n");
      expect(code?.hasAttribute("class")).toBe(false);
      expect(container.querySelectorAll("pre b").length).toBe(0);
    });

    it("renders unordered and ordered lists, honouring an ordered list's start number", () => {
      const { container } = renderBody("- apple\n- pear\n  - nested\n\n3. three\n4. four");

      expect(container.querySelectorAll("ul > li").length).toBe(3);
      const ordered = container.querySelector("ol");
      expect(ordered?.getAttribute("start")).toBe("3");
      expect(ordered?.querySelectorAll("li").length).toBe(2);
    });

    it("renders an https autolink literal as a link", () => {
      const { container } = renderBody("see https://example.com/page for more");

      const anchor = container.querySelector("a");
      expect(anchor?.getAttribute("href")).toBe("https://example.com/page");
      expect(anchor?.getAttribute("rel")).toBe("noopener noreferrer");
    });
  });

  describe("task lists", () => {
    it("renders disabled, read-only checkboxes that keep their checked state, name themselves, and take no focus", () => {
      const { container } = renderBody("- [x] shipped\n- [ ] pending");

      const boxes = Array.from(container.querySelectorAll<HTMLInputElement>("input"));
      expect(boxes.length).toBe(2);
      expect(boxes.map((b) => b.type)).toEqual(["checkbox", "checkbox"]);
      expect(boxes.map((b) => b.checked)).toEqual([true, false]);
      for (const box of boxes) {
        expect(box.disabled).toBe(true);
        expect(box.hasAttribute("name")).toBe(false);
        expect(box.getAttribute("aria-label")).toBeTruthy();
        box.focus();
        expect(document.activeElement).not.toBe(box);
      }
      expect(boxes[0].getAttribute("aria-label")).not.toBe(boxes[1].getAttribute("aria-label"));
      expect(container.textContent).toContain("shipped");
      expect(container.textContent).toContain("pending");
    });

    it("renders no input element for anything but a task list item", () => {
      const { container } = renderBody("plain paragraph with [ ] brackets");

      expect(container.querySelectorAll("input").length).toBe(0);
    });
  });

  describe("tables", () => {
    const TABLE = "| Name | Notes |\n| :--- | ---: |\n| Alpha | a very long cell that would otherwise widen the page |\n| Beta | short |";

    it("puts every table in a focusable, labelled, horizontally scrolling region", () => {
      const { container } = renderBody(TABLE);

      const region = container.querySelector(".markdown-table-scroll");
      expect(region).not.toBeNull();
      expect(region?.getAttribute("role")).toBe("region");
      expect(region?.getAttribute("tabindex")).toBe("0");
      expect(region?.getAttribute("aria-label")).toBe("Table 1");
      const table = region?.querySelector("table");
      expect(table).not.toBeNull();
      expect(within(table as HTMLElement).getAllByRole("columnheader").map((h) => h.textContent)).toEqual(["Name", "Notes"]);
      expect(within(table as HTMLElement).getAllByRole("row").length).toBe(3);
      expect(region?.hasAttribute("style")).toBe(false);
    });

    it("numbers the regions so their labels are unique when a note has several tables", () => {
      const { getAllByRole } = renderBody(`${TABLE}\n\nBetween.\n\n${TABLE}\n\n${TABLE}`);

      const labels = getAllByRole("region").map((r) => r.getAttribute("aria-label"));
      expect(labels).toEqual(["Table 1", "Table 2", "Table 3"]);
    });

    it("does not keep the alignment as a style attribute", () => {
      const { container } = renderBody(TABLE);

      expect(container.querySelectorAll("[style], [align]").length).toBe(0);
    });
  });

  describe("size guard", () => {
    it("renders a body over 256 KiB as plain preformatted text with a notice, never as markdown", () => {
      const body = `# not a heading\n\n[not a link](https://ok.example/)\n${"x".repeat(MAX_MARKDOWN_BYTES)}`;
      const { container } = renderBody(body);

      expect(container.querySelectorAll("h1, h2, h3, h4, h5, h6, a, table, p").length).toBe(0);
      const pre = container.querySelector("pre.markdown-plain");
      expect(pre).not.toBeNull();
      expect(pre?.textContent).toBe(body);
      const notice = container.querySelector(".markdown-notice");
      expect(notice?.textContent).toMatch(/too large to format/i);
      expect(notice?.getAttribute("role")).toBe("status");
    });

    it("counts bytes, not characters", () => {
      const twoBytes = "é".repeat(MAX_MARKDOWN_BYTES / 2 + 1);
      const { container } = renderBody(twoBytes);

      expect(twoBytes.length).toBeLessThan(MAX_MARKDOWN_BYTES);
      expect(container.querySelector("pre.markdown-plain")).not.toBeNull();
    });

    it("formats a body of exactly 256 KiB", () => {
      const exactly = `# Title\n\n${"y".repeat(MAX_MARKDOWN_BYTES - "# Title\n\n".length)}`;
      const { container } = renderBody(exactly);

      expect(new TextEncoder().encode(exactly).length).toBe(MAX_MARKDOWN_BYTES);
      expect(container.querySelector("h3")?.textContent).toBe("Title");
      expect(container.querySelector(".markdown-notice")).toBeNull();
    });
  });

  describe("edge cases", () => {
    it("renders an empty body without error", () => {
      const { container } = renderBody("");

      expect(container.querySelector(".markdown-body")).not.toBeNull();
      expect(container.textContent).toBe("");
    });

    it("wraps formatted output in the markdown-body class and writes no inline style anywhere", () => {
      const { container } = renderBody("# T\n\ntext\n\n| a |\n|---|\n| b |\n\n- [x] t");

      expect(container.querySelector(".markdown-body")).not.toBeNull();
      expect(container.querySelectorAll("[style]").length).toBe(0);
    });

  });

  describe("complexity guard: a body that is small but costly to parse is shown as text, never parsed", () => {
    function expectPlain(body: string) {
      const { container } = renderBody(body);

      const pre = container.querySelector("pre.markdown-plain");
      expect(pre, "shown as plain text").not.toBeNull();
      expect(pre?.textContent).toBe(body);
      expect(container.querySelector(".markdown-notice")?.textContent).toMatch(/too complex to format/i);
      expect(container.querySelectorAll("h1, h2, h3, h4, h5, h6, a, table, blockquote, ul, ol, p").length).toBe(0);
    }

    function expectFormatted(body: string) {
      const { container } = renderBody(body);

      expect(container.querySelector(".markdown-notice")).toBeNull();
      expect(container.querySelector("pre.markdown-plain")).toBeNull();
    }

    it("shows thousands of nested block quotes as text instead of overflowing the stack", () => {
      expect(() => renderBody(`${"> ".repeat(2_000)}deep`)).not.toThrow();
      expectPlain(`${"> ".repeat(2_000)}deep`);
    });

    it("shows deeply nested lists as text", () => {
      const nested = Array.from({ length: 200 }, (_, i) => `${" ".repeat(i * 2)}- item`).join("\n");
      expectPlain(nested);
    });

    it("formats nesting up to the limit and not beyond it", () => {
      expectFormatted(`${"> ".repeat(MAX_LEADING_RUN / 2)}deep`);
      expectPlain(`${"> ".repeat(MAX_LEADING_RUN / 2 + 1)}deep`);
    });

    it("shows a block with too many emphasis, link or footnote delimiters as text (quadratic parse cost)", () => {
      expectPlain(`${"*a ".repeat(MAX_DELIMITERS_PER_BLOCK + 1)}deep`);
      expectPlain("[".repeat(MAX_DELIMITERS_PER_BLOCK + 1));
      expectPlain(`${"[^1] ".repeat(MAX_DELIMITERS_PER_BLOCK + 1)}\n\n[^1]: note`);
      expectPlain("~a ".repeat(MAX_DELIMITERS_PER_BLOCK + 1));
      expectPlain("_a ".repeat(MAX_DELIMITERS_PER_BLOCK + 1));
    });

    it("counts delimiters per blank-line-separated block, so a long note of ordinary paragraphs is still formatted", () => {
      const paragraph = "some *emphasis* and a [link](https://ok.example/) here";
      const blocks = Array.from({ length: 1_000 }, () => paragraph).join("\n\n");

      expect(blocks.length).toBeLessThan(MAX_MARKDOWN_BYTES);
      expectFormatted(blocks);
      expectFormatted(`${"*a ".repeat(MAX_DELIMITERS_PER_BLOCK / 2)}\n\n${"*a ".repeat(MAX_DELIMITERS_PER_BLOCK / 2)}`);
    });

    it("shows a body with more than the line limit as text, and never overflows on one huge paragraph of short lines", () => {
      const manyLines = "a\nb\n".repeat(60_000);
      expect(() => renderBody(manyLines)).not.toThrow();
      expectPlain(manyLines);
      expectPlain("x\n".repeat(MAX_MARKDOWN_LINES));
      expectFormatted("x\n".repeat(MAX_MARKDOWN_LINES - 1));
    });

    it("counts CRLF and bare CR line endings too", () => {
      expectPlain("x\r\n".repeat(MAX_MARKDOWN_LINES));
      expectPlain("x\r".repeat(MAX_MARKDOWN_LINES));
    });

    it("is not fooled by a hostile body that uses blank lines or whitespace to hide its delimiters", () => {
      expectPlain(`${"*a \n".repeat(MAX_DELIMITERS_PER_BLOCK + 1)}`);
      expectPlain(`${"*a \n \t \n".repeat(1)}${"*a ".repeat(MAX_DELIMITERS_PER_BLOCK + 1)}`);
    });

    it("keeps an ordinary note well inside every limit", () => {
      const note = "# Interview prep\n\n- Review the **system design** notes\n- [ ] Send thanks\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";
      expectFormatted(note);
    });
  });
});
