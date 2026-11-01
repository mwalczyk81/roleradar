import fs from "node:fs";
import path from "node:path";
import { reactRouter } from "@react-router/dev/vite";
import { defineConfig, loadEnv, type UserConfig } from "vite";

function getEnvDir(): string {
  return process.env.VITE_ENV_DIR ?? (import.meta.dirname ?? path.resolve(process.cwd(), "web"));
}

let envOverrideLogged = false;

export function resetEnvOverrideLoggedForTesting(): void {
  envOverrideLogged = false;
}

export function parseEnvFile(filePath: string): Record<string, string> {
  if (!fs.existsSync(filePath)) return {};
  const content = fs.readFileSync(filePath, "utf8");
  const result: Record<string, string> = {};
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

// Load web/.env.local into process.env before server modules run
export function populateProcessEnv(mode: string, targetEnvDir: string = getEnvDir()): void {
  const env = loadEnv(mode, targetEnvDir, "");
  const localEnvPath = path.resolve(targetEnvDir, ".env.local");
  const localEnv = parseEnvFile(localEnvPath);

  const ignoredShellVars: string[] = [];

  for (const [key, val] of Object.entries(localEnv)) {
    // web/.env.local values must beat inherited shell values in dev
    const shellVal = process.env[key];
    if (shellVal !== undefined && shellVal !== val && shellVal !== "") {
      if (!ignoredShellVars.includes(key)) {
        ignoredShellVars.push(key);
      }
    }
    process.env[key] = val;
  }

  for (const [key, val] of Object.entries(env)) {
    if (!Object.prototype.hasOwnProperty.call(localEnv, key)) {
      if (process.env[key] === undefined || process.env[key] === "") {
        process.env[key] = val;
      }
    }
  }

  if (ignoredShellVars.length > 0 && !envOverrideLogged) {
    envOverrideLogged = true;
    console.info(
      `[dev] Ignored inherited shell environment variable(s) overridden by web/.env.local: ${ignoredShellVars.join(", ")}`
    );
  }
}

// Initial eager load so module imports see process.env
populateProcessEnv(process.env.NODE_ENV || "development");

function resolveCertPath(filePath: string): string {
  if (path.isAbsolute(filePath)) {
    return filePath;
  }
  const cwdResolved = path.resolve(process.cwd(), filePath);
  if (fs.existsSync(cwdResolved)) {
    return cwdResolved;
  }
  const dirResolved = path.resolve(getEnvDir(), filePath);
  if (fs.existsSync(dirResolved)) {
    return dirResolved;
  }
  return cwdResolved;
}

export default defineConfig(({ command, mode }) => {
  populateProcessEnv(mode || process.env.NODE_ENV || "development");

  const certEnv = process.env.HTTPS_CERT_PATH?.trim();
  const keyEnv = process.env.HTTPS_CERT_KEY_PATH?.trim();

  let httpsConfig: { cert: Buffer; key: Buffer } | undefined;

  if (command === "serve") {
    if (!certEnv || !keyEnv) {
      throw new Error(
        "[FATAL] RoleRadar Web dev server requires in-process TLS. " +
          "HTTPS_CERT_PATH and HTTPS_CERT_KEY_PATH must be set in web/.env.local or the environment."
      );
    }

    const resolvedCert = resolveCertPath(certEnv);
    const resolvedKey = resolveCertPath(keyEnv);

    if (!fs.existsSync(resolvedCert) || !fs.existsSync(resolvedKey)) {
      throw new Error(
        `[FATAL] RoleRadar Web dev server TLS certificate or key file not found. ` +
          `Checked cert: '${resolvedCert}', key: '${resolvedKey}'. Ensure dev certificates exist in deploy/certs.`
      );
    }

    httpsConfig = {
      cert: fs.readFileSync(resolvedCert),
      key: fs.readFileSync(resolvedKey),
    };
  } else if (certEnv && keyEnv) {
    const resolvedCert = resolveCertPath(certEnv);
    const resolvedKey = resolveCertPath(keyEnv);
    if (fs.existsSync(resolvedCert) && fs.existsSync(resolvedKey)) {
      httpsConfig = {
        cert: fs.readFileSync(resolvedCert),
        key: fs.readFileSync(resolvedKey),
      };
    }
  }

  const config: UserConfig = {
    plugins: [reactRouter()],
    server: {
      port: 5003,
      ...(httpsConfig ? { https: httpsConfig } : {}),
    },
  };

  return config;
});
