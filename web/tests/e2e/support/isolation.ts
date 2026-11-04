/**
 * Isolation guard for the Insights e2e specs (T050). They run only against a loopback stub API that serves the
 * fictional fixture, never the developer's stack and never a real database. These checks fail closed.
 */

const LOOPBACK_HOSTS = new Set(["127.0.0.1", "localhost", "[::1]", "::1"]);

export function assertLoopbackStub(apiBaseUrl: string | undefined, stubPort: number): void {
  if (!apiBaseUrl || apiBaseUrl.trim() === "") {
    throw new Error("Insights e2e isolation: ROLERADAR_API_BASE_URL is not set; it must point at the loopback stub.");
  }

  let url: URL;
  try {
    url = new URL(apiBaseUrl);
  } catch {
    throw new Error("Insights e2e isolation: ROLERADAR_API_BASE_URL is not a valid URL.");
  }

  if (!LOOPBACK_HOSTS.has(url.hostname)) {
    // Deliberately does not echo the host: it could identify a real server.
    throw new Error("Insights e2e isolation: the API base URL is not a loopback address; refusing to run.");
  }

  const port = url.port === "" ? (url.protocol === "https:" ? 443 : 80) : Number(url.port);
  if (port !== stubPort) {
    throw new Error("Insights e2e isolation: the API base URL does not point at this run's fictional stub port; refusing to run.");
  }
}

export function assertNotRealStackProject(projectName: string): void {
  if (projectName === "real-stack") {
    throw new Error("Insights e2e isolation: Insights specs must never run in the real-stack project.");
  }
}
