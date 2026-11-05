import type { ComponentPropsWithoutRef, ReactNode } from "react";
import Markdown, { type Components, type ExtraProps } from "react-markdown";
import remarkBreaks from "remark-breaks";
import remarkGfm from "remark-gfm";
import { MAX_MARKDOWN_BYTES, plainTextReason } from "../lib/markdown-limits.js";
import { remarkNoteStructure } from "../lib/markdown-plugins.js";
import { safeMarkdownUrl } from "../lib/markdown-url.js";

/**
 * Renders a note body as Markdown (ADR 0032). The body is untrusted text (Constitution V, ADR 0026), so this is a
 * locked-down pipeline, not a general renderer:
 *
 *  - react-markdown with remark-gfm and remark-breaks only. No rehype plugin, so raw HTML in a note is never parsed:
 *    it reaches the DOM as visible text. skipHtml stays off on purpose, so the author's content is not silently lost.
 *  - `allowedElements` is the whole vocabulary; `unwrapDisallowed` keeps the text of anything else (a footnote
 *    section, a superscript) instead of dropping it along with the element.
 *  - Every allowed element is rendered by a component below that passes on only the props it needs. Nothing from the
 *    parsed document is spread onto the DOM, so no `id`, `name`, `style`, `class` or `on*` attribute can come from a
 *    note, and nothing can clobber a global (DOM clobbering).
 *  - Links go through `safeMarkdownUrl`: absolute http and https only, `rel="noopener noreferrer"`, same tab.
 *  - Images never become elements. Only the alt text is shown, so nothing is fetched (not even a preload).
 *  - Markdown headings are shifted down two levels (h1 becomes h3, capped at h6): the page owns the h1 and the note
 *    card owns an h2.
 *  - A body over 256 KiB, or one that trips the complexity limits in markdown-limits.ts, is shown as plain
 *    preformatted text so a huge or pathological note cannot stall the server render.
 *
 * Styling is in app.css (`.markdown-*`), tokens only, so the CSP never sees an inline style.
 */

export { MAX_MARKDOWN_BYTES };

type Props<T extends keyof React.JSX.IntrinsicElements> = ComponentPropsWithoutRef<T> & ExtraProps;

const ALLOWED_ELEMENTS = [
  "p",
  "h1",
  "h2",
  "h3",
  "h4",
  "h5",
  "h6",
  "ul",
  "ol",
  "li",
  "blockquote",
  "pre",
  "code",
  "em",
  "strong",
  "del",
  "hr",
  "br",
  "a",
  "img", // allowed only so that its component can show the alt text; it never renders an <img>
  "table",
  "thead",
  "tbody",
  "tr",
  "th",
  "td",
  "input", // GFM task-list checkboxes
];

function hasClass(node: ExtraProps["node"], name: string): boolean {
  const className = node?.properties?.className;
  return Array.isArray(className) && className.includes(name);
}

function alignClass(node: ExtraProps["node"]): string | undefined {
  const align = node?.properties?.align;
  return align === "right" || align === "center" ? `markdown-align-${align}` : undefined;
}

function Paragraph({ children }: Props<"p">) {
  return <p>{children}</p>;
}

function Heading3({ children }: Props<"h1">) {
  return <h3>{children}</h3>;
}
function Heading4({ children }: Props<"h2">) {
  return <h4>{children}</h4>;
}
function Heading5({ children }: Props<"h3">) {
  return <h5>{children}</h5>;
}
function Heading6({ children }: Props<"h4">) {
  return <h6>{children}</h6>;
}

function UnorderedList({ children, node }: Props<"ul">) {
  return <ul className={hasClass(node, "contains-task-list") ? "markdown-task-list" : undefined}>{children}</ul>;
}

function OrderedList({ children, start }: Props<"ol">) {
  return Number.isInteger(start) ? <ol start={start}>{children}</ol> : <ol>{children}</ol>;
}

function ListItem({ children, node }: Props<"li">) {
  return <li className={hasClass(node, "task-list-item") ? "markdown-task-item" : undefined}>{children}</li>;
}

function Blockquote({ children }: Props<"blockquote">) {
  return <blockquote>{children}</blockquote>;
}

function Preformatted({ children }: Props<"pre">) {
  return <pre>{children}</pre>;
}

function Code({ children }: Props<"code">) {
  return <code>{children}</code>;
}

function Emphasis({ children }: Props<"em">) {
  return <em>{children}</em>;
}

function Strong({ children }: Props<"strong">) {
  return <strong>{children}</strong>;
}

function Deleted({ children }: Props<"del">) {
  return <del>{children}</del>;
}

function Rule() {
  return <hr />;
}

function LineBreak() {
  return <br />;
}

function Anchor({ href, children, node }: Props<"a">) {
  // A footnote reference or its back arrow is not a link here: footnotes degrade to plain text.
  if (node?.properties?.dataFootnoteBackref !== undefined) {
    return null;
  }
  if (node?.properties?.dataFootnoteRef !== undefined) {
    return <>[{children}]</>;
  }

  // react-markdown already ran the url through urlTransform; checking again keeps this component safe on its own.
  const safe = typeof href === "string" ? safeMarkdownUrl(href) : null;
  if (safe === null) {
    return <>{children}</>;
  }
  return (
    <a href={safe} rel="noopener noreferrer">
      {children}
    </a>
  );
}

function ImageAltText({ alt }: Props<"img">): ReactNode {
  return alt ? <>{alt}</> : null;
}

function TaskCheckbox({ type, checked }: Props<"input">) {
  if (type !== "checkbox") {
    return null;
  }
  return (
    <input
      type="checkbox"
      className="markdown-task-checkbox"
      checked={checked === true}
      disabled
      readOnly
      aria-label={checked === true ? "Completed task" : "Open task"}
    />
  );
}

function Table({ children, node }: Props<"table">) {
  const index = node?.properties?.dataTableIndex;
  const label = typeof index === "number" ? `Table ${index}` : "Table";
  return (
    <div className="markdown-table-scroll" role="region" aria-label={label} tabIndex={0}>
      <table>{children}</table>
    </div>
  );
}

function TableHead({ children }: Props<"thead">) {
  return <thead>{children}</thead>;
}

function TableBody({ children }: Props<"tbody">) {
  return <tbody>{children}</tbody>;
}

function TableRow({ children }: Props<"tr">) {
  return <tr>{children}</tr>;
}

function TableHeaderCell({ children, node }: Props<"th">) {
  return <th className={alignClass(node)}>{children}</th>;
}

function TableCell({ children, node }: Props<"td">) {
  return <td className={alignClass(node)}>{children}</td>;
}

const COMPONENTS: Components = {
  p: Paragraph,
  h1: Heading3,
  h2: Heading4,
  h3: Heading5,
  h4: Heading6,
  h5: Heading6,
  h6: Heading6,
  ul: UnorderedList,
  ol: OrderedList,
  li: ListItem,
  blockquote: Blockquote,
  pre: Preformatted,
  code: Code,
  em: Emphasis,
  strong: Strong,
  del: Deleted,
  hr: Rule,
  br: LineBreak,
  a: Anchor,
  img: ImageAltText,
  input: TaskCheckbox,
  table: Table,
  thead: TableHead,
  tbody: TableBody,
  tr: TableRow,
  th: TableHeaderCell,
  td: TableCell,
};

const REMARK_PLUGINS = [remarkGfm, remarkBreaks, remarkNoteStructure];

export function MarkdownBody({ content }: { content: string }) {
  const reason = plainTextReason(content);
  if (reason !== null) {
    return (
      <div className="markdown-body markdown-body--plain">
        <div className="markdown-notice" role="status">
          {reason === "size"
            ? `This note is too large to format (over ${MAX_MARKDOWN_BYTES / 1024} KiB). It is shown as plain text.`
            : "This note is too complex to format safely. It is shown as plain text."}
        </div>
        <pre className="markdown-plain">{content}</pre>
      </div>
    );
  }

  return (
    <div className="markdown-body">
      <Markdown
        remarkPlugins={REMARK_PLUGINS}
        allowedElements={ALLOWED_ELEMENTS}
        unwrapDisallowed
        components={COMPONENTS}
        urlTransform={safeMarkdownUrl}
      >
        {content}
      </Markdown>
    </div>
  );
}
