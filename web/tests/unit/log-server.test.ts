import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { log } from "../../app/lib/log.server";
import { runWithRequestContext } from "../../app/lib/request-context.server";

// Feature 013 US1: web log wiring. info and below go to stdout, warn and above to stderr (contracts/log-records.md).

function captureStreams() {
  const stdout: string[] = [];
  const stderr: string[] = [];
  vi.spyOn(process.stdout, "write").mockImplementation((chunk: string | Uint8Array) => {
    stdout.push(String(chunk));
    return true;
  });
  vi.spyOn(process.stderr, "write").mockImplementation((chunk: string | Uint8Array) => {
    stderr.push(String(chunk));
    return true;
  });
  return { stdout, stderr };
}

describe("log.server", () => {
  const originalEnv = { ...process.env };

  beforeEach(() => {
    process.env = { ...originalEnv };
    delete process.env.ROLERADAR_LOG_FORMAT;
    delete process.env.ROLERADAR_LOG_LEVEL;
  });

  afterEach(() => {
    process.env = { ...originalEnv };
    vi.restoreAllMocks();
  });

  it("sends info and debug to stdout, warn, error and fatal to stderr", () => {
    process.env.ROLERADAR_LOG_LEVEL = "debug";
    const { stdout, stderr } = captureStreams();

    log.debug("d");
    log.info("i");
    log.warn("w");
    log.error("e");
    log.fatal("f");

    expect(stdout.join("")).toMatch(/ debug web \[-\] d\n/);
    expect(stdout.join("")).toMatch(/ info web \[-\] i\n/);
    expect(stdout).toHaveLength(2);
    expect(stderr).toHaveLength(3);
    expect(stderr.join("")).toMatch(/ fatal web \[-\] f\n/);
  });

  it("writes JSON lines when ROLERADAR_LOG_FORMAT=json and text when unset", () => {
    const { stdout } = captureStreams();

    log.info("as text");
    process.env.ROLERADAR_LOG_FORMAT = "json";
    log.info("as json", { event: "Thing", props: { count: 2 } });

    expect(stdout[0]).toMatch(/^\S+Z info web \[-\] as text\n$/);
    const record = JSON.parse(stdout[1]);
    expect(record).toMatchObject({ level: "info", service: "web", msg: "as json", event: "Thing", props: { count: 2 } });
    expect(stdout[1].endsWith("\n")).toBe(true);
  });

  it("filters lower levels with ROLERADAR_LOG_LEVEL and defaults to info", () => {
    const { stdout, stderr } = captureStreams();

    log.debug("hidden by default");
    process.env.ROLERADAR_LOG_LEVEL = "warn";
    log.info("hidden at warn");
    log.warn("shown at warn");

    expect(stdout).toHaveLength(0);
    expect(stderr).toHaveLength(1);
  });

  it("takes the correlation id from the request store and omits it outside a request", async () => {
    process.env.ROLERADAR_LOG_FORMAT = "json";
    const { stdout } = captureStreams();

    await runWithRequestContext({ correlationId: "test-corr-web-0001", nonce: "" }, async () => {
      await Promise.resolve();
      log.info("inside");
    });
    log.info("outside");

    expect(JSON.parse(stdout[0]).correlationId).toBe("test-corr-web-0001");
    expect(JSON.parse(stdout[1]).correlationId).toBeUndefined();
  });

  it("lets an explicit correlation id win, for callbacks that run outside the store", () => {
    process.env.ROLERADAR_LOG_FORMAT = "json";
    const { stdout } = captureStreams();

    log.info("explicit", { correlationId: "test-corr-web-0002" });

    expect(JSON.parse(stdout[0]).correlationId).toBe("test-corr-web-0002");
  });
});
