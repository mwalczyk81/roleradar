import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";

/**
 * T064 (web half): the committed contracts/odata/metadata.xml is the only description of the OData surface, and the
 * web code must be built against it. Every entity set and property name that a route uses for $select, $filter,
 * $orderby or its row type has to exist in that file. A name that is in the web code but not in the XML fails here.
 *
 * The .NET side (ODataMetadataDriftTests) proves the XML equals what the running API serves, so together the web
 * code, the contract file and the service cannot drift apart.
 *
 * ODATA_METADATA_PATH points the test at another copy of the file (used to demonstrate that a renamed property
 * is caught).
 */

const REPO_ROOT = path.resolve(__dirname, "../../..");
const ROUTES_DIR = path.resolve(__dirname, "../../app/routes");
const METADATA_PATH = process.env.ODATA_METADATA_PATH ?? path.join(REPO_ROOT, "contracts/odata/metadata.xml");

type Contract = Map<string, Set<string>>; // entity set -> property names

export function parseContract(xml: string): Contract {
  const typeProperties = new Map<string, Set<string>>();
  for (const match of xml.matchAll(/<EntityType Name="([^"]+)">([\s\S]*?)<\/EntityType>/g)) {
    const props = new Set<string>();
    for (const prop of match[2].matchAll(/<Property Name="([^"]+)"/g)) {
      props.add(prop[1]);
    }
    typeProperties.set(match[1], props);
  }

  const contract: Contract = new Map();
  for (const set of xml.matchAll(/<EntitySet Name="([^"]+)" EntityType="[^"]*?([^."]+)"/g)) {
    contract.set(set[1], typeProperties.get(set[2]) ?? new Set());
  }
  return contract;
}

export interface RouteUsage {
  file: string;
  sets: string[];
  names: string[];
}

function quoted(list: string): string[] {
  return [...list.matchAll(/"([A-Za-z][A-Za-z0-9]*)"/g)].map((m) => m[1]);
}

/** Entity sets and property names a route source uses against the OData API. */
export function readRouteUsage(file: string, source: string): RouteUsage {
  const sets = [...new Set([...source.matchAll(/"\/odata\/([A-Za-z]+)"/g)].map((m) => m[1]))];
  const names = new Set<string>();

  for (const m of source.matchAll(/\bfield: "([A-Za-z][A-Za-z0-9]*)"/g)) names.add(m[1]);
  for (const m of source.matchAll(/\bselect: \[([^\]]*)\]/g)) quoted(m[1]).forEach((n) => names.add(n));
  for (const m of source.matchAll(/ALLOWED_SORT_FIELDS = \[([^\]]*)\]/g)) quoted(m[1]).forEach((n) => names.add(n));
  for (const m of source.matchAll(/containsAny\(\[([^\]]*)\]/g)) quoted(m[1]).forEach((n) => names.add(n));
  for (const iface of source.matchAll(/export interface \w+ODataModel \{([\s\S]*?)\n\}/g)) {
    for (const prop of iface[1].matchAll(/^\s+([A-Za-z][A-Za-z0-9]*)\??:/gm)) names.add(prop[1]);
  }

  return { file, sets, names: [...names].sort() };
}

export function findDrift(contract: Contract, usages: RouteUsage[]): string[] {
  const problems: string[] = [];
  for (const usage of usages) {
    if (usage.sets.length !== 1) {
      problems.push(`${usage.file}: expected exactly one OData entity set, found [${usage.sets.join(", ")}]`);
      continue;
    }
    const [set] = usage.sets;
    const properties = contract.get(set);
    if (!properties) {
      problems.push(`${usage.file}: entity set '${set}' is not in the metadata`);
      continue;
    }
    for (const name of usage.names) {
      if (!properties.has(name)) {
        problems.push(`${usage.file}: '${name}' is not a property of ${set} in the metadata`);
      }
    }
  }
  return problems;
}

function loadRouteUsages(): RouteUsage[] {
  return fs
    .readdirSync(ROUTES_DIR)
    .filter((f) => f.endsWith(".tsx") || f.endsWith(".ts"))
    .map((f) => ({ file: f, source: fs.readFileSync(path.join(ROUTES_DIR, f), "utf8") }))
    .filter(({ source }) => source.includes('"/odata/'))
    .map(({ file, source }) => readRouteUsage(file, source));
}

describe("OData metadata contract vs web code (T064)", () => {
  const xml = fs.readFileSync(METADATA_PATH, "utf8");
  const contract = parseContract(xml);
  const usages = loadRouteUsages();

  it("reads the six entity sets from the committed metadata", () => {
    expect([...contract.keys()].sort()).toEqual(
      ["Applications", "Companies", "Criteria", "Notes", "Postings", "SearchRuns"]
    );
    for (const [, properties] of contract) {
      expect(properties.size).toBeGreaterThan(3);
    }
  });

  it("finds every web route that talks to OData, so a new route cannot skip the check", () => {
    const files = usages.map((u) => u.file).sort();
    expect(files).toEqual(
      [
        "applications._index.tsx",
        "companies._index.tsx",
        "criteria.tsx",
        "notes._index.tsx",
        "postings.$id.tsx",
        "postings._index.tsx",
        "search-runs.tsx",
      ]
    );
    for (const usage of usages) {
      expect(usage.names.length, `${usage.file} should use at least one property`).toBeGreaterThan(0);
    }
  });

  it("uses only entity sets and property names that exist in contracts/odata/metadata.xml", () => {
    expect(findDrift(contract, usages)).toEqual([]);
  });

  it("detects a property that is renamed in the metadata (self-check of the drift rule)", () => {
    const renamed = parseContract(xml.replace('<Property Name="newPostings"', '<Property Name="newPostingz"'));
    const problems = findDrift(renamed, usages);
    expect(problems.some((p) => p.includes("'newPostings'") && p.includes("SearchRuns"))).toBe(true);
  });

  it("detects a web name that the metadata does not have", () => {
    const withRogue = usages.map((u) => (u.file === "criteria.tsx" ? { ...u, names: [...u.names, "weight"] } : u));
    expect(findDrift(contract, withRogue)).toContain("criteria.tsx: 'weight' is not a property of Criteria in the metadata");
  });
});
