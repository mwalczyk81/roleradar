import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import {
  LENS_KEYS,
  COVERAGE_KEYS,
  lensEntity,
  coverageEntity,
  lensFilterString,
  coverageFilterString,
  type LensWindow,
} from "../../app/lib/insights-lenses.js";

/**
 * contracts/insights-lenses.json is the shared contract between the web lens module and the .NET oracle test:
 * `filters` (written here) are the exact $filter strings the module emits for a fixed asOf, `expected` (written by the
 * .NET parity test from its independent oracle) are the counts they must return over the fictional seed.
 * Regenerate only the `filters` section with UPDATE_SNAPSHOTS=1; never hand-edit.
 */
const SNAPSHOT_PATH = path.resolve(process.cwd(), "..", "contracts", "insights-lenses.json");
const WINDOWS: LensWindow[] = ["90d", "all"];

interface Entry {
  entity: string;
  filters: Record<string, string>;
  expected?: Record<string, Record<string, number>>;
}

interface Snapshot {
  asOf: string;
  lenses: Record<string, Entry>;
  coverage: Record<string, Entry>;
}

function build(asOf: string, previous?: Snapshot): Snapshot {
  const lenses: Record<string, Entry> = {};
  for (const key of LENS_KEYS) {
    lenses[key] = {
      entity: lensEntity(key),
      filters: Object.fromEntries(WINDOWS.map((w) => [w, lensFilterString(key, { window: w, asOf })])),
      ...(previous?.lenses[key]?.expected ? { expected: previous.lenses[key].expected } : {}),
    };
  }
  const coverage: Record<string, Entry> = {};
  for (const key of COVERAGE_KEYS) {
    coverage[key] = {
      entity: coverageEntity(key),
      filters: Object.fromEntries(WINDOWS.map((w) => [w, coverageFilterString(key, { window: w, asOf })])),
      ...(previous?.coverage[key]?.expected ? { expected: previous.coverage[key].expected } : {}),
    };
  }
  return { asOf, lenses, coverage };
}

function read(): Snapshot | undefined {
  return fs.existsSync(SNAPSHOT_PATH) ? (JSON.parse(fs.readFileSync(SNAPSHOT_PATH, "utf8")) as Snapshot) : undefined;
}

describe("contracts/insights-lenses.json", () => {
  it("equals the filters the lens module emits (drift in the module or the contract fails)", () => {
    const existing = read();

    if (process.env.UPDATE_SNAPSHOTS === "1") {
      const next = build(existing?.asOf ?? "2026-09-30", existing);
      fs.writeFileSync(SNAPSHOT_PATH, JSON.stringify(next, null, 2) + "\n");
    }

    const snapshot = read();
    expect(snapshot, "contracts/insights-lenses.json is missing; run once with UPDATE_SNAPSHOTS=1").toBeDefined();
    if (!snapshot) return;

    expect(snapshot.asOf).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(Object.keys(snapshot.lenses).sort()).toEqual([...LENS_KEYS].sort());
    expect(Object.keys(snapshot.coverage).sort()).toEqual([...COVERAGE_KEYS].sort());

    const rebuilt = build(snapshot.asOf, snapshot);
    for (const key of LENS_KEYS) {
      expect(snapshot.lenses[key].entity, `${key} entity`).toBe(rebuilt.lenses[key].entity);
      expect(snapshot.lenses[key].filters, `${key} filters`).toEqual(rebuilt.lenses[key].filters);
    }
    for (const key of COVERAGE_KEYS) {
      expect(snapshot.coverage[key].entity, `${key} entity`).toBe(rebuilt.coverage[key].entity);
      expect(snapshot.coverage[key].filters, `${key} filters`).toEqual(rebuilt.coverage[key].filters);
    }
  });

  it("uses a fictional fixed asOf and carries no real data", () => {
    const snapshot = read();
    if (!snapshot) return;
    expect(snapshot.asOf).toBe("2026-09-30");
    const text = JSON.stringify(snapshot);
    expect(text).not.toMatch(/@|https?:\/\//);
  });
});
