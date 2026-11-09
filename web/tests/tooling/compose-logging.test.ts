import fs from "node:fs";
import path from "node:path";
import { describe, it, expect } from "vitest";
import { parse } from "yaml";

// Feature 013 US1: every service in the real compose files and the opt-in log viewer caps its container log (FR-032,
// SC-010); the viewer is loopback-only, sends no analytics and is pinned (FR-011, FR-028); the demo stack never
// includes it (FR-029). Parsed with the pinned `yaml` dev dependency (research R-014).

const repoRoot = path.resolve(__dirname, "../../..");
const CAPPED_FILES = ["deploy/docker-compose.yml", "deploy/docker-compose.apps.yml", "deploy/docker-compose.logs.yml"] as const;
const LOGS_FILE = "deploy/docker-compose.logs.yml";
const DEMO_FILE = "deploy/demo/docker-compose.yml";

interface ComposeService {
  image?: string;
  ports?: Array<string | { host_ip?: string; published?: string | number; target?: number }>;
  volumes?: Array<string | { source?: string; target?: string; read_only?: boolean }>;
  environment?: Record<string, string | number | boolean | null> | string[];
  logging?: { driver?: string; options?: Record<string, string | number> };
}

interface ComposeFile {
  services?: Record<string, ComposeService>;
}

function load(relative: string): ComposeFile {
  const text = fs.readFileSync(path.join(repoRoot, relative), "utf8");
  return parse(text, { merge: true }) as ComposeFile;
}

function environmentOf(service: ComposeService): Record<string, string> {
  const env = service.environment;
  if (!env) return {};
  if (Array.isArray(env)) {
    return Object.fromEntries(env.map((entry) => {
      const index = entry.indexOf("=");
      return index < 0 ? [entry, ""] : [entry.slice(0, index), entry.slice(index + 1)];
    }));
  }
  return Object.fromEntries(Object.entries(env).map(([key, value]) => [key, String(value ?? "")]));
}

describe("compose log caps and the opt-in viewer", () => {
  it.each(CAPPED_FILES)("%s caps every service's log size and file count", (file) => {
    const services = Object.entries(load(file).services ?? {});
    expect(services.length).toBeGreaterThan(0);

    for (const [name, service] of services) {
      expect(service.logging?.driver, `${file} service '${name}' logging.driver`).toBe("json-file");
      expect(String(service.logging?.options?.["max-size"]), `${file} service '${name}' max-size`).toBe("10m");
      expect(String(service.logging?.options?.["max-file"]), `${file} service '${name}' max-file`).toBe("3");
    }
  });

  it("the demo stack never includes the log viewer", () => {
    const text = fs.readFileSync(path.join(repoRoot, DEMO_FILE), "utf8");

    expect(text.toLowerCase()).not.toContain("dozzle");
    expect(text).not.toContain("docker-compose.logs.yml");
  });

  it("the viewer publishes ports on loopback only", () => {
    const services = Object.entries(load(LOGS_FILE).services ?? {});
    const ports = services.flatMap(([, service]) => service.ports ?? []);
    expect(ports.length).toBeGreaterThan(0);

    for (const port of ports) {
      if (typeof port === "string") {
        expect(port.startsWith("127.0.0.1:"), `port '${port}'`).toBe(true);
      } else {
        expect(port.host_ip).toBe("127.0.0.1");
      }
    }
  });

  it("the viewer disables analytics, mounts the Docker socket read-only and pins its image by digest", () => {
    const services = Object.values(load(LOGS_FILE).services ?? {});
    const viewer = services.find((service) => (service.image ?? "").includes("dozzle"));
    expect(viewer, "a dozzle service").toBeDefined();
    if (!viewer) return;

    // Nothing leaves the machine by default (FR-028): no analytics, no release or registry checks, no avatar fetches.
    const env = environmentOf(viewer);
    expect(env.DOZZLE_NO_ANALYTICS).toBe("true");
    expect(env.DOZZLE_RELEASE_CHECK_MODE).toBe("manual");
    expect(env.DOZZLE_IMAGE_CHECK_MODE).toBe("off");
    expect(env.DOZZLE_DISABLE_AVATARS).toBe("true");

    const socketMounts = (viewer.volumes ?? []).filter((volume) =>
      typeof volume === "string" ? volume.includes("docker.sock") : (volume.source ?? "").includes("docker.sock")
    );
    expect(socketMounts).toHaveLength(1);
    const mount = socketMounts[0];
    if (typeof mount === "string") {
      expect(mount.endsWith(":ro")).toBe(true);
    } else {
      expect(mount.read_only).toBe(true);
    }

    expect(viewer.image).toMatch(/^[\w./-]+:[\w.-]+@sha256:[0-9a-f]{64}$/);
  });
});
