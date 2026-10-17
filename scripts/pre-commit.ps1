[CmdletBinding()]
param(
    [Parameter()]
    [switch]$StagedOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location -LiteralPath $repoRoot
try {
    Write-Output "[pre-commit] Running RoleRadar privacy and secret guardrails..."

    # 1. Path & extension guard: forbid committing private directories, dumps, or backup archives
    $trackedFiles = if ($StagedOnly) {
        @(git diff --cached --name-only --diff-filter=ACMR)
    } else {
        @(git ls-files)
    }

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

    # 2. Local private denylist check (%LOCALAPPDATA%\roleradar\private\denylist.txt)
    # Per Constitution Principle I: runs locally when the denylist file exists outside the repo;
    # CI never needs or stores the private denylist.
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = $env:LOCALAPPDATA
    }

    if (-not [string]::IsNullOrWhiteSpace($localAppData)) {
        $denylistPath = Join-Path -Path $localAppData -ChildPath "roleradar\private\denylist.txt"
        if (Test-Path -LiteralPath $denylistPath -PathType Leaf) {
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
            Write-Output "[pre-commit] No external denylist found at '$denylistPath' (optional local check skipped)."
        }
    }

    # 3. Gitleaks secret scan (if gitleaks binary is installed on PATH)
    $gitleaksCmd = Get-Command -Name 'gitleaks' -ErrorAction SilentlyContinue
    if ($null -ne $gitleaksCmd) {
        if ($StagedOnly) {
            & gitleaks protect --staged --config .gitleaks.toml --redact
        } else {
            & gitleaks detect --source . --config .gitleaks.toml --redact
        }
        if ($LASTEXITCODE -ne 0) {
            throw "gitleaks scan failed with exit code $LASTEXITCODE."
        }
        Write-Output "[pre-commit] gitleaks scan passed."
    } else {
        Write-Output "[pre-commit] gitleaks CLI not found on PATH; path/pattern and denylist checks completed."
    }

    Write-Output "[pre-commit] All privacy and secret checks passed."
} finally {
    Pop-Location
}
