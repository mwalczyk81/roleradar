# Quickstart: validating 009 UI polish and theming

Run from the repository root in Git Bash or PowerShell as shown. Nothing here touches the developer's running stack, Mongo or `deploy/.env`. Everything uses fictional fixtures. Report numbers from the command output.

## 0. Prerequisites

- Node 22, pnpm (version in the root `package.json`), Docker Desktop (for the Linux gate and the Identity image test).
- Branch `009-ui-polish-theming`. Working tree changes are left uncommitted for review.

## 1. Baseline and guards (Phase 1)

```powershell
$env:ROLERADAR_VISUAL_DIR = "$env:TEMP\roleradar-visual\baseline"
pnpm --filter web exec playwright test tests/e2e/visual-baseline.spec.ts --update-snapshots
pnpm --filter web run test -- inline-style-ratchet
```

Expected: screenshots written under `ROLERADAR_VISUAL_DIR` only (check `git status` shows none); ratchet total 203; the guard fixture fails a deliberately injected CSP violation (throwaway, removed).

## 2. Compare after each conversion slice (Phase 3)

```powershell
$env:ROLERADAR_VISUAL_DIR = "$env:TEMP\roleradar-visual\baseline"
pnpm --filter web exec playwright test tests/e2e/visual-baseline.spec.ts
```

Expected: every route, light and dark, matches the baseline; the census count on the converted routes falls by the number of removed app styles that render there; ceiling lowered to the schedule in [contracts/inline-style-ratchet.md](./contracts/inline-style-ratchet.md).

## 3. The phase gate (every phase)

```bash
pnpm --filter web run typecheck
pnpm --filter web run lint
pnpm --filter web run test
pnpm --filter web run build
pnpm --filter web run test:e2e
bash scripts/linux-web-check.sh
pwsh ./scripts/test-fast.ps1
pre-commit run --all-files
```

Phases 5 and 6 add `pwsh ./scripts/review-gate.ps1`.

## 4. Theme (Phase 2)

- `pnpm --filter web run test -- theme root use-theme`.
- In e2e: request any page with `Cookie: roleradar_theme=dark` and JavaScript disabled; the document element has `data-theme="dark"`. See [contracts/theme-module.md](./contracts/theme-module.md).

## 5. Polish checks (Phase 4)

`pnpm --filter web exec playwright test tests/e2e/polish.spec.ts` asserts, in both themes: role-title link colours and contrast, placeholder contrast, Insights tile heights per row, one "See all postings" link inside its section, Markdown table region width, h4 to h6 computed style, search-run chart text and table view, and axe with the shared reduced-motion helper. Items marked as judgement calls (J1 to J7) are reviewed by eye: open Insights, a Postings list and a wide-table note in light and dark.

## 6. Identity (Phase 5)

Automated:

```powershell
dotnet build RoleRadar.sln -c Release -v q --nologo -warnaserror
pwsh ./scripts/test-fast.ps1
pwsh ./scripts/review-gate.ps1
```

Expected: the build copies `web/app/ds` assets into `src/RoleRadar.Identity/wwwroot/ds/`; `DockerIgnoreCoverageTests` covers `.css` and `.js`; the Docker image test fetches `/ds/tokens.css`, `/css/identity.css`, one font and `/js/theme.js`; Login and Logout HTML have no `<style>` and no `style=`; the `/Account` response carries the CSP in [contracts/identity-assets-and-csp.md](./contracts/identity-assets-and-csp.md).

Manual (unchanged): run section 5a of [the 005a quickstart](../005a-api-and-login/quickstart.md) (authorization code with PKCE and TOTP). It must complete exactly as before. Look at the Login page in light and dark system themes.

## 7. CSP flip (Phase 6)

```powershell
pnpm --filter web run test -- kit-style-emitters
```

- Count 0: the flip runs and the full e2e suite passes with the guard on every route in both themes.
- Count above 0 (today 150 in 27 files): `kit-style-emitters.md` is written with the per-file list and the phase ends; the CSP is unchanged.
