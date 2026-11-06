import https from "node:https";
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { AsyncLocalStorage } from "node:async_hooks";
import { pathToFileURL } from "node:url";

const requestContextStorage = new AsyncLocalStorage<{ nonce: string }>();
import { createRequestListener } from "@react-router/node";

export function validateSecretsOrExit(): void {
  if (process.env.NODE_ENV !== "development" && process.env.NODE_ENV !== "test") {
    const missing: string[] = [];
    const encKey = process.env.SESSION_ENCRYPTION_KEY;
    if (!encKey || encKey.trim().length === 0) {
      missing.push("SESSION_ENCRYPTION_KEY");
    } else {
      try {
        const buf = Buffer.from(encKey.trim(), "base64");
        if (buf.length !== 32) {
          missing.push("SESSION_ENCRYPTION_KEY (must be 32 bytes base64)");
        }
      } catch {
        missing.push("SESSION_ENCRYPTION_KEY (invalid base64)");
      }
    }

    const clientSecret = process.env.ROLERADAR_WEB_CLIENT_SECRET;
    if (!clientSecret || clientSecret.trim().length === 0) {
      missing.push("ROLERADAR_WEB_CLIENT_SECRET");
    }

    if (missing.length > 0) {
      console.error(
        `[FATAL] RoleRadar Web refused to start: missing required secrets (${missing.join(
          ", "
        )}).`
      );
      process.exit(1);
    }
  }
}

export function generateCspNonce(): string {
  return crypto.randomBytes(16).toString("base64");
}

export function applySecurityHeaders(
  res: http.ServerResponse,
  nonce: string
): void {
  res.setHeader("X-Content-Type-Options", "nosniff");
  res.setHeader("X-Frame-Options", "DENY");
  res.setHeader("Referrer-Policy", "strict-origin-when-cross-origin");
  res.setHeader(
    "Content-Security-Policy",
    `default-src 'self'; script-src 'self' 'nonce-${nonce}'; style-src 'self' 'nonce-${nonce}'; img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self' https://identity:5001 https://localhost:5001; frame-ancestors 'none';`
  );
}

function resolveCertPath(
  configuredPath: string | undefined,
  defaultFilename: string
): string {
  if (configuredPath && configuredPath.trim().length > 0) {
    if (fs.existsSync(configuredPath)) return configuredPath;
    const resolved = path.resolve(process.cwd(), configuredPath);
    if (fs.existsSync(resolved)) return resolved;
    return configuredPath;
  }

  const candidates = [
    path.resolve(process.cwd(), "deploy/certs", defaultFilename),
    path.resolve(process.cwd(), "../deploy/certs", defaultFilename),
    path.resolve(process.cwd(), "certs", defaultFilename),
  ];

  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }
  return defaultFilename;
}

let requestListener: ReturnType<typeof createRequestListener> | null = null;

export async function getRequestListener(): Promise<
  ReturnType<typeof createRequestListener>
> {
  if (!requestListener) {
    const candidates = [
      path.resolve(process.cwd(), "build/server/index.js"),
      path.resolve(process.cwd(), "web/build/server/index.js"),
      ...(import.meta.dirname
        ? [
            path.resolve(import.meta.dirname, "build/server/index.js"),
            path.resolve(import.meta.dirname, "../build/server/index.js"),
          ]
        : []),
    ];
    const target = candidates.find((c) => fs.existsSync(c)) ?? candidates[0];
    const build = await import(pathToFileURL(target).href);
    requestListener = createRequestListener({
      build,
      getLoadContext: (req) => {
        const store = requestContextStorage.getStore();
        return {
          nonce: store?.nonce ?? req.headers.get("x-csp-nonce") ?? undefined,
        };
      },
    });
  }
  return requestListener;
}

function serveStaticFile(reqUrl: string, res: http.ServerResponse): boolean {
  const cleanUrl = reqUrl.split("?")[0];
  const clientDirs = [
    path.resolve(process.cwd(), "build/client"),
    path.resolve(process.cwd(), "web/build/client"),
    ...(import.meta.dirname
      ? [
          path.resolve(import.meta.dirname, "build/client"),
          path.resolve(import.meta.dirname, "../build/client"),
        ]
      : []),
  ];

  for (const clientDir of clientDirs) {
    if (!fs.existsSync(clientDir)) continue;
    const filePath = path.join(clientDir, cleanUrl);
    if (!filePath.startsWith(clientDir)) continue;

    if (fs.existsSync(filePath) && fs.statSync(filePath).isFile()) {
      const ext = path.extname(filePath).toLowerCase();
      const contentTypes: Record<string, string> = {
        ".js": "text/javascript",
        ".mjs": "text/javascript",
        ".css": "text/css",
        ".json": "application/json",
        ".woff2": "font/woff2",
        ".woff": "font/woff",
        ".svg": "image/svg+xml",
        ".png": "image/png",
        ".ico": "image/x-icon",
      };

      const contentType = contentTypes[ext] ?? "application/octet-stream";
      res.writeHead(200, {
        "Content-Type": contentType,
        "Cache-Control": cleanUrl.startsWith("/assets/")
          ? "public, max-age=31536000, immutable"
          : "public, max-age=3600",
      });
      fs.createReadStream(filePath).pipe(res);
      return true;
    }
  }
  return false;
}

export function createRequestHandler() {
  return async (req: http.IncomingMessage, res: http.ServerResponse) => {
    if (req.url === "/health") {
      res.writeHead(200, { "Content-Type": "application/json" });
      res.end(JSON.stringify({ status: "ok" }));
      return;
    }

    if (req.url && serveStaticFile(req.url, res)) {
      return;
    }

    if (req.url === "/favicon.ico") {
      res.writeHead(204);
      res.end();
      return;
    }

    const nonce = generateCspNonce();
    applySecurityHeaders(res, nonce);
    req.headers["x-csp-nonce"] = nonce;

    requestContextStorage.run({ nonce }, async () => {
      try {
        const listener = await getRequestListener();
        listener(req, res);
      } catch (err) {
        console.error("[ERROR] Failed to handle web request:", err);
        res.writeHead(500, { "Content-Type": "text/plain" });
        res.end("Internal Server Error");
      }
    });
  };
}

export function createWebServer(): https.Server | http.Server {
  validateSecretsOrExit();

  const certFile = resolveCertPath(process.env.HTTPS_CERT_PATH, "dev-cert.crt");
  const keyFile = resolveCertPath(process.env.HTTPS_CERT_KEY_PATH, "dev-cert.key");
  const handler = createRequestHandler();

  if (fs.existsSync(certFile) && fs.existsSync(keyFile)) {
    const tlsOptions = {
      cert: fs.readFileSync(certFile),
      key: fs.readFileSync(keyFile),
    };
    return https.createServer(tlsOptions, handler);
  }

  if (process.env.NODE_ENV !== "development" && process.env.NODE_ENV !== "test") {
    console.error(
      `[FATAL] RoleRadar Web refused to start: TLS certificate ('${certFile}') or key ('${keyFile}') not found. Production requires TLS.`
    );
    process.exit(1);
  }

  console.warn(
    `[WARN] TLS certificates not found at '${certFile}' and '${keyFile}'. Falling back to HTTP.`
  );
  return http.createServer(handler);
}

export function startServer(port: number = 5003): void {
  const server = createWebServer();
  server.listen(port, "0.0.0.0", () => {
    console.error(`[INFO] RoleRadar Web listening on port ${port}`);
  });
}

// Auto-start if executed directly
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  startServer(Number(process.env.PORT ?? 5003));
}
