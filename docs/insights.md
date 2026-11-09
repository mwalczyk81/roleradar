# Insights Page

The demo shows this page over fictional data: see [demo.md](demo.md).

The web app has a read-only Insights page at `/insights` (signed-in owner only). It shows two funnels, each over one cohort: postings first seen in the window (leads, reviewed, applied-to) and applications applied in the window (applied, responded, interviewed, offers). Stat tiles give the application, screen-out, interview, rejection, offer and ghost rates plus open applications and median days to first response, with applications per week and search-run yield as charts that each have a table view. Every percentage is shown as "x of y", and a denominator under 10 is marked "small sample".

- **Windows**: the default is the last 90 days (90 whole UTC days ending today); the toggle switches to all time. The window is in the page address, so a view can be shared.
- **Drill-down**: every count links to the Postings or Applications list with the same filter and window, so the number on the page and the number of rows in the list agree. The dashboard and its links are built from one shared filter module.
- **First run on existing data**: flag-dependent numbers show "needs backfill" until the owner runs the `reconcile-read-models` steps in [docs/operations/reconcile-read-models.md](operations/reconcile-read-models.md).
- **Notes**: the note detail page renders the body as locked-down Markdown (no raw HTML, no images, http and https links only).

Validation steps are in [specs/007-insights-dashboard/quickstart.md](../specs/007-insights-dashboard/quickstart.md). Decisions are recorded in ADRs [0029](adr/0029-insights-counts-via-shared-lens-module.md) (counts and lens module), [0030](adr/0030-insights-stats-read-in-memory.md) (stats read), [0031](adr/0031-insights-derived-read-model-flags.md) (derived flags) and [0032](adr/0032-markdown-rendering-of-notes.md) (Markdown).
