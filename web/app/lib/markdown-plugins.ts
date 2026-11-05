/**
 * The one remark transform used for note bodies (ADR 0032). It is a remark plugin (it works on the Markdown tree), not a
 * rehype plugin, and it never parses or emits HTML.
 *
 * It does two things:
 *  1. A bare `www.example.com` is recognised by remark-gfm as an "autolink literal" and given the URL
 *     `http://www.example.com`. The author wrote no scheme and we would be inventing a non-TLS one, so such a link is
 *     replaced by its text. A link the author wrote on purpose (`[www.example.com](https://...)`, `<https://...>`,
 *     `https://...`) is untouched: only a link whose source text itself starts with `www.` is unwrapped.
 *  2. Every table gets a 1-based number, so each scroll region can carry a unique accessible name ("Table 1").
 *
 * The node shape below is the small part of mdast this needs; the full type package is not a direct dependency.
 */

interface MdNode {
  type: string;
  children?: MdNode[];
  position?: { start: { offset?: number | undefined } };
  data?: { hProperties?: Record<string, unknown> };
}

function isBareWwwLink(node: MdNode, source: string): boolean {
  if (node.type !== "link") {
    return false;
  }
  const offset = node.position?.start.offset;
  return typeof offset === "number" && source.slice(offset, offset + 4).toLowerCase() === "www.";
}

export function remarkNoteStructure() {
  return (tree: MdNode, file: { value: unknown }): void => {
    const source = typeof file.value === "string" ? file.value : "";
    let tableCount = 0;

    const visit = (node: MdNode): void => {
      if (node.type === "table") {
        tableCount += 1;
        node.data = { ...node.data, hProperties: { ...node.data?.hProperties, dataTableIndex: tableCount } };
      }

      if (node.children) {
        node.children = node.children.flatMap((child) => (isBareWwwLink(child, source) ? (child.children ?? []) : [child]));
        for (const child of node.children) {
          visit(child);
        }
      }
    };

    visit(tree);
  };
}
