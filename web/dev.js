/* global process, console */
import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const webDir = import.meta.dirname ?? process.cwd();

function parseEnvFile(filePath) {
  if (!fs.existsSync(filePath)) return {};
  const content = fs.readFileSync(filePath, "utf8");
  const result = {};
  for (const line of content.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith("#")) continue;
    const eqIdx = trimmed.indexOf("=");
    if (eqIdx > 0) {
      const key = trimmed.slice(0, eqIdx).trim();
      let val = trimmed.slice(eqIdx + 1).trim();
      if ((val.startsWith('"') && val.endsWith('"')) || (val.startsWith("'") && val.endsWith("'"))) {
        val = val.slice(1, -1);
      }
      result[key] = val;
    }
  }
  return result;
}

function resolveCert() {
  const localEnv = parseEnvFile(path.resolve(webDir, ".env.local"));
  const certCandidate =
    process.env.NODE_EXTRA_CA_CERTS ||
    localEnv.NODE_EXTRA_CA_CERTS ||
    process.env.HTTPS_CERT_PATH ||
    localEnv.HTTPS_CERT_PATH ||
    "../deploy/certs/dev-cert.crt";

  const resolved = path.isAbsolute(certCandidate)
    ? certCandidate
    : path.resolve(webDir, certCandidate);

  if (fs.existsSync(resolved)) {
    return resolved;
  }
  return null;
}

const certPath = resolveCert();
const env = { ...process.env };
if (certPath) {
  env.NODE_EXTRA_CA_CERTS = certPath;
  console.info(`[dev] Set NODE_EXTRA_CA_CERTS from cert path before Node starts: ${certPath}`);
}

const isWindows = process.platform === "win32";
const npmCmd = isWindows ? "pnpm.cmd" : "pnpm";

const child = spawn(npmCmd, ["exec", "react-router", "dev"], {
  stdio: "inherit",
  env,
  shell: true,
  cwd: webDir,
});

child.on("exit", (code) => {
  process.exit(code ?? 0);
});
