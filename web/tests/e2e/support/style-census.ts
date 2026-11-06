/**
 * Style attribute census helper (009 phase 1). Finds the elements in a piece of server-rendered HTML that carry a
 * `style` attribute, so each conversion phase can show how many remain on a route. Pure string work: no DOM, no
 * browser, so it can be unit tested. It looks at tags only, so text that merely contains `style="..."` is ignored.
 */

const TAG = /<([a-zA-Z][a-zA-Z0-9-]*)((?:\s+[^\s>"'=/]+(?:\s*=\s*(?:"[^"]*"|'[^']*'|[^\s>"']+))?)*)\s*\/?>/g;

/** Each element with a style attribute, as `tag.firstClass`, or `tag` when it has no class. In document order. */
export function styleAttributeElements(html: string): string[] {
  const out: string[] = [];
  for (const match of html.matchAll(TAG)) {
    const attrs = match[2] ?? "";
    if (!/(?:^|\s)style\s*=/.test(attrs)) continue;
    const firstClass = /(?:^|\s)class\s*=\s*"([^"]*)"/.exec(attrs)?.[1]?.trim().split(/\s+/)[0];
    out.push(firstClass ? `${match[1]}.${firstClass}` : String(match[1]));
  }
  return out;
}
