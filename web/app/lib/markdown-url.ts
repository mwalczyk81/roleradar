/**
 * The only gate a URL from a note body passes through (ADR 0032). Returns the normalised URL when it is an absolute
 * http or https URL, and null for everything else, so the caller renders plain text instead of a link.
 *
 * Rejected, after trimming: any scheme other than http and https (javascript:, data:, vbscript:, mailto:, tel:, ftp:,
 * file: ...), in any letter case; control characters, whitespace and backslashes anywhere (so java<TAB>script: and
 * entity-obfuscated schemes cannot slip through); protocol-relative //host; relative paths, fragments and queries;
 * a scheme without exactly two slashes before the host (https:host, https:/host, https:///host); and credentials in
 * the URL (https://user:pw@host).
 */

// C0 and C1 control characters, backslash, any Unicode whitespace, and the invisible format characters that can hide
// inside a scheme or host (zero-width spaces and joiners, bidi marks and overrides, word joiner, BOM).
// eslint-disable-next-line no-control-regex -- matching control characters is the point
const FORBIDDEN_CHARACTERS = /[\u0000-\u001f\u007f-\u009f\\\s\u200b-\u200f\u202a-\u202e\u2060-\u2064\ufeff]/;

// http or https (any case), then exactly "//", then a host that does not start with another slash, query or fragment.
const ABSOLUTE_HTTP = /^https?:\/\/[^/?#]/i;

export function safeMarkdownUrl(raw: string): string | null {
  const url = raw.trim();
  if (url.length === 0) {
    return null;
  }
  if (FORBIDDEN_CHARACTERS.test(url)) {
    return null;
  }
  if (!ABSOLUTE_HTTP.test(url)) {
    return null;
  }

  let parsed: URL;
  try {
    parsed = new URL(url);
  } catch {
    return null;
  }

  if (parsed.protocol !== "http:" && parsed.protocol !== "https:") {
    return null;
  }
  if (parsed.hostname === "" || parsed.username !== "" || parsed.password !== "") {
    return null;
  }

  return parsed.href;
}
