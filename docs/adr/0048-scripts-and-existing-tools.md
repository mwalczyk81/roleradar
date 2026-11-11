# ADR 0048: The Action-Pin Script Is Retired for zizmor With a Token; the Licence Audit Stays

- **Status**: Accepted (2026-11-11; revised the same day, see "Revision")
- **Date**: 2026-11-11
- **Feature**: `014-standards-first` (story P4, Jira RR-83)

## Context and Problem Statement

Principle X (Standards First) asks whether an existing tool already does the job of a custom script. Two scripts were named:

1. `scripts/verify-action-pins.ps1` (58 lines, run in `.github/workflows/ci.yml` with `GH_TOKEN`; covered by `ScriptPortability.Tests.ps1`). Its policy was: every `uses:` is a 40-character SHA with a `# vX.Y.Z` comment, and that SHA exists in the named repository. Candidates: zizmor (the pre-commit hook pinned at v1.32.0, with its `unpinned-uses`, `impostor-commit` and `ref-version-mismatch` audits) and Dependabot, which only keeps pins current.
2. `scripts/licence-audit.mjs` (207 lines; tested in `web/tests/security/markdown-invariants.test.ts`; not called from CI). Its policy (ADR 0032): the Markdown rendering dependency tree may use only MIT, ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0 or 0BSD, and a missing, empty, unparseable, `SEE LICENSE IN` or `UNLICENSED` field **fails**. Candidates: `trivy fs --scanners license` (the pinned `aquasec/trivy:0.59.1` that the review gate and CI run), `pnpm licenses`, and the Dependency Review action (CI runs v5.0.0 with `deny-licenses`).

The rule fixed beforehand: replace a script only if the existing tool enforces the same policy and **failed on a deliberate violation first**; otherwise the script stays with a one-line justification. `scripts/denylist-check.ps1` and `scripts/review-gate.ps1` stay without a comparison (project-specific orchestration).

## Decision Outcome

| Script | Outcome |
| --- | --- |
| `verify-action-pins.ps1` | **Retired.** zizmor fails on every violation the script caught except a missing version comment, which the owner chose to stop checking (below), once its online audits run in CI. CI now runs zizmor in its own step with the workflow token. |
| `licence-audit.mjs` | **Stays.** No tool fails on a package with a missing or empty licence field, and Dependency Review documents that it will not fail on an unknown licence. |

### What changed

- `ci.yml`: the main `pre-commit` step skips zizmor (`SKIP: roleradar-denylist,no-commit-to-branch,zizmor`); a separate step runs only `pre-commit run zizmor --all-files --verbose` with `GH_TOKEN: ${{ github.token }}`, so the online audits run and the other hooks never see the token. `--verbose` is there because a passing hook prints nothing otherwise. The step `Verify workflow action SHA pins exist on GitHub` is gone.
- Deleted: `scripts/verify-action-pins.ps1` and its entry in `ScriptPortability.Tests.ps1`.
- Test first, shown failing (11 of 12 failed against the old `ci.yml`), now passing: `web/tests/tooling/workflow-zizmor.test.ts` with `web/tests/support/workflow-zizmor.ts`. It asserts a zizmor step with `GH_TOKEN: ${{ github.token }}` exists, runs only the zizmor hook, has no `if` and no `continue-on-error`, comes after the pre-commit install, that the main step skips zizmor, that no other step, job or workflow env sets a token, that no step runs the retired script, and that the script file is gone. A mutation test exists for each rule.
- Docs: ADR 0024 (the `pre-commit` job description), this record, the README rows and `CHANGELOG.md`. ADR 0032 needed no change: it describes only the licence audit, which stays.

### The missing-comment check is intentionally dropped

The script failed a pin whose `# vX.Y.Z` comment was missing. zizmor does not (it passed that case with and without a token, below), and the owner accepted dropping the check. The reasons:

- The SHA is the pin. The comment carries no security property: whether the pinned commit exists in the action's repository is the `impostor-commit` audit's job, and a hash pin is the `unpinned-uses` audit's.
- A comment that is **present** is now checked for correctness (`ref-version-mismatch` fails a wrong one with a token), which the script never did, since it only checked the comment's shape.
- Keeping the script for this one rule would keep a PowerShell script, the `gh` dependency, a Pester entry and a second CI step.

The consequence is stated plainly: a pin with no comment passes CI, and a reader loses the hint of which version it is. If that proves a problem, the right tool is a zizmor rule or a small test, not this script.

## Action pins: evidence

Violations were made in the **working tree only** (workflow files, never committed) and undone afterwards; the workflow files were back to the intended content every time. "Hook, no token" is how CI ran zizmor before this change (zizmor v1.32.0 logs "running in offline mode by default" and the old pre-commit step set only `SKIP`). "Hook, with token" is the same hook with `GH_TOKEN` in the environment (a local token for these runs only), which is how the new step runs it. The first three columns come from the original evidence runs, before the change. The last column was run first the same way, then repeated on the revised `ci.yml` with the exact CI command (`pre-commit run zizmor --all-files --verbose` with a token), with the same result for every row except the third-party tag, which was run once.

| Violation | Script | Hook, no token | Hook, with token |
| --- | --- | --- | --- |
| Tag form on a GitHub-owned action (`actions/checkout@v7`) | fails | fails (`unpinned-uses`) | fails |
| Tag form on a third-party action (`dorny/paths-filter@v4`) | fails | fails | fails (first run only) |
| Pin to a SHA that does not exist (`actions/checkout@...90b2`; `gh api` returns 422) | fails | **passes** | fails (`impostor-commit`, `ref-version-mismatch`) |
| Valid SHA, wrong comment (`# v1.0.0`) | passes (it reads only the comment's shape) | passes | fails (`ref-version-mismatch`) |
| Valid SHA, comment removed | fails | passes | **passes** (accepted, above) |

What this shows:

- The online audits (`impostor-commit`, `ref-version-mismatch`) **need a token**, and before this change **CI gave none to zizmor**: `GH_TOKEN` was set only on the script's step. That is why the script could not be retired on the first pass, and why the token step is the condition for retiring it now.
- zizmor already requires hash pins for every owner without configuration (it flagged both a GitHub-owned and a third-party tag), so no `zizmor.yml` was added. An explicit one would only protect against a future change of its default.
- With the token, zizmor is stronger than the script on one point (a wrong comment) and equal on the one the script existed for (a nonexistent SHA).
- A token run of the hook takes about 3 to 4 seconds when nothing is wrong and about 43 seconds when a pin is to a nonexistent commit (the online lookup); the offline hook takes 0.1 seconds.
- Not tested: a SHA that exists only in a fork (the other half of `impostor-commit`), because creating one needs a fork.

### What could not be verified locally

- That the new step runs its online audits **in CI**. Locally the token was my own, from `gh auth token`. In CI it is `${{ github.token }}` of the workflow (`permissions: contents: read`); whether that token is accepted by the audits' API calls on a GitHub-hosted runner, including on a pull request from the same repository, is not provable here. **The pull request's own CI log has to show it.** In the log of the step `Run zizmor with its online audits (impostor-commit, ref-version-mismatch)` look for: the line `INFO zizmor: zizmor v1.32.0`, **no** `WARN audit: zizmor: zizmor is running in offline mode by default` line, and a duration of a few seconds rather than 0.1 s. If the offline warning is there, the token did not reach zizmor and the audits did not run.
- That the main pre-commit step still passes in CI without zizmor and without a token (it does locally with `SKIP` containing `zizmor`).
- Nothing detects a later silent return to offline mode except that log line (the test guards the workflow text, not the runtime); a guard on the log would be a custom script and was not added.

## Licences: evidence

The evidence is a **throwaway package tree in the scratchpad, outside the repository**, with fictional packages: `MIT`, `Apache-2.0` (allowed), `GPL-3.0-only`, `AGPL-3.0-only` (denied), `MPL-2.0` (allowed by no list), no `license` field, an empty one, `Totally Made Up Terms v9`, `SEE LICENSE IN LICENSE.txt` and `UNLICENSED`. It was installed with `pnpm install` from `file:` dependencies. No real dependency, lock file or manifest changed.

| Case | `pnpm licenses list --prod` (pnpm 10.4.0) | `trivy rootfs --scanners license` (0.59.1) | Dependency Review v5.0.0 |
| --- | --- | --- | --- |
| MIT, Apache-2.0 | listed | LOW, notice | not run (see below) |
| GPL-3.0-only | listed | HIGH, restricted | `deny-licenses` has GPL-3.0 |
| AGPL-3.0-only | listed | CRITICAL, forbidden | `deny-licenses` has AGPL-3.0 |
| MPL-2.0 | listed | MEDIUM, reciprocal | passes (not in the deny list) |
| **No `license` field** | listed as `Unknown` | **not reported at all** | documented: informs, "the action won't fail" |
| **Empty `license`** | listed as blank | **not reported at all** | same |
| Unparseable text | listed verbatim | UNKNOWN | same |
| `SEE LICENSE IN ...` | listed verbatim | UNKNOWN | same |
| `UNLICENSED` | listed verbatim | reported as `Unlicense`, unencumbered, LOW (the opposite meaning) | same |
| Exit code | 0 for every case | 1 with `--exit-code 1` and all severities | fails only on a denied licence |

- `pnpm licenses` is a report, not a gate: exit 0 with GPL, AGPL and unknown packages present.
- Trivy can approximate an allowlist: `--ignored-licenses MIT,ISC,BSD-2-Clause,BSD-3-Clause,Apache-2.0,0BSD --severity UNKNOWN,LOW,MEDIUM,HIGH,CRITICAL --exit-code 1` failed on every package in the tree except the two that matter most. On a tree of only an allowed package, a package with no licence field and one with an empty field, it **exited 0**. It also files `UNLICENSED` under the wrong name. In `fs` mode it read nothing (installed `node_modules` are read only by `rootfs`). Run on the real `web/` it found nothing, but that is not a result about the dependencies: on this Windows checkout, pnpm's junctions do not resolve inside a Linux container. For NuGet it reported no licences for two restored packages whose `.nuspec` files declare `MIT` and `Apache-2.0`, so it gives no NuGet licence data.
- Dependency Review was **not run**: it works on a pull request's dependency diff through GitHub's dependency graph and cannot run locally; creating a throwaway repository and pull request to try it would be an outward-facing action that was not requested. The evidence is its own documentation at the pinned commit (`a1d282b3...`): "If we can't detect the license for a dependency we will inform you, but the action won't fail", and `src/licenses.ts` sends an unlicensed dependency to a notice, not to the failure list. The same README marks `deny-licenses` (the option CI uses) as deprecated for possible removal in the next major release. That is a risk to the CI policy to track, not a reason to change it in this card.

**No tool reproduces "unknown fails".** `licence-audit.mjs` stays. Its scope (the three Markdown packages and their tree) is narrower than Dependency Review's (every dependency a pull request adds, deny-list only), so the two do not overlap fully either. Revisit when a licence tool offers an allowlist that fails on unknown.

## Revision

First written the same day as "keep both scripts": zizmor, as CI ran it, missed a nonexistent SHA (it ran offline) and a missing comment, so the script was the only check for both. The owner then accepted dropping the comment check, and asked for the token to be given to zizmor so the SHA check moves to the standard tool. The evidence above did not change; the policy and the CI wiring did.

## Spike notes

- Every scratch tool ran outside the repository, and the working tree was clean of every violation afterwards. The throwaway tree, caches and logs are in the scratchpad only.
- Two of my first violation runs were not violations: a `sed` delimiter clash left the file unchanged, so those runs were baselines and were redone. The table comes from the runs that changed the file.
- The "tag form" violations changed every matching line in `ci.yml` in two cases (11 lines), not one; the result is the same.
