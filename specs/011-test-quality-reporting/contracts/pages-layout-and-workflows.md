# Contract: Pages layout, triggers and permissions

## gh-pages layout (no collisions)

| Path | Content | Written by |
|---|---|---|
| `metrics/metrics.jsonl` | append-only records; the job checks out gh-pages, appends with `web/scripts/metrics-record.mjs` and publishes the whole file | main CI publish job |
| `metrics/index.html` | trends page | main CI publish job (copied from `pages/metrics/`) |
| `badges/coverage.svg` | coverage badge from ReportGenerator | main CI publish job |
| `coverage/` | ReportGenerator HTML | main CI publish job |
| `allure/` | combined Allure site (sanitized), replaced on every publish | main CI publish job and the nightly `publish-allure` job |
| `allure-history/history.jsonl` | the Allure history file (capped by `historyLimit`), overwritten on every publish | main CI publish job and the nightly `publish-allure` job |

## Trigger and publish matrix

| Event | Mutation | Allure build | Artifacts uploaded | Pages publish |
|---|---|---|---|---|
| pull_request, same repo, not touching nightly-quality.yml | no | yes in ci.yml (artifact only) | yes | no |
| pull_request, same repo, changing nightly-quality.yml (validation run) | yes | yes (artifact only) | yes | no, no secrets |
| pull_request, fork | no | no | no | no |
| push to main | no | yes | yes | yes |
| nightly schedule / manual dispatch (nightly-quality.yml) | yes | yes | yes | yes |

The nightly workflow's trigger block is exactly `schedule`, `workflow_dispatch` and `pull_request` with `paths: ['.github/workflows/nightly-quality.yml']`; a test fails on any other shape. In the final Allure job `needs:` lists every stack job and `if: always()` is set so one failed stack still yields a report.

## Permissions

| Job | Grants |
|---|---|
| default | `permissions: {}` at workflow level where possible, otherwise `contents: read` |
| build, test, mutation, report jobs | `contents: read` |
| metrics fetch of latest nightly | `contents: read`, `actions: read` |
| Pages publish job (single job, main and nightly only) | `contents: write` |

Rules: `persist-credentials: false` on every checkout, including the read-only checkout of gh-pages (the deploy action pushes with the job token); the dashboard key is exposed only to the single Stryker publish step; the publish job runs no repository code from a pull request; actions pinned by full SHA; `concurrency: pages-publish` with `cancel-in-progress: false`.

## Publishing and what the owner must enable

All publishing is `peaceiris/actions-gh-pages` (v4.1.0, SHA pinned). `site-keep` (trends page, metrics history, coverage badge, Allure history) is copied with `keep_files: true`; `site-coverage` and `site-allure` each replace one directory (`destination_dir`). There is no skip logic: pushing to the branch works whether or not GitHub Pages is enabled, and the publish job summary says what the owner must enable (Settings > Pages > Source: Deploy from a branch, `gh-pages`, `/ (root)`). Real-stack Allure results are sanitized to pass/fail and timings before the publish input exists; real-stack traces stay in a 7-day CI artifact and never enter any `site-*` folder.
