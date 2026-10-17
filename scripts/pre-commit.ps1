[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('All', 'PathGuard', 'Denylist', 'Gitleaks')]
    [string]$Check = 'All',

    [Parameter()]
    [switch]$StagedOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    $trackedFiles = if ($StagedOnly) {
        @(git diff --cached --name-only --diff-filter=ACMR)
    } else {
        @(git ls-files)
    }

    if ($Check -in @('All', 'PathGuard')) {
        Write-Output "[pre-commit] Running RoleRadar path, extension, and workflow SHA guard..."

        # 1. Path & extension guard: forbid committing private directories, dumps, or backup archives
        $forbiddenPatterns = @(
            '^import-review/',
            '^private/',
            '^db/',
            'job-search-export',
            '\.dump$',
            '\.sql\.gz$',
            '\.7z$',
            '\.age$',
            '\.rrbak$',
            '(^|/)\.env$'
        )

        $violations = [System.Collections.Generic.List[string]]::new()
        foreach ($file in $trackedFiles) {
            if ([string]::IsNullOrWhiteSpace($file)) {
                continue
            }
            foreach ($pattern in $forbiddenPatterns) {
                if ($file -match $pattern) {
                    $violations.Add("Forbidden path or file pattern committed: '$file' (matched '$pattern')")
                }
            }
        }

        if ($violations.Count -gt 0) {
            foreach ($violation in $violations) {
                Write-Error $violation -ErrorAction Continue
            }
            throw "Path/pattern privacy guard failed with $($violations.Count) violation(s)."
        }
        Write-Output "[pre-commit] Path and extension guard passed ($($trackedFiles.Count) files checked)."

        # 2. GitHub Actions workflow SHA pin guard: verify every `uses:` in .github/workflows
        # is pinned to a 40-character commit SHA with a `# vX.Y.Z` version comment.
        $workflowDir = Join-Path -Path $repoRoot -ChildPath ".github\workflows"
        if (Test-Path -LiteralPath $workflowDir -PathType Container) {
            $workflowFiles = @(Get-ChildItem -LiteralPath $workflowDir -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
            $pinViolations = [System.Collections.Generic.List[string]]::new()
            $usesCount = 0
            $pinnedUsesPattern = '^\s*(?:-\s*)?uses:\s*[A-Za-z0-9_.-]+/[A-Za-z0-9_./-]+@[0-9a-f]{40}\s+#\s*v\d+\.\d+\.\d+\b'

            foreach ($wf in $workflowFiles) {
                $lines = @(Get-Content -LiteralPath $wf.FullName -Encoding UTF8)
                for ($i = 0; $i -lt $lines.Count; $i++) {
                    $line = $lines[$i]
                    if ($line -match '^\s*#') {
                        continue
                    }
                    if ($line -match '^\s*(?:-\s*)?uses:\s*') {
                        $usesCount++
                        if ($line -notmatch $pinnedUsesPattern) {
                            $lineNum = $i + 1
                            $pinViolations.Add("$($wf.Name):$lineNum -> '$($line.Trim())' must be a 40-character hex SHA with a '# vX.Y.Z' comment.")
                        }
                    }
                }
            }

            if ($pinViolations.Count -gt 0) {
                foreach ($pinViolation in $pinViolations) {
                    Write-Error $pinViolation -ErrorAction Continue
                }
                throw "Workflow action SHA pin check failed with $($pinViolations.Count) violation(s)."
            }
            Write-Output "[pre-commit] GitHub Actions SHA pin check passed ($usesCount 'uses:' reference(s) verified across $($workflowFiles.Count) workflow file(s))."
        }
    }

    if ($Check -in @('All', 'Denylist')) {
        # 3. Local private denylist check (%LOCALAPPDATA%\roleradar\private\denylist.txt)
        # Per Constitution Principle I: runs locally when the denylist file exists outside the repo;
        # skippable in CI via SKIP=roleradar-denylist. Prints a loud warning if missing locally.
        $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
        if ([string]::IsNullOrWhiteSpace($localAppData)) {
            $localAppData = $env:LOCALAPPDATA
        }

        $denylistPath = if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
            Join-Path -Path $localAppData -ChildPath "roleradar\private\denylist.txt"
        } else {
            "%LOCALAPPDATA%\roleradar\private\denylist.txt"
        }

        if (-not [string]::IsNullOrWhiteSpace($localAppData) -and (Test-Path -LiteralPath $denylistPath -PathType Leaf)) {
            $terms = @(
                Get-Content -LiteralPath $denylistPath -Encoding UTF8 |
                ForEach-Object { $_.Trim() } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#') }
            )

            $denylistHits = [System.Collections.Generic.List[string]]::new()
            foreach ($file in $trackedFiles) {
                if ([string]::IsNullOrWhiteSpace($file) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) {
                    continue
                }
                $content = Get-Content -LiteralPath $file -Raw -ErrorAction SilentlyContinue
                if ([string]::IsNullOrEmpty($content)) {
                    continue
                }
                foreach ($term in $terms) {
                    $escaped = [regex]::Escape($term)
                    if ($content -match "(?i)\b$escaped\b") {
                        $denylistHits.Add("File '$file' matched a private denylist term.")
                    }
                }
            }

            if ($denylistHits.Count -gt 0) {
                foreach ($hit in $denylistHits) {
                    Write-Error $hit -ErrorAction Continue
                }
                throw "Private denylist check failed with $($denylistHits.Count) hit(s)."
            }
            Write-Output "[pre-commit] External private denylist check passed ($($terms.Count) term(s) verified)."
        } else {
            Write-Warning "================================================================================"
            Write-Warning "LOUD WARNING: Local private denylist not found at '$denylistPath'."
            Write-Warning "Create '$denylistPath' (one real company/contact name per line) to enforce local real-name scanning."
            Write-Warning "================================================================================"
        }
    }

    if ($Check -in @('All', 'Gitleaks')) {
        # 4. Fail-closed Gitleaks secret scan (must fail rather than skip if gitleaks is unavailable)
        $gitleaksCmd = Get-Command -Name 'gitleaks' -ErrorAction SilentlyContinue
        if ($null -eq $gitleaksCmd) {
            throw "gitleaks CLI is not installed on PATH. Run via `pre-commit run --all-files` (which provisions the pinned gitleaks hook) or install gitleaks."
        }

        if ($StagedOnly) {
            & gitleaks protect --staged --config .gitleaks.toml --redact
        } else {
            & gitleaks detect --source . --config .gitleaks.toml --redact
        }
        if ($LASTEXITCODE -ne 0) {
            throw "gitleaks scan failed with exit code $LASTEXITCODE."
        }
        Write-Output "[pre-commit] gitleaks scan passed."
    }
} finally {
    Pop-Location
}
