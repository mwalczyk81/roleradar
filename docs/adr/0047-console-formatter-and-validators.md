# ADR 0047: The Console Formatter and the Command Validators Stay Hand-Written

- **Status**: Accepted (2026-11-10)
- **Date**: 2026-11-10
- **Feature**: `014-standards-first` (story P4, Jira RR-81)

## Context and Problem Statement

Principle X (Standards First) asks whether a platform feature or a maintained library already does the job of two hand-written mechanisms. This record decides them separately, because they share nothing but the card.

1. `RoleRadarConsoleFormatter` (`src/RoleRadar.Observability`, 311 lines): the one console formatter every .NET host uses (ADR 0041). Candidates: the built-in `AddJsonConsole` and `AddSimpleConsole`, and Serilog (compact JSON formatters and a custom `ITextFormatter`, added in an addendum the same day).
2. The command validators in `src/RoleRadar.Application`: nine classes implementing `ICommandValidator<T>` plus two static validators (`ListAuditEntriesQueryValidator`, `CsvHeaderValidator`), 729 lines in all. Candidates: FluentValidation, .NET 10 minimal API validation, DataAnnotations.

Both ran as spikes in scratch copies outside the repository (nothing committed). The decision rules were fixed beforehand. Formatter: swap only if no existing formatter assertion has to change (a guard against changing the 013 contract by accident; "Required and incidental" below says what actually decides). Validators: adopt only if it removes code without changing any result (the same error code, message and metadata).

## Decision Outcome

**Keep both. No package is added.** Nothing in the repository changed except this record and its index rows. Both are cheap to revisit (see the end).

The formatter half is the closer call, and it is the owner's to overrule: a Serilog formatter that holds the whole 013 record contract passes all 17 cases and is about a quarter smaller than the console formatter, but what it deletes is adapter code, not the mechanism (see "Serilog" below). Failing the 17 cases is not itself a reason to keep the formatter; the deciding reasons are the scrubber hook and the platform's extension point (see "Required and incidental").

## Half 1: the console formatter

### The spike

The scratch project took the production formatter, `LoggingSetup`, `LogScrubber`, `TraceIds`, the `FormatterLogCapture` test sink and the real `RoleRadarConsoleFormatterTests` (27 cases). The only edit was the one factory line that builds the formatter under test. The built-ins were taken from the logging builder (`AddJsonConsole`, `AddSimpleConsole`, scopes included, UTC timestamps, one line) and run through the same cases.

| Formatter under test | Failed of 27 | Failed of the 17 cases that read formatter output |
| --- | --- | --- |
| `RoleRadarConsoleFormatter` (control) | 0 | 0 |
| `AddJsonConsole` for every case | 13 | 13 |
| `AddSimpleConsole` for every case | 17 | 17 |
| Mixed (`json` for the Json cases, `simple` for the Text cases) | 14 | 14 |

The other 10 of the 27 cases (8 `ResolveFormat`, 2 `Setup_ConfiguresTheConsoleStreamAndFormatter`) test `LoggingSetup`, our own code, and pass whatever the formatter is. Of the passes among the 17, four are vacuous: `Json_DropsAspNetHostingScopeNoise` and `Json_WithNoActivity_HasNoTraceId` pass because the built-in writes none of the fields they check for, and the two `EmbeddedNewlines` Json cases pass because JSON escapes newlines.

### Which assertions fail, and why

Sample output for one error record, as the built-ins write it:

- `AddJsonConsole`: `{"EventId":0,"LogLevel":"Error","Category":"RoleRadar.Test","Message":"Posting p-1 failed token SENTINEL-TOKEN-PROP Bearer SENTINEL-TOKEN-7f3a /cb?code=SENTINEL-CODE","Exception":"System.InvalidOperationException: boom password=SENTINEL-PW","State":{...},"Scopes":[{"Message":"System.Collections.Generic.Dictionary`2[System.String,System.Object]","RequestId":"0HN1","Upstream":"Bearer SENTINEL-TOKEN-7f3a"}]}`
- `AddSimpleConsole`: `fail: RoleRadar.Test[0] => System.Collections.Generic.Dictionary`2[System.String,System.Object] Posting p-1 failed ... System.InvalidOperationException: boom password=SENTINEL-PW`

| Assertion (013 record contract) | Built-in result |
| --- | --- |
| `Json_WritesOneObjectWithTheContractFields` | Field names are fixed and PascalCase (`EventId`, `LogLevel`, `Category`, `Message`, `Exception`, `State`). There is no `ts`, `service`, `msg`, `event`, `eventId` or `props`. The service field has no option to set it. |
| `Json_MapsLevels` (6 cases) | The level is `LogLevel` with the enum name (`Error`, `Critical`), not `level` with `error` and `fatal`. |
| `Json_TakesTheTraceIdFromTheCurrentActivity_UnderTheFieldTraceId` | No top-level `traceId`. The trace id would reach a built-in record only as a scope entry if the host enables activity tracking scopes, as an item in the `Scopes` array. |
| `Json_WritesTheErrorObject` | `Exception` is one string, not an object with `type`, `message` and `stack`. |
| `EveryField_IsScrubbed` (2 cases) | **No scrubber hook.** Bearer values, the token argument, the query string, the Mongo user info and the exception text all appear unredacted. Scope values are written as `ToString()` of the scope object. |
| `Text_WritesOneReadableLine`, `Text_WithoutATraceId_UsesADash` | The simple formatter writes `level: Category[0]` followed by the message: no service or trace id slot, and no timestamp unless one is configured. |
| `EmbeddedNewlines_StillProduceOneLine` (Text) | The simple formatter wrote three lines for a message and exception with embedded newlines, even with `SingleLine`. |

The other 013 suites use the same record shape (nine test files reference the formatter, and the `HostLogFormat` tests parse `service`, `traceId` and `eventId`), so a swap to a built-in would change assertions well beyond these 17, which the guard above forbids. Most of those assertions are about the shape 013 chose, though, which is why the next sections separate what is required from what is incidental.

### What was not a gap

A text and JSON toggle is possible with the built-ins: `ROLERADAR_LOG_FORMAT` could pick `FormatterName` between `json` and `simple`. The research note (R-012) said there was no built-in equivalent; that was too strong. What the built-ins lack that the code needs is covered under "Required and incidental": above all the scrubber hook. A scrubber could be added as a wrapping `ILoggerProvider` or by formatting before the built-in sees the message, but that is a hand-written mechanism again, with two layers where there is one.

### Serilog

The first pass left Serilog out. The same scratch project then ran the same 27 cases through Serilog 4.4.0 with `Serilog.Extensions.Logging` 10.0.0 (so the code still logs through `ILogger`), `Serilog.Formatting.Compact` 3.0.0 and, for the hosts, `Serilog.Sinks.Console` 6.1.1. All four are Apache-2.0 and older than 14 days (the newest, Serilog 4.4.0, was published 2026-07-10). The test sink is a Serilog sink that writes each event through the formatter under test; the only edit to the test file is the type of the capture it reads.

| Formatter under test | Failed of 27 | Failed of the 17 cases that read formatter output |
| --- | --- | --- |
| `CompactJsonFormatter` | 13 | 13 |
| `RenderedCompactJsonFormatter` | 13 | 13 |
| A custom `ITextFormatter` holding the 013 record contract | 0 | 0 |

**Compact formatters: the same 13 as `AddJsonConsole`, and the same causes.** They write Compact Log Event Format, not our record: `@t`, `@mt` (the template) or `@m` (the message with string arguments quoted), `@l` with `Error` and `Fatal`, `@x` for the exception as one string, `@tr` and `@sp` for the trace and span ids, `@i` as a hash of the template rather than our event id, and every property at the top level instead of under `props`. There is no `ts`, `level` in our spelling, `msg`, `event`, `eventId`, `error` object or `traceId`. Serilog does carry the trace id (`@tr` is the current activity's), so that is a renaming, not a gap. The two `EveryField_IsScrubbed` cases fail because the formatters have no scrubber hook: the Bearer value, the token argument, the query string and the exception text came out unredacted. They are JSON-only, so the two `Text` cases fail by construction. (An output template for text was not tried; a template cannot write our `key=value` props.)

**A custom `ITextFormatter`: 17 of 17, no assertion changed.** It is what the console formatter is, rewritten against Serilog's data model. It has to contain everything that makes the record ours:

- the record shape in both modes, JSON (`ts`, `level`, `service`, `category`, `msg`, `event`, `eventId`, `traceId`, `props`, `error`) and text (`ts level service [traceId] msg key=value ... error=`);
- the level names (`trace`, `debug`, `info`, `warn`, `error`, `fatal`) and the UTC millisecond timestamp;
- the ignore list for ASP.NET hosting scope noise;
- typing of property values (integers, decimals and booleans as JSON numbers and booleans), and one-line output in text mode;
- the message rendered without Serilog's quoting of string arguments, and with the template's format specifiers kept (below);
- the `EventId` property unpacked into `event` and `eventId`, and the exception into `type`, `message` and `stack`;
- the scrubber, and the redaction of a sensitive template argument inside the rendered message (`RedactSensitiveTemplateValues`).

**How the scrubber hooks in.** It stays inside the formatter, in the same places as today: the rendered message, every string property by key (`ScrubProperty`), the exception message and the stack. Serilog has no earlier stage that could do it: an enricher or a wrapping sink can rewrite property values, but a `LogEvent`'s exception is immutable and the message is rendered by the formatter, so the exception text and the message still reach the formatter unscrubbed. The scrubber (ADR 0046) is unchanged either way.

**What it deletes and what it adds.**

| Measure | Console formatter | Serilog custom formatter |
| --- | --- | --- |
| Formatter class, lines | 266 | 204 |
| Formatter class, code lines (no blanks or braces) | 154 | 112 |
| Formatter class, non-whitespace characters | 6,666 | 5,042 (24 percent fewer) |
| Deleted with the `ConsoleFormatter` base | scope walking (`Collect`), property de-duplication, the options-monitor constructor and the `Create` factory, about 60 lines | |
| Added | | message rendering token by token, the `EventId` unpacking, and a test sink (about 70 lines for `ICapture` and the Serilog capture, against the 88-line `FormatterLogCapture`) |
| Packages | `Microsoft.Extensions.Logging.Console` | `Serilog`, `Serilog.Extensions.Logging`, `Serilog.Sinks.Console` (3; a fourth, `Serilog.Formatting.Compact`, only for the compact path). The resolved graph is 10 packages, 6 of them Microsoft.Extensions packages already in the graph. |
| Lock files | | Observability and the 8 projects that reference it: 9 |

Whole-swap estimate, with the host wiring and the capture sink counted: about 375 lines removed (formatter, `FormatterLogCapture`, the console block of `LoggingSetup`, the options type) and about 290 added, a net of roughly 85 lines fewer. The host wiring and the 13 test files that use `FormatterLogCapture` were not spiked; that part is the estimate.

**The 17 cases do not pin message text.** A second comparison, outside the 17, ran five real call-site shapes through both formatters. The first port rendered `{ElapsedMs:F2}` as `12.3456 ms` where the console formatter writes `12.35 ms` (five templates in `src` use `:F2`), and the 17 cases passed anyway. After rendering numbers through the token's own format, four of five shapes were identical. The fifth differs only for a null argument (`null` against `(null)`) and a collection argument (`[1, 2, 3]` against `1, 2, 3`); no call site passes a collection today. Two more points the spike did not exercise: `Serilog.Sinks.Console` writes to stdout unless `standardErrorFromLevel` is set, which the CLI and the MCP host must do for every level (Principle VI), and the `Logging:LogLevel` filters should keep working because Serilog sits behind the `ILogger` provider.

**Reading of the rule.** The rule for this addendum was: if Serilog deletes real code, adopt it. It deletes real code, about 62 lines of the formatter class (24 percent by characters) and roughly 85 net across the swap. What it deletes is the `ConsoleFormatter` plumbing. The record shape, the level names, the one-line folding, the event and error fields, the scrubber hook and the redaction of sensitive template arguments all stay, in a formatter that is still ours, and the swap adds a compensation layer for Serilog's different rendering and 3 packages in the layer every host shares. That is a smaller gain than a library replacing the mechanism, and 015 may reopen the question anyway (below), so this record keeps the console formatter and flags the call to the owner. If the owner reads the rule as met, the swap is its own gate: the Serilog formatter and sink, `LoggingSetup`, the `ICapture` change in the 13 test files, nine lock files, the stdout settings above and a message-text test that pins format specifiers.

### Required and incidental

The 17 cases mix two kinds of assertion. Some check a capability the service needs. Others check the shape 013 chose for the record (field names, level spellings, the `error` object, the text layout). The first kind is the evidence; the second is not.

**Required** (ADR 0041, ADR 0046 and feature 014 say so):

1. **Scrubbing the final text.** Secrets from third parties reach a record as message text, exception text and structured values (ADR 0046). The scrub has to see the final rendered text, so it has to run in the formatter.
2. **The text and JSON toggle**, chosen by `ROLERADAR_LOG_FORMAT` and nothing else.
3. **A trace id on every record** written while an activity is current (feature 014).
4. **One line per record**, whatever the message or the exception contains.

**Incidental** (our own 013 choice, which a later feature may re-choose): `ts`, `level`, `service`, `category`, `msg`, `event`, `eventId`, `props` and `error` as field names and as a shape; `warn` and `fatal` as level names; the `key=value` text layout.

| Requirement | `AddJsonConsole` | `AddSimpleConsole` | Serilog compact formatters | A custom `ConsoleFormatter` or Serilog `ITextFormatter` |
| --- | --- | --- | --- | --- |
| Scrub the final text | **No hook** | **No hook** | **No hook** | Yes, inside the formatter |
| Text and JSON toggle | Yes, by choosing `FormatterName` | Yes, the same way | JSON only; a text output template was not tried | Yes |
| Trace id on the record | Not top-level; only as a scope entry if the host enables activity scopes | No slot | Yes, as `@tr` | Yes |
| One line per record | Yes (JSON escapes newlines) | **No**: three lines for a message and exception with embedded newlines, even with `SingleLine` | Yes (JSON escapes newlines) | Yes |

Counting the 17 cases by that split (5 required, 10 incidental, 2 vacuous):

| Formatter | Required cases failed (of 5) | Incidental cases failed (of 10) | Vacuous cases failed (of 2) | Total |
| --- | --- | --- | --- | --- |
| `AddJsonConsole` | 3 (scrubbing 2, trace id 1) | 10 | 0 | 13 |
| `AddSimpleConsole` | 5 | 10 | 2 | 17 |
| Mixed | 4 | 10 | 0 | 14 |
| Serilog compact and rendered compact | 3 (scrubbing 2, trace id 1, by field name only) | 10 | 0 | 13 |
| Custom Serilog `ITextFormatter` | 0 | 0 | 0 | 0 |

The required cases are `EveryField_IsScrubbed` (2), `Json_TakesTheTraceIdFromTheCurrentActivity_UnderTheFieldTraceId` (1, which also asserts the field name `traceId`) and `EmbeddedNewlines_StillProduceOneLine` (2). The toggle is covered by the 10 `LoggingSetup` cases outside the 17, which no formatter choice affects. The incidental cases are `Json_WritesOneObjectWithTheContractFields`, `Json_MapsLevels` (6), `Json_WritesTheErrorObject` and the two `Text_*` cases. The vacuous ones are `Json_DropsAspNetHostingScopeNoise` and `Json_WithNoActivity_HasNoTraceId`.

**Failing the 17 cases is not itself a reason to keep the formatter.** Ten of the 17 test names and shapes that 013 picked, and a built-in or library formatter that wrote a different but equally good record would be a legitimate swap if the tests were rewritten for it; the guard in the rules above only makes that a deliberate act, not an accident. What the table shows is narrower: of the required capabilities, scrubbing is the one missing from every candidate; the simple formatter also fails one line per record, and the built-in JSON formatter carries the trace id only as a scope entry.

**The deciding reasons are two.**

1. **No scrubber hook.** `AddJsonConsole`, `AddSimpleConsole` and Serilog's formatters have no point at which the final text can be rewritten, and Serilog's enrichers and sinks cannot reach the exception or the rendered message (see "How the scrubber hooks in"). Scrubbing the final text is required, so today the code that does it has to sit in the formatter, whichever library is underneath (015 may move it; see the note below).
2. **A custom `ConsoleFormatter` is the platform's extension point.** `Microsoft.Extensions.Logging.Console` is built to take a formatter registered with `AddConsoleFormatter`; `RoleRadarConsoleFormatter` uses that seam and nothing else, on the pipeline every host already has. That is extending the platform thinly in-repo, which AGENTS.md allows for a near fit, and it needs no second logging framework. A Serilog `ITextFormatter` would be the same custom code on Serilog's seam, behind the same hook and with the same scrubber, plus the framework.

What the record looks like (the incidental part) is a separate question, and 015 may change it (below).

### Note for feature 015's ADR (not a decision)

Feature 015 (Jira RR-66) adds OpenTelemetry. This is the comparison to carry into its ADR. I read "Microsoft's OpenTelemetry logging exporter" as the OpenTelemetry .NET SDK's `ILoggingBuilder.AddOpenTelemetry` integration with the OTLP exporter (`OpenTelemetry` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.20.1, Apache-2.0, published 2026-10-20); the SDK is maintained under the OpenTelemetry project, not by Microsoft.

- **It is the better base for 015, on this evidence.** The code logs only through `ILogger` (22 message definitions, no direct `Log*` calls, every host calling `AddRoleRadarLogging`). The OpenTelemetry integration is one more `ILoggerProvider` on that pipeline, so an OTLP exporter is added next to the console output and no call site changes. Serilog would be a second pipeline behind `SerilogLoggerProvider`, and its OTLP path is a further package (`Serilog.Sinks.OpenTelemetry` 4.2.0, Apache-2.0, 2025-05-31).
- **Trace correlation does not separate them.** Both carry the current activity's trace and span ids (the spike saw `@tr` and `@sp` in Serilog's output).
- **015 reopens the formatter question either way.** Once logs leave the process through OTLP, the console is a developer view and the built-in simple formatter may be enough, and the 62 lines Serilog would save here would go with the formatter. Adopting Serilog now and an OpenTelemetry provider in 015 would put two frameworks in the path in between.
- **The OTLP log data model may be a better target shape than the 013 record.** The record's field names and shape are incidental (see above), and OTLP already defines severity (text and number), body, attributes, trace and span ids, resource and instrumentation scope. If 015 sends logs through OTLP, the console record and its contract (`specs/013-operability/contracts/log-records.md`) could be re-derived from that model instead of kept as a second schema. Not a decision; 015's spike would compare the two.
- **An OpenTelemetry log processor is where scrubbing might move.** A processor sits between the logger and the exporter, which is where the scrubber would have to run for OTLP output in any case. Moving the scrubber there could serve both the OTLP exporter and a console exporter from one place, and would then remove the scrubber's reason for keeping a custom console formatter. Not a decision.
- **To verify in 015's spike, not assumed here:** whether a log processor can scrub the body, the attributes and the exception text before export. If it cannot, the scrubber has to run before the exporter (in front of it or at the source), which is the same question the Serilog formatter answered above.
- **Where Serilog would win:** a sink ecosystem and config-driven sinks that 015 might want, none of which the code needs today.

### Decision and when to revisit

**Keep `RoleRadarConsoleFormatter`.** The reasons are that none of `AddJsonConsole`, `AddSimpleConsole` or Serilog's formatters has a hook for scrubbing the final text, which is required, and that a custom `ConsoleFormatter` is the platform's own extension point for this. The count of failed cases is not a reason: 10 of the 17 test the incidental 013 shape, and the built-ins fail 13 to 17, Serilog's compact formatters 13. A custom Serilog formatter passes all 17 but is the same mechanism in a smaller shell, behind a second framework, and is flagged to the owner above. **Revisit when feature 015's OpenTelemetry log exporter lands.** If logs then leave the process through OTLP, the record shape may be re-derived from the OTLP data model, the scrubber may move into a log processor, and the console output may become a developer convenience that the built-in simple formatter serves; the formatter, its record contract and the scrubber's position in the pipeline are then reconsidered together. The scrubber (ADR 0046) stays as the backstop either way.

## Half 2: the command validators

### What ADR 0006 and ADR 0009 record

Neither record contains a reason against a validation library. ADR 0006 defines the seam, `ICommandValidator<in TCommand>` with `ResultError? Validate(TCommand command)`, run by `TransactionAndAuditCommandDecorator` before the readiness check, and rejects MediatR on licence grounds. ADR 0009 reuses the same patterns and rejects a state machine library and a CQRS framework. So the validators are hand-written because that was the default, not because a library was rejected. The decision here rests on the spike.

### The spike

FluentValidation 12.2.0: Apache-2.0 (the NuGet licence expression), published 2026-10-10 (31 days before this record), no dependencies on net10.0 (the transitive list is the package alone). `CreateNoteCommandValidator` and `RecordPostingCommandValidator` were converted in a scratch copy of Domain and Application, with the first-failure-only and rule-order behavior kept by `ClassLevelCascadeMode.Stop` and an adapter implementing `ICommandValidator<T>`. A differential test fed every combination of a grid of inputs through the original and the converted validator and compared code, message and metadata: 17,280 commands for the note validator and 29,160 for the posting validator (46,440 in all; the grid covers every branch, null and blank values, over-limit sizes, several simultaneous errors, tag and signal lists, and values that look like format placeholders).

| Result | Value |
| --- | --- |
| Natural conversion (`Must(...).WithMessage(...)`) | **88 differences** in 46,440 comparisons (36 note, 52 posting) |
| Cause, all 88 | FluentValidation formats every message it is given. A value the caller supplied that looks like a placeholder is replaced: provenance kind `{PropertyName}` came back as `Provenance Kind`, signal type `{PropertyName}` as `Signal Type`. The original echoes the text it was given. |
| Escaping with doubled braces | Does not work: `{{PropertyValue}}` came back as `{{PropertyValue}}` and `{PropertyName}` as `{Signal Type}`. |
| Conversion with `Custom` and a prebuilt `ValidationFailure` for the three rules that echo input (provenance kind, provenance date, signal type) | **0 differences** in 46,440 |
| First-failure and order across simultaneous errors | Equal on the whole grid. The tag rules are one `RuleForEach` chain (blank, then length, per tag), which is the order the original uses. |
| Null command | The same `ArgumentNullException` |

### The numbers

| Measure | Original | Converted |
| --- | --- | --- |
| Lines, the two spiked validators | 219 (94 and 125) | 97 (39, 35 and a 23-line nested signal validator), plus a 24-line adapter written once |
| Non-whitespace characters, same classes | 5,694 | 4,813, plus 585 for the adapter (net 296 fewer, about 5 percent) |
| Longest line | 149 characters | 181 characters |
| Projected for the nine `ICommandValidator` classes (626 lines) | 626 | about 277, plus the adapter |
| Not convertible with the same call shape | `ListAuditEntriesQueryValidator` and `CsvHeaderValidator` (103 lines) are static methods over other shapes | |
| Projects that would take the package | Application, and through it Api, Cli, Mcp.Stdio, Infrastructure and the Architecture, Contract, Integration and Unit test projects: nine lock files | |

By line count the swap removes more than half the validator code. By characters it removes about 5 percent: the conditions and the messages are most of each validator and FluentValidation does not shorten them, it only replaces the `if (...) { return new ResultError(...); }` frame with a chain on fewer, longer lines.

### Compared

| Option | Fit | Verdict |
| --- | --- | --- |
| FluentValidation 12.2.0 | Same code, message and order are reachable only by leaving the declarative form for the three rules that echo input, and every message that echoes a caller's value would need the same care, because the natural form changes the result without any test failing. One of the nine validators (`TransitionPostingCommandValidator`) carries `field` metadata, which would travel as `CustomState` through the adapter; it was not in the spike, so parity for it and for six other validators is extrapolated, not shown. | **Not adopted** (below) |
| .NET 10 minimal API validation | Validates endpoint parameters at the HTTP layer only. The CLI and the MCP host never see it, and the Application layer's validators run for all three hosts inside the decorator. | Rejected, by reasoning (not spiked) |
| DataAnnotations | The rules are conditional on the mode (update versus create), depend on several fields and build their messages from computed lengths. Attributes on shared command records express none of that, and mapping a failure to `ErrorCode` and `Metadata` would be hand-written code again. | Rejected, by reasoning (not spiked) |

### Decision

**Keep the hand-written validators.** The rule was to adopt only if the change removes code without changing a result. The result half holds only after a workaround. The code half holds by lines (about 325 fewer across the nine) and fails by substance (about 5 percent fewer characters): the saving is mostly layout. Against it stand a package in the layer all three hosts share, nine lock files, an adapter, a standing trap for every future message that echoes input (the natural form changes a result and no existing test would notice), and parity for seven validators that would be argued, not shown. The existing validator tests would not catch a difference either; they assert `NotNull`, not the message.

**What would change the decision.** If the validators grow past about a dozen, or a rule needs cross-property or async checks that the hand-written chain handles badly, the line saving starts to matter and the swap becomes a net win. Then it should be done with a parity test through `TransactionAndAuditCommandDecorator` that compares code, message and metadata on a grid like the spike's (the grid and the escaped-message finding are in the notes of `specs/014-standards-first/tasks.md`), and with the three echo rules written as `Custom` failures from the start.

## Consequences

- **Good:** nothing changes in the logging path or in validation; the 013 formatter suites and every validator test are untouched. The reasons the hand-written code stays are recorded with evidence, as Principle X asks. The research note that said the built-ins have no toggle is corrected here.
- **Bad:** 311 lines of formatter and about 625 lines of validators stay hand-written. A Serilog `ITextFormatter` would shrink the formatter class by about a quarter; that saving is left on the table until the owner decides and until 015 settles the logging base, and the validators repeat the `if` and `return new ResultError` frame that a library would shorten.
- **Packages:** none added or changed. No lock file changes.
- **Scratch evidence:** the spikes (including the Serilog addendum) lived outside the repository and are not committed; the tables above are their output.
