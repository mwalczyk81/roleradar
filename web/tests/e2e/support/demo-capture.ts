/**
 * Shared rules for the screenshot spec (feature 012): the fixed file set, the target guard and the credential source.
 * Pure functions, so a Vitest file can pin them without a browser.
 */

/** Host port of the demo web app (deploy/demo/docker-compose.yml). The real stack uses 5003. */
export const DEMO_PORT = 5103;

export const SCREENSHOT_VIEWS = [
  "insights",
  "insights-drilldown",
  "postings-list",
  "posting-detail",
  "application-timeline",
  "company",
  "note",
] as const;
export type ScreenshotView = (typeof SCREENSHOT_VIEWS)[number];

export const THEMES = ["light", "dark"] as const;
export type Theme = (typeof THEMES)[number];

export const SCREENSHOT_NAMES: readonly string[] = SCREENSHOT_VIEWS.flatMap((view) => THEMES.map((theme) => `${view}-${theme}.png`));

/**
 * docs/images/manifest.json: the expected files, the README hero image, and the hero records the capture opens (the
 * seed's showcase company, posting and note, and the Insights lens the drill-down follows).
 */
export interface ScreenshotManifest {
  images: string[];
  hero: {
    image: { view: string; light: string; dark: string };
    records: { company: string; posting: string; note: string; drilldown: string };
  };
  temporary?: { file: string; reason: string }[];
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasStrings<K extends string>(value: unknown, keys: readonly K[]): value is Record<K, string> {
  return isRecord(value) && keys.every((key) => typeof value[key] === "string" && value[key] !== "");
}

function isTemporaryList(value: unknown): value is { file: string; reason: string }[] {
  return Array.isArray(value) && value.every((entry: unknown) => hasStrings(entry, ["file", "reason"] as const));
}

/** Parses the manifest text, refusing any other shape rather than guessing. */
export function parseScreenshotManifest(text: string): ScreenshotManifest {
  const parsed: unknown = JSON.parse(text);
  if (
    !isRecord(parsed) ||
    !Array.isArray(parsed.images) ||
    !parsed.images.every((entry: unknown) => typeof entry === "string") ||
    !isRecord(parsed.hero) ||
    !hasStrings(parsed.hero.image, ["view", "light", "dark"] as const) ||
    !hasStrings(parsed.hero.records, ["company", "posting", "note", "drilldown"] as const) ||
    (parsed.temporary !== undefined && !isTemporaryList(parsed.temporary))
  ) {
    throw new Error("docs/images/manifest.json does not have the expected shape.");
  }
  const { image, records } = parsed.hero;
  return {
    images: parsed.images.filter((entry: unknown): entry is string => typeof entry === "string"),
    hero: {
      image: { view: image.view, light: image.light, dark: image.dark },
      records: { company: records.company, posting: records.posting, note: records.note, drilldown: records.drilldown },
    },
    temporary: isTemporaryList(parsed.temporary) ? parsed.temporary : undefined,
  };
}

/**
 * Refuses anything that is not the demo web origin: https, host `localhost` or a `*.localhost` name, port 5103.
 * Deliberately does not echo the rejected value.
 */
export function assertDemoTarget(target: string | undefined): URL {
  let url: URL;
  try {
    url = new URL(target ?? "");
  } catch {
    throw new Error("Screenshot capture refused: ROLERADAR_DEMO_URL is not a valid demo address.");
  }

  const host = url.hostname.toLowerCase();
  const localhostName = host === "localhost" || host.endsWith(".localhost");
  if (url.protocol !== "https:" || !localhostName || url.port !== String(DEMO_PORT)) {
    throw new Error(`Screenshot capture refused: the target is not the demo web app (https on localhost port ${DEMO_PORT}).`);
  }

  return url;
}

export interface DemoCredentials {
  username: string;
  password: string;
  totpSecret: string;
}

/** Reads the demo credentials from the environment. Names the missing variables, never their values. */
export function readDemoCredentials(env: Record<string, string | undefined>): DemoCredentials {
  const username = env.ROLERADAR_DEMO_USERNAME;
  const password = env.ROLERADAR_DEMO_PASSWORD;
  const totpSecret = env.ROLERADAR_DEMO_TOTP_SECRET;
  if (!username || !password || !totpSecret) {
    const missing = [
      username ? null : "ROLERADAR_DEMO_USERNAME",
      password ? null : "ROLERADAR_DEMO_PASSWORD",
      totpSecret ? null : "ROLERADAR_DEMO_TOTP_SECRET",
    ].filter((name): name is string => name !== null);
    throw new Error(`Screenshot capture needs these environment variables: ${missing.join(", ")}.`);
  }
  return { username, password, totpSecret };
}
