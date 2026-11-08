# Contract: metrics record

One JSON object per line in `metrics/metrics.jsonl` on gh-pages. UTF-8, LF, no trailing comma, one record per commit SHA.

```json
{
  "schemaVersion": 1,
  "commit": "<40-hex sha>",
  "timestampUtc": "2026-01-01T00:00:00Z",
  "tests": { "unit": {"passed": 0, "failed": 0, "skipped": 0}, "architecture": {}, "contract": {}, "integration": {}, "web": {}, "e2e": {} },
  "coverage": { "overallLinePct": 0.0, "projects": { "RoleRadar.Domain": 0.0, "RoleRadar.Application": 0.0 } },
  "jobDurationsSeconds": { "<job name>": 0 },
  "mutation": { "web-lib": 0.0, "runDate": "2026-01-01T00:00:00Z", "commit": "<sha>" }
}
```

Rules:

- `mutation` is optional; when present it carries its own `runDate` and `commit` (the latest nightly), never the main run's.
- Any value that could not be read makes the append script fail. It never writes zero or a default.
- The example values above are placeholders showing shape only.
- Readers (the trends page) treat every field as untrusted text, skip lines that fail to parse and show a visible count of skipped lines.
