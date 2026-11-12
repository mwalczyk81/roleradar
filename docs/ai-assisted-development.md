# How AI-written code is governed here

AI agents wrote much of RoleRadar's code, tests and documentation. This page says what that means in practice: who decides, what is checked by a machine, what is never delegated, and what the review caught. The numbers are from the 0.1.0 release (2026-11-12) and from command output, not from memory.

## The short version

- A person sets the requirements, reviews every phase and every pull request, and makes the calls that the [ADRs](adr/README.md) record.
- A coding agent writes the code under a spec, a plan and a task list, test first.
- Machines check what they can check: architecture tests, a review gate, secret and name scans, and CI.
- A second AI session reviews each agent report against the ADRs and the command output before the owner reads it. That is a filter, not a decision-maker.
- Some things are never delegated (below).

## The process

Every feature goes through the same path (Spec Kit, with a [constitution](../.specify/memory/constitution.md) of ten principles):

1. **Specification.** What and why, with measurable success criteria.
2. **Plan.** How, with a "Standards considered" table: before building a mechanism, check for an RFC, a platform feature or a maintained library (Principle X). A hand-rolled mechanism needs an ADR that names what it lost to.
3. **Tasks.** Ordered, with the tests listed before the code.
4. **Implementation in gates.** Each gate is one pull request. The agent writes the failing test and shows the red run before it writes the code, then reports command output, not impressions.
5. **Review.** The owner reads the report and the diff, and sends it back when it does not hold up.
6. **Close-out.** Every success criterion and requirement is checked against fresh command output and recorded (for example [feature 015](../specs/015-observability/tasks.md)).

Fifteen features have gone through it. There are 53 ADRs.

## What is checked by a machine

| Check | What it enforces |
| --- | --- |
| Architecture tests | Layer dependencies, that every command handler is wrapped by the audit and transaction decorator, that no tool or endpoint hard-deletes, that owner ids come from one place |
| Review gate (`scripts/review-gate.ps1`) | Locked restore, format, warnings as errors, typecheck, tests, coverage of at least 85 percent on Domain and Application, image builds, a container scan with a pinned Trivy, and the real-stack end-to-end lane |
| Web gate | Typecheck, lint, tests, build and browser tests in the CI Linux image, which is stricter than a Windows run |
| Mutation testing | The web library code has a committed mutation score floor of 81 |
| Pre-commit and pre-push hooks | gitleaks over the diff, a private real-name denylist, conventional commits, markdown lint |
| CI | CodeQL, Dependency Review, workflow pin checks with zizmor, a combined test report |
| Dependencies | Exact pins, digests for images, and a 14-day minimum release age, with the one exception so far recorded in an ADR ([0053](adr/0053-aspire-13-6-5-security-fix-release-age-waiver.md)) |

At 0.1.0 the .NET suites pass 2,253 tests (Unit 1,040, Architecture 108, Contract 339, Integration 766) and the web suite passes 1,368 tests plus 154 browser tests.

## What is never delegated

- **Committing, pushing and opening pull requests.** The agent leaves changes in the working tree ([AGENTS.md](../AGENTS.md)). A person pushes.
- **Anything against the owner's real data or running stack.** Tests use throwaway containers. The agent never runs `docker compose down -v`, a wipe, or a restore over a live database.
- **Trust decisions on the machine.** The agent does not run a certificate trust step. The local dev loop runs over loopback HTTP for that reason.
- **Real data anywhere.** The repository, the screenshots and the reports use fictional data only.
- **Upstream issues and pull requests.** Nothing is opened against another project without the owner's approval.
- **The calls.** Keep our own code or adopt a library, accept a risk, waive a rule: the owner decides and an ADR records it. For example, the owner chose to keep the log scrubber and the backup format after the alternatives were measured ([0046](adr/0046-log-redaction.md), [0043](adr/0043-backup-archive-format.md)).

## What the review caught

Agents are fast and confident, and some of what they report is wrong. These are real examples, found by a review or a manual run, not by the agent:

- **A privacy gate that relied on a processor to strip what a handler put on a span.** The handler put the exception message and stack on the span and counted on a later step to remove them. The fix was to put only the type there.
- **A reflection hack in the same gate that failed open.** If the field it cleared went missing, nothing would have been scrubbed. It now fails closed, with an ADR and a test.
- **A census that guessed.** A log-scrubber evidence table said a rule "can embed" a secret without ever observing one. Review sent it back twice. The third version ran each rule against the real producers and found what actually happened: the identity server logged the raw authorize request at its default level, so that source is now filtered at the source.
- **Reports that disagreed with their own ADR.** Package versions, rule names and event ids differed between the agent's summary and its ADR several times. The ADR and the command output were the source of truth.
- **Unbounded metric labels.** A first metrics implementation labelled requests with the raw path. A route label from the real route table, with one bucket for everything else, replaced it.
- **A secret in an exported log record that every test missed.** During the manual check of the local dashboard, an OAuth PKCE code verifier showed up in an exported log record. The scrubber only handled string attributes, and the tests had only covered failed token requests. The fix is at the shared choke point, and the lesson is in the [close-out record](../specs/015-observability/tasks.md).
- **A restart bug in the dev loop.** The second start failed because a generated database password no longer matched the kept data volume. Only a person using it twice found it.
- **Hand-rolled code where a standard exists.** A sweep found a private error envelope, a private correlation id, hand-written one-time-code checks and a hand-written web retry. They were replaced by RFC 9457, W3C Trace Context, the ASP.NET Core Identity provider and `ky`, and the rule became Principle X.

## How cost and risk are contained

- The agent's prompt is the spec command plus context. It has no connector access to the issue tracker.
- Every pull request is one gate, small enough to review.
- The gates run the same checks the agent was told to run, so a claim in a report can be checked.
- Fictional data and throwaway containers mean a mistake cannot reach real data.

## Limits, said plainly

- There is one human reviewer. The process reduces what has to be read; it does not remove the reading.
- Gates prove what they assert and nothing else. The verifier leak passed every automated check because no check covered a successful token exchange.
- Not every platform is checked on every pull request. The macOS job runs weekly and on demand, and the macOS lock file for the local dev loop was generated on Windows, so its first real restore is on that run.
- An agent's summary is a claim. Treat it the way you would treat a colleague's: check it against the output.
