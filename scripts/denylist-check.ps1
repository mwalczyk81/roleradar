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
    $msg = "LOUD WARNING: Local private denylist not found at '$denylistPath'. Create it (one term per line) or set ROLERADAR_ALLOW_NO_DENYLIST=1 to bypass."
    if ($env:ROLERADAR_ALLOW_NO_DENYLIST -eq '1') { Write-Warning $msg; return }
    throw $msg
}

$terms = @(
    Get-Content -LiteralPath $denylistPath -Encoding UTF8 |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#') }
)
if ($terms.Count -eq 0) {
    throw "Private denylist at '$denylistPath' exists but contains no active terms."
}

$textToScan = switch ($Mode) {
    'Staged' {
        @(& git diff --cached -U0 --no-color 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') } | ForEach-Object { $_.Substring(1) }) -join "`n"
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
        if (-not [string]::IsNullOrWhiteSpace($fromRef) -and $fromRef -notmatch '^0+$') {
            $msgs = @(& git log --no-patch --format=%B "$fromRef..$toRef" 2>$null)
            $diffs = @(& git log -p -U0 --no-color --format= "$fromRef..$toRef" 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') } | ForEach-Object { $_.Substring(1) })
        } else {
            $revs = @(& git rev-list $toRef --not --remotes 2>$null)
            $msgs = if ($revs.Count -gt 0) { @($revs | & git log --stdin --no-walk --no-patch --format=%B 2>$null) } else { @() }
            $diffs = if ($revs.Count -gt 0) { @($revs | & git log --stdin --no-walk -p -U0 --no-color --format= 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') } | ForEach-Object { $_.Substring(1) }) } else { @() }
        }
        @($msgs + $diffs) -join "`n"
    }
}

foreach ($term in $terms) {
    if ($textToScan -match "(?i)(?<!\w)$([regex]::Escape($term))(?!\w)") {
        throw "Private denylist check ($Mode) failed: input matched a forbidden private term."
    }
}
