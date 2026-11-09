# Contract: `roleradar demo` CLI group

All three commands run the demo guard first and refuse (exit code 2, short message, no connection string or host in it) unless demo mode (`ROLERADAR_DEMO=1`) is on and the database is `roleradar-demo` on the demo host. Production callers do not pass a host.

| Command | Purpose | Output | Exit codes |
| --- | --- | --- | --- |
| `roleradar demo seed [--as-of YYYY-MM-DD]` | Load the fictional dataset through the repositories, idempotent by natural key; create the owner with a generated, discarded password and key | Counts per entity, "created" or "already seeded". Never a secret. | 0 ok, 2 guard refusal, 1 failure |
| `roleradar demo credentials` | Reset the demo account; print username, password, TOTP secret and `otpauth://` URI once | Console only. Refuses unless stdin and stdout are interactive terminals. Never uses a logger. | 0 ok, 2 guard refusal or non-interactive, 1 failure |
| `roleradar demo totp` | Print the current 6-digit TOTP code for the demo account | One line: the code and seconds remaining | 0 ok, 2 guard refusal, 1 no account |

The screenshot spec contract is the environment: `ROLERADAR_SCREENSHOT_DIR`, `ROLERADAR_DEMO_URL`, `ROLERADAR_DEMO_USERNAME`, `ROLERADAR_DEMO_PASSWORD`, `ROLERADAR_DEMO_TOTP_SECRET`. It skips with a reason when `ROLERADAR_SCREENSHOT_DIR` is unset and refuses a non-demo URL.
