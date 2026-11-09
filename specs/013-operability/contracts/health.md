# Contract: Health Endpoints

Applies to the API and the Identity service. Decision: research R-006.

| Endpoint | Checks | 200 body | 503 body |
| --- | --- | --- | --- |
| `GET /health/live` | none (the process serves requests) | `{"status":"Healthy"}` | n/a |
| `GET /health/ready` | API: Mongo readiness probe (`hello`, writable primary). Identity: SQLite `CanConnectAsync`. Each bounded at 2 s. | `{"status":"Healthy"}` | `{"status":"Unhealthy"}` |
| `GET /health` | alias of `/health/live`, kept for compatibility | `{"status":"Healthy"}` | n/a |

- Anonymous. Bodies carry no endpoint, host, error text or remediation command.
- Every response carries `X-Correlation-ID`. Health requests log at Debug so compose healthchecks do not flood the log at the default level.
- `--healthcheck` (the image's built-in check, used by compose) calls `ROLERADAR_HEALTHCHECK_URL`, whose default changes from `http://127.0.0.1:8080/health` to `http://127.0.0.1:8080/health/ready`. The default is held in `public const string DefaultHealthcheckUrl` on `RoleRadar.Api.Program` and `RoleRadar.Identity.Program`. Exit code 0 on 200, 1 otherwise. Timeouts stay 2 s connect and 3 s total. No compose healthcheck line changes.
- The web's `/health` is unchanged (static, liveness only).
