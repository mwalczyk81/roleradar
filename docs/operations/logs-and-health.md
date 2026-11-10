# Logs, trace ids and health

How to read what the RoleRadar services say, follow one request across them, and tell whether they are healthy. It covers the web app, the API, Identity, the CLI and the MCP host. The decisions behind it are in [ADR 0041](../adr/0041-operability-logging-correlation-health.md). Every id, name and address below is fictional.

## Reading logs

There are three ways, from simplest to most convenient. All three read the same output; the apps need no log sink and nothing leaves the machine.

1. **`docker compose logs`.** From the repository root, for the real stack:

   ```bash
   docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps logs --tail 100 -f api
   ```

   Replace `api` with `web`, `identity` or `mongo`, or leave the service out to follow all of them. The shipped images write JSON, one object per line.
2. **Text mode outside compose.** When you run a host yourself (`dotnet run --project src/RoleRadar.Api`, the web dev server, the CLI), `ROLERADAR_LOG_FORMAT` is unset and the output is one readable line per record:

   ```text
   2026-11-09T14:03:22.418Z warn api [01970f3e-5b1c-7d2a-9a77-3c2f1e0d4b5a] Command TransitionPostingCommand returned failure NotFound in 12.4 ms
   ```

   The fields are time (UTC), level, service, the trace id in brackets (`-` outside a request), then the message and its `key=value` properties.
3. **The opt-in viewer.** A small log viewer (Dozzle) that you start deliberately and that is never part of the demo:

   ```bash
   docker compose -f deploy/docker-compose.logs.yml up -d
   ```

   Open `http://127.0.0.1:5004`. It shows the containers of the real stack with live search and a level filter. Stop it with `docker compose -f deploy/docker-compose.logs.yml down`. It is bound to loopback, mounts the Docker socket read-only, and has analytics, release checks, registry checks and avatar fetches turned off. The trade-off of that socket mount is in the [threat model](../threat-model.md).

## Following one request by trace id

Every request has a W3C trace id: 32 lower-case hexadecimal characters. The web app starts it (or honours a valid inbound `traceparent` header) and sends a `traceparent` header to the API and to Identity's server-side endpoints; the API and Identity use the same trace id in their own records. The same value is the `traceId` on every log record written while the request was handled, the `traceId` in every API error body (a problem document, see [API problem types](../api/problems.md)), and the `request_correlation_id` on every audit entry the request wrote. There is no `X-Correlation-ID` header any more, and no response header returns the id: it reaches you in the error body, in the logs and in the audit trail. See [ADR 0044](../adr/0044-problem-details-and-trace-context.md).

To follow one request, take the id and search for it:

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.apps.yml --profile apps logs --no-color | grep 0af7651916cd43dd8448eb211c80319c
```

In the viewer, paste the id into the search box. You get the web record, the API records and, for a sign-in, the Identity records for that one request. This is the id only: there are no spans and no exporter yet (feature 015 adds them).

The CLI uses one trace id for the whole run and the MCP host one per tool call. Browser navigations to Identity get Identity's own trace.

Where do you get the id from? From the `traceId` member of an API error body, from the `req=` column of `roleradar audit list`, from the `ref:` shown when a tracker import fails, or from the warn record the web writes when the API answers 503. Audit entries written before feature 014 keep their old-format values in the same column.

## Format and levels

| Setting | Applies to | Values |
| --- | --- | --- |
| `ROLERADAR_LOG_FORMAT` | web, API, Identity, CLI, MCP | `json` or `text`. Unset means `text`. The shipped Dockerfiles set `json`. |
| `Logging__LogLevel__<Category>` | API, Identity, MCP (.NET) | The standard .NET level for a category, for example `Logging__LogLevel__Default=Debug` or `Logging__LogLevel__RoleRadar.Application=Debug`. Categories you do not set default to `Information`, with `Microsoft` and `System` at `Warning`. |
| `ROLERADAR_LOG_LEVEL` | web | `trace`, `debug`, `info`, `warn`, `error` or `fatal`. Default `info`. |
| `ROLERADAR_LOG_LEVEL` and `--verbose` | CLI | The level variable names a .NET level. Without either, the CLI logs `Warning` and above, so `status` and `import --dry-run` stay quiet; `--verbose` (or `-v`) raises it to `Information`. |

Streams: the API, Identity and web write `info` and below to stdout and `warn` and above to stderr; compose shows both. The CLI and MCP host write to stderr only, because stdout is reserved for command output and MCP frames.

Health requests log at `Debug`, so the compose healthchecks do not fill the log at the default level.

### The JSON record

One object per line:

```json
{"ts":"2026-11-09T14:03:22.418Z","level":"warn","service":"api","category":"RoleRadar.Api.Observability.ApiExceptionHandler","msg":"The data store did not answer; the request was answered with 503","eventId":6002,"event":"DatastoreUnavailable","traceId":"0af7651916cd43dd8448eb211c80319c"}
```

`ts`, `level`, `service` and `msg` are always present. `category` and `eventId` appear for .NET services. `event`, `traceId` (inside a request, a CLI run or an MCP call), `props` (structured properties) and `error` (`type`, `message` and `stack`, each scrubbed) appear when there is something to say. Each request writes one `RequestCompleted` record with the method, the path without its query string, the status and the duration.

### Sign-in events

Identity writes four security events to the log, one per outcome, each with the request's trace id:

| Event | Level | Carries |
| --- | --- | --- |
| `SignInSucceeded` | info | the account's subject id |
| `SignInFailed` | warn | the subject id only when the typed username matched an account |
| `AccountLockedOut` | warn | the subject id |
| `OneTimeCodeFailed` | warn | the subject id |

A failed attempt that also locks the account writes `SignInFailed` (or `OneTimeCodeFailed`) and then `AccountLockedOut`. The typed username is never logged. Sign-in history lasts only as long as the capped container logs.

## Log caps

Every service in the real compose files (`deploy/docker-compose.yml` and `deploy/docker-compose.apps.yml`) and the viewer's own file uses the `json-file` driver capped at 10 MB per file and 3 files, so each service keeps at most about 30 MB of log. Older lines are discarded. The viewer reads the same files, so it shows no more than that either.

## Health endpoints

The API and Identity serve three anonymous endpoints. The bodies are the minimal status document and nothing else: no host, endpoint, error text or remediation command.

| Endpoint | What it checks | Healthy | Unhealthy |
| --- | --- | --- | --- |
| `GET /health/live` | Nothing: the process answers requests. | `200 {"status":"Healthy"}` | not applicable |
| `GET /health/ready` | API: Mongo answers as a writable primary. Identity: its SQLite database can be reached. Each check is bounded at 2 s. | `200 {"status":"Healthy"}` | `503 {"status":"Unhealthy"}` |
| `GET /health` | Alias of `/health/live`, kept for compatibility. | `200 {"status":"Healthy"}` | not applicable |

The images' built-in check (`--healthcheck`, used by the compose healthchecks) calls `ROLERADAR_HEALTHCHECK_URL`, which now defaults to `http://127.0.0.1:8080/health/ready`. A container whose store is down therefore shows as `unhealthy` in `docker compose ps`, and goes back to `healthy` by itself when the store returns. The web app's own `/health` is unchanged: static, liveness only.

## What a 503 means

When the API cannot reach Mongo, or Mongo does not answer in time, a request is answered within about 5 seconds with `503` and `Retry-After: 5`, not left hanging:

```json
{
  "type": "https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#service_unavailable",
  "title": "Service Unavailable",
  "status": 503,
  "detail": "The data store is not reachable. Try again shortly.",
  "errorCode": "service_unavailable",
  "traceId": "0af7651916cd43dd8448eb211c80319c"
}
```

It means the data store is down, paused or stalled, not that the request was wrong. Nothing needs restarting: the driver reconnects on its own and the next request after the store is back succeeds. Look at the store first (`docker compose ps`, then the `mongo` logs), then use the `traceId` to find the API's record of the failed request. Any other unhandled failure is a `500` with `errorCode` `internal_error` and the same fixed kind of problem document. Exception text, stack traces and connection details never reach a response; they go only to the scrubbed log.

In the web app a 503 shows a "Temporarily unavailable" page with a Try again link (or the same friendly message on a form). A real 503 from the API carries `Retry-After: 5`, which is more than the web is willing to wait (2 seconds), so the web does not retry it and shows the page at once. Reads are still retried up to twice after a network error, a timeout, a 502 or 504, or a 503 that carries no `Retry-After` or a short one; writes are sent once and never retried, so a retry can never repeat a change ([ADR 0045](../adr/0045-web-retry-and-limiter-libraries.md)). The web writes one warn record, `ApiServiceUnavailable`, that carries the API's trace id.

## Reading the audit log

`roleradar audit list` prints the append-only audit log, newest first: who changed what and when, with the `req=` trace id of the request that did it. It is read-only and available only through the CLI; there is no MCP tool and no API endpoint for it. Options, output columns and exit codes are in the [CLI reference](../cli-reference.md#roleradar-audit-list).

```bash
roleradar audit list --entity-type posting --limit 10
```

## What is never logged

Access, refresh and identity tokens; `Authorization` and `Cookie` header values; passwords and backup passphrases; one-time codes and TOTP secrets; client secrets and the session key; credentials in connection strings; note bodies and appended note text; OIDC `code` and `state` values; and the username typed on a failed sign-in. Headers, cookies, request bodies and query strings are not logged either.

A scrubber applied to every record enforces this on top of the rule of not passing such values to a logger, and tests with sentinel values prove it in each host. See the [threat model](../threat-model.md) for the control and the tests.
