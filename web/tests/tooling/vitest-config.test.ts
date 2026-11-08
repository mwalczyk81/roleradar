import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import path from "node:path";

// Feature 011, story 2b. The Vitest lane reaches the combined Allure report as Vitest's own JUnit file, like the xUnit v3
// lanes, not through an Allure adapter: the adapter wraps every expect call in a report step, which made assertion-heavy
// tests about 35 times slower (research.md D15). With ALLURE_RESULTS_DIR set Vitest writes <dir>/Vitest.xml; unset, an
// ordinary run changes nothing. No file of the Vitest configuration may name allure-vitest.

type ConfigShape = { test?: { setupFiles?: unknown[]; reporters?: unknown[] } };

async function loadConfig(resultsDir: string | undefined): Promise<ConfigShape> {
  vi.resetModules();
  if (resultsDir === undefined) {
    delete process.env["ALLURE_RESULTS_DIR"];
  } else {
    process.env["ALLURE_RESULTS_DIR"] = resultsDir;
  }
  const loaded = (await import("../../vitest.config")) as { default: ConfigShape };
  return loaded.default;
}

describe("the Vitest configuration", () => {
  const original = process.env["ALLURE_RESULTS_DIR"];

  beforeEach(() => {
    vi.resetModules();
  });

  afterEach(() => {
    if (original === undefined) {
      delete process.env["ALLURE_RESULTS_DIR"];
    } else {
      process.env["ALLURE_RESULTS_DIR"] = original;
    }
  });

  it("writes Vitest.xml with the junit reporter into ALLURE_RESULTS_DIR, next to the default reporter", async () => {
    const dir = path.join("fictional", "allure-input", "vitest");
    const config = await loadConfig(dir);

    expect(config.test?.reporters).toEqual(["default", ["junit", { outputFile: path.join(dir, "Vitest.xml") }]]);
  });

  it("does not load an Allure adapter or its setup file, with or without the results folder", async () => {
    for (const dir of [path.join("fictional", "out"), undefined]) {
      const config = await loadConfig(dir);
      const text = JSON.stringify(config.test);

      expect(text, String(dir)).not.toContain("allure");
      expect(config.test?.setupFiles, String(dir)).toEqual(["./tests/setup.ts"]);
    }
  });

  it("uses only the default reporter when ALLURE_RESULTS_DIR is not set", async () => {
    const config = await loadConfig(undefined);

    expect(config.test?.reporters).toEqual(["default"]);
  });
});
