# Pre-announce checks

Run these on your own machine before the repository is announced or linked publicly. They check that no secret and no real name ever entered the history or the pictures. Only the name checks (steps 2 and 3) have to stay local, because they read your private list at `%LOCALAPPDATA%\roleradar\private\denylist.txt` on Windows (format: [denylist.example.txt](denylist.example.txt)) and that list never leaves your machine. The gitleaks scan (step 1) has no private input; it runs here because it needs the full history, and it could run anywhere with a full clone.

Run everything from the repository root in PowerShell, on a full clone (not shallow).

## 1. Secrets in every commit

gitleaks, the same version and rules as the pre-commit hook, through its container image pinned by tag and digest. `--redact` keeps any secret it finds out of the output.

```powershell
docker run --rm -e GIT_CONFIG_COUNT=1 -e GIT_CONFIG_KEY_0=safe.directory -e GIT_CONFIG_VALUE_0=/repo -v "${PWD}:/repo:ro" ghcr.io/gitleaks/gitleaks:v8.30.1@sha256:c00b6bd0aeb3071cbcb79009cb16a60dd9e0a7c60e2be9ab65d25e6bc8abbb7f git /repo --config /repo/.gitleaks.toml --log-opts=--all --redact --no-banner
```

Read: the last lines say how many commits were scanned and either `no leaks found` (exit 0) or the findings with the secret replaced by `REDACTED` (exit 1). The repository is mounted read-only; the `GIT_CONFIG_*` variables only tell git inside the container to trust the mounted folder. If you prefer a local install, `gitleaks git . --config .gitleaks.toml --log-opts=--all --redact` with gitleaks v8.30.1 is equivalent.

## 2. Real names in every commit

The denylist script's history mode scans the content and message of every commit against your private list.

```powershell
pwsh ./scripts/denylist-check.ps1 -Mode History
```

## 3. Real names in the pictures

Binary files escape the text scan, so the image-metadata mode checks the printable strings and file names under `docs/images/`.

```powershell
pwsh ./scripts/denylist-check.ps1 -Mode ImageMetadata
```

Read steps 2 and 3 the same way: they print counts and file names only, never a term from your list. Exit 0 with zero matches is clean; exit 1 names the files that matched. A warning that `ROLERADAR_DENYLIST_PATH` is set means the run used another list, so a clean result proves nothing about real names.

## 4. Look at every picture

Open each image listed in [images/manifest.json](images/manifest.json), light and dark. The only invented text may be the demo seed's fictional names; no real name, address or account may appear. The procedure for regenerating them is in [demo.md](demo.md#regenerate-the-screenshots).

## If anything matches

Stop. Do not paste the output into an issue, a pull request or a chat. A match in the current files is fixed with a normal commit; a match in history needs a history rewrite and a force push, which is a separate, deliberate decision, followed by rotating any exposed secret. The `main` ruleset blocks force pushes ([repo-settings.md](repo-settings.md)), so a rewrite means lifting that rule deliberately for the push and restoring it straight after.

## Before announcing

- [ ] Steps 1 to 4 are clean on a full clone.
- [ ] CI is green on `main`, and the README badges and the reports site load.
- [ ] Private vulnerability reporting is enabled in the repository settings ([repo-settings.md](repo-settings.md)).
- [ ] The `v0.1.0` tag and release exist, and the release date is set in the [changelog](../CHANGELOG.md).
- [ ] Open Dependabot pull requests are merged or closed.
