# Release text for v0.1.0 (draft to paste)

Paste everything below the line into the GitHub release for tag `v0.1.0`. Before publishing, set the release date in `CHANGELOG.md` (the `## [0.1.0] - TBD` heading) and add the feature 012 pull request number where `(012)` appears there.

---

## RoleRadar v0.1.0

The first release of RoleRadar: long-term memory for AI agents over MCP, and a job-search tracker with a web UI, on one local data store. It is local-first by design and runs entirely on your machine.

### Try it in four steps

You need Docker with Compose v2.

```bash
git clone https://github.com/mwalczyk81/roleradar.git
cd roleradar
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml up --build --wait
docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml --profile tools run --rm demo-cli demo credentials
```

Then open <https://localhost:5103> and sign in with the printed username, password and a TOTP code. The certificates are self-signed, so accept the browser warning once for each origin (`localhost:5103` and `identity.localhost:5101`). All demo data is fictional. Remove the demo with `docker compose -p roleradar-demo -f deploy/demo/docker-compose.yml down -v`.

### What is in it

- **Agent memory over MCP**: twelve stdio tools for Markdown notes and the job-search tracker, and none of them deletes anything.
- **Job-search tracker**: companies, deduplicated postings, a disposition state machine, applications with timelines, criteria and search runs, plus an import of an existing tracker.
- **Web app**: a server-rendered UI with password and TOTP sign-in, list pages read through an owner-scoped OData seam, and an Insights dashboard whose every count links to the list it counts.
- **Durable and private**: loopback-only MongoDB, an append-only audit trail, and encrypted backups with a verified two-pass restore.
- **Built in the open**: spec-driven development with a constitution, about forty ADRs, architecture tests, a review gate, and published test, coverage and mutation reports. AI agents wrote much of the code under that process, with a human review at every phase.

Full list: [CHANGELOG.md](https://github.com/mwalczyk81/roleradar/blob/main/CHANGELOG.md). Reports: <https://mwalczyk81.github.io/roleradar/>.

### Limits

Single owner, no hosted version (hosting is parked), and pre-1.0, so interfaces can still change.
