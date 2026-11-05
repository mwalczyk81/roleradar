/**
 * When a note body is shown as plain text instead of Markdown (ADR 0032).
 *
 * Size alone does not bound the cost of parsing Markdown. Measured on this build, inside 256 KiB: 500 levels of nested
 * list took 2.3 s, 80 KB of "[" took 1.6 s, 6 KB of nested emphasis took 0.5 s (quadratic), 35,000 "* item" lines took
 * 5.3 s, and 60,000 lines in one paragraph overflowed the stack. The parser runs on the single Node event loop, so one
 * hostile or pasted note could stall every request. This check is a single linear pass over the text, and it falls back
 * to the same plain view as an oversized body:
 *
 *  - more than MAX_MARKDOWN_BYTES of UTF-8,
 *  - more than MAX_MARKDOWN_LINES lines,
 *  - a line that starts with more than MAX_LEADING_RUN spaces, tabs and ">" (deeply nested quotes or lists),
 *  - more than MAX_DELIMITERS_PER_BLOCK of "*", "_", "~" and "[" in one blank-line-separated block (emphasis, links and
 *    footnote references, whose cost is quadratic in how many there are).
 *
 * Real notes are far below every limit. A note that trips one is still shown in full, as text.
 */

export const MAX_MARKDOWN_BYTES = 256 * 1024;
export const MAX_MARKDOWN_LINES = 10_000;
export const MAX_LEADING_RUN = 128;
export const MAX_DELIMITERS_PER_BLOCK = 2_000;

export type PlainTextReason = "size" | "complexity";

const LF = 10;
const CR = 13;
const TAB = 9;
const SPACE = 32;
const GREATER_THAN = 62;
const ASTERISK = 42;
const UNDERSCORE = 95;
const TILDE = 126;
const OPEN_BRACKET = 91;

function isOverSizeLimit(text: string): boolean {
  // UTF-8 uses at least one and at most three bytes per UTF-16 unit: only encode when the answer is not certain.
  if (text.length > MAX_MARKDOWN_BYTES) return true;
  if (text.length * 3 <= MAX_MARKDOWN_BYTES) return false;
  return new TextEncoder().encode(text).length > MAX_MARKDOWN_BYTES;
}

function isTooComplex(text: string): boolean {
  let lines = 1;
  let delimiters = 0;
  let leadingRun = 0;
  let inLeading = true;
  let lineHasContent = false;

  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);

    // A line ends at LF, at CRLF (counted once, at the LF) or at a lone CR.
    if (code === LF || (code === CR && text.charCodeAt(i + 1) !== LF)) {
      lines++;
      if (lines > MAX_MARKDOWN_LINES) return true;
      if (!lineHasContent) delimiters = 0; // a blank line ends the block
      lineHasContent = false;
      inLeading = true;
      leadingRun = 0;
      continue;
    }
    if (code === CR) continue;

    if (inLeading) {
      if (code === SPACE || code === TAB || code === GREATER_THAN) {
        leadingRun++;
        if (leadingRun > MAX_LEADING_RUN) return true;
        if (code === GREATER_THAN) lineHasContent = true;
        continue;
      }
      inLeading = false;
    }

    lineHasContent = true;
    if (code === ASTERISK || code === UNDERSCORE || code === TILDE || code === OPEN_BRACKET) {
      delimiters++;
      if (delimiters > MAX_DELIMITERS_PER_BLOCK) return true;
    }
  }

  return false;
}

/** Why this body must be shown as plain text, or null when it is safe to format. */
export function plainTextReason(text: string): PlainTextReason | null {
  if (isOverSizeLimit(text)) return "size";
  if (isTooComplex(text)) return "complexity";
  return null;
}
