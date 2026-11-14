# ADR 0046: Log Redaction Stays With LogScrubber

- **Status**: Accepted (2026-11-10)
- **Date**: 2026-11-10
- **Feature**: `014-standards-first` (story P4, backlog RR-80)

## Context and Problem Statement

Feature 013 redacts secrets from every log record with `LogScrubber` (`src/RoleRadar.Observability`, 84 lines): seven rules over formatted text and structured properties, mirrored in `web/app/lib/observability.ts`. It is hand-written, so Principle X (Standards First) asks whether a platform feature does the job. The candidate is `Microsoft.Extensions.Compliance.Redaction` 10.11.0 (MIT, published 2026-10-14) with data classification: a parameter of a source-generated `[LoggerMessage]` method carries a classification attribute and the logging pipeline runs the redactor registered for it. 013 R-001 rejected it as a replacement. This evaluation asks the narrower question: adopt it for the service's own parameters and keep a smaller scrubber for third-party text, or keep `LogScrubber` as it is.

## Decision Outcome

**Keep `LogScrubber` as it is. Do not add `Microsoft.Extensions.Compliance.Redaction`.** The decision rule was: adopt only if every 013 redaction suite passes unchanged and the change closes a real gap or removes net code. The first condition is not in question (nothing was changed). The second fails on both counts: no gap is closed, and no `LogScrubber` line can be removed. Nothing in the repository changed except this record.

### What the spike did

A scratch project outside the repository (nothing committed) took the real `LogScrubber`, `RoleRadarConsoleFormatter` and `TraceIds` files, added `Microsoft.Extensions.Compliance.Redaction` and `Microsoft.Extensions.Telemetry` 10.11.0 (the package that wires redaction into `ILogger`), classified two parameters (the `subject` of `SecurityEvents.SignInFailed` and the `detail` of a handler-style message) as a fictional `Secret` class erased to `[redacted]`, and logged a sentinel through the standard simple console and through the RoleRadar formatter. Then it logged the sentinel through the paths classification cannot reach.

| Case (sentinel secret) | Classification alone (standard console) | With `LogScrubber` (RoleRadar formatter) |
| --- | --- | --- |
| Classified parameter of a `[LoggerMessage]` method (2 cases) | redacted | redacted |
| Raw Duende authorize request as a JSON-shaped string | **leaks** | redacted |
| `Authorization: Bearer <value>` in message text | **leaks** | redacted |
| JWT as a message argument | **leaks** | redacted |
| `mongodb://user:password@host` in message text | **leaks** | redacted |
| URL with `?code=...&state=...` | **leaks** | query stripped |
| Structured property a third party named `code` or `state` | **leaks** | redacted by key |
| Exception message `login failed password=<value>` | **leaks** | redacted |
| `LoggerMessage.Define` call site (no attribute exists) | **leaks** | **leaks** (innocuous key `Detail`, bare value) |

Classification covers only parameters the code classifies, in `[LoggerMessage]` partial methods. It never sees text another library formats, exception messages, or property names a third party picks. Those are the seven 013 rules, so all seven stay. The one row both approaches miss is a secret passed to our own message under an innocuous name; none of the 22 definitions passes one today, and the sign-in events are built so that none can (no `SecurityEvents` method takes a username, password or one-time code, and the 013 contract tests hold that line).

### The numbers

| Measure | Value |
| --- | --- |
| Log message definitions in `src` | 22 in 9 files (`git grep -n "LoggerMessage" -- src`): 14 are `[LoggerMessage]` partial methods, 8 are `LoggerMessage.Define` |
| Definitions that classification could attach to | 14. The 8 `Define` call sites (`LoggingDecorators`, `MongoReadinessProbe`) have no attribute and would first be rewritten as partial methods |
| Direct `ILogger.Log*` extension calls in `src` | 0 |
| Parameters that carry anything secret-shaped today | 0 (ids, names, status, durations, an endpoint; `{Detail}` in the MCP filter is free text and needs the text rules anyway) |
| Lines of `LogScrubber` removable | **0**. The text rules cover third-party text. The key denylist (`IsSensitiveKey`, `ScrubProperty`, `RedactSensitiveTemplateValues` in the formatter, about 50 lines) protects structured keys other libraries choose, which classification cannot see |
| Lines added if adopted | a data taxonomy, an erasing redactor, `AddRedaction` and `EnableRedaction` in four hosts (API, Identity, CLI, MCP), attributes on the classified parameters, and a new unit test class |
| New packages | 10 for a project that references only the console logger (22 resolved against 12; the hosts already carry some of the abstractions): `Microsoft.Extensions.Compliance.Redaction`, `Compliance.Abstractions`, `Telemetry`, `Telemetry.Abstractions`, `AmbientMetadata.Application`, `DependencyInjection.AutoActivation`, `ObjectPool`, and three hosting and file-provider abstractions |

### Behavior found that counts against adoption

- A classified parameter renders as `(null)` when redaction is not enabled on the logger. With the RoleRadar formatter and `EnableRedaction` left out, `Sign-in failed for subject (null)` came out. It fails closed, but it drops a field the 013 sign-in events and tests assert (`subject`), and every host and every test host that builds its own logging would have to remember the registration.
- Classification asks a developer to remember an attribute. Today the guarantee is structural (no method accepts a secret) and the scrubber is the backstop for everything else. An attribute does not replace either of those and adds a third thing to forget.

### Compared

| Option | Own parameters | Third-party text | Cost | Verdict |
| --- | --- | --- | --- | --- |
| `LogScrubber` as is | Key denylist and text rules | All seven rules | 84 lines plus the web mirror, tested one case per rule (SC-003) | **Kept** |
| Classification plus a smaller scrubber | Classified at the source | Unchanged, every text rule stays | Everything above, plus 10 packages and a rewrite of 8 `Define` call sites, with nothing removed | Rejected: more code and a larger dependency surface for no gap closed |
| Classification only | Classified at the source | Not covered (7 of 7 third-party spike cases leak) | Smallest of the two adoption options | Rejected: the 013 redaction suites for third-party text would fail |

### Revisit when

Revisit if the service starts logging a parameter that could hold a secret under a name the denylist cannot know (for example a user-supplied free-text field in a `[LoggerMessage]` method), or if feature 015's OpenTelemetry log exporter brings the Compliance package in for another reason. Then classifying that parameter at the source costs one attribute and this decision is cheap to reverse. The scrubber would stay as the backstop either way.

## Consequences

- **Good:** nothing changes in the logging path, the 013 redaction suites (unit `LogScrubberTests`, contract `IdentityRedactionContractTests`, integration, web `web-redaction.test.ts`) are untouched, and the reason the hand-written code stays is recorded with evidence, as Principle X asks.
- **Bad:** `LogScrubber` remains hand-written code that Microsoft's package does not replace: the seven rules need maintenance when a new third-party log format appears, and the web keeps a mirrored copy.
- **Packages:** none added or changed. No lock file changes.
- **Scratch evidence:** the spike lived outside the repository and is not committed; the table above is its output.
