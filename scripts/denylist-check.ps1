[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Staged', 'CommitMsg', 'PrePush')]
    [string]$Mode,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if ([string]::IsNullOrWhiteSpace($localAppData)) { $localAppData = $env:LOCALAPPDATA }
$denylistPath = Join-Path -Path "$localAppData" -ChildPath 'roleradar\private\denylist.txt'

if ([string]::IsNullOrWhiteSpace($localAppData) -or -not (Test-Path -LiteralPath $denylistPath -PathType Leaf)) {
    Write-Warning "LOUD WARNING: Local private denylist not found at '$denylistPath'. Create it (one term per line) to enforce local real-name scanning."
    return
}

$terms = @(
    Get-Content -LiteralPath $denylistPath -Encoding UTF8 |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#') }
)
if ($terms.Count -eq 0) { return }

$textToScan = switch ($Mode) {
    'Staged' {
        @(& git diff --cached -U0 --no-color 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') }) -join "`n"
    }
    'CommitMsg' {
        if ($RemainingArgs.Count -eq 0 -or -not (Test-Path -LiteralPath $RemainingArgs[0] -PathType Leaf)) {
            throw 'CommitMsg mode requires a valid commit message file path.'
        }
        @(Get-Content -LiteralPath $RemainingArgs[0] -Encoding UTF8 | Where-Object { -not $_.StartsWith('#') }) -join "`n"
    }
    'PrePush' {
        $fromRef = $env:PRE_COMMIT_FROM_REF
        $toRef = if ([string]::IsNullOrWhiteSpace($env:PRE_COMMIT_TO_REF)) { 'HEAD' } else { $env:PRE_COMMIT_TO_REF }
        $rawLines = if (-not [string]::IsNullOrWhiteSpace($fromRef) -and $fromRef -notmatch '^0+$') {
            @(& git log -p -U0 --no-color --format=%B "$fromRef..$toRef" 2>$null)
        } else {
            $commits = @(& git rev-list $toRef --not --remotes 2>$null)
            if ($commits.Count -gt 0) { @($commits | & git log --stdin --no-walk -p -U0 --no-color --format=%B 2>$null) } else { @() }
        }
        @($rawLines | Where-Object { -not $_.StartsWith('---') -and -not $_.StartsWith('+++') -and -not $_.StartsWith('-') }) -join "`n"
    }
}

foreach ($term in $terms) {
    if ($textToScan -match "(?i)\b$([regex]::Escape($term))\b") {
        throw "Private denylist check ($Mode) failed: input matched a forbidden private term."
    }
}
