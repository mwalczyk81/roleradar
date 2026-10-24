<#
.SYNOPSIS
    Checks MongoDB Database Tools release status against the pinned version in Dockerfile.
.DESCRIPTION
    Reads the latest release metadata from downloads.mongodb.org (release.json and full.json)
    and compares against the pinned version in the Dockerfile.
.PARAMETER DockerfilePath
    Path to Dockerfile (defaults to repo root Dockerfile).
.PARAMETER ReleaseJsonUrl
    URL for latest release metadata.
.PARAMETER FullJsonUrl
    URL for historical versions catalog.
#>
[CmdletBinding()]
param(
    [string]$DockerfilePath = "Dockerfile",
    [string]$ReleaseJsonUrl = "https://downloads.mongodb.org/tools/db/release.json",
    [string]$FullJsonUrl = "https://downloads.mongodb.org/tools/db/full.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$fullDockerfilePath = Join-Path $repoRoot $DockerfilePath

if (-not (Test-Path -LiteralPath $fullDockerfilePath)) {
    throw "Dockerfile not found at: $fullDockerfilePath"
}

$dockerfileContent = Get-Content -LiteralPath $fullDockerfilePath -Raw
$pinnedMatch = [regex]::Match($dockerfileContent, 'mongodb-database-tools-ubuntu2404-\$\{TOOL_ARCH\}-([0-9]+\.[0-9]+\.[0-9]+)\.tgz')

if (-not $pinnedMatch.Success) {
    throw "Could not find MongoDB Database Tools version pin in $fullDockerfilePath"
}

$pinnedVersion = $pinnedMatch.Groups[1].Value
Write-Output "[check-tools-release] Pinned version in Dockerfile: $pinnedVersion"

$latestVersion = $null
$historicalCount = 0

try {
    $releaseJson = Invoke-RestMethod -Uri $ReleaseJsonUrl -TimeoutSec 10 -ErrorAction Stop
    if ($releaseJson -and $releaseJson.versions -and $releaseJson.versions.Count -gt 0) {
        $latestVersion = $releaseJson.versions[0].version
    }
}
catch {
    Write-Warning "Could not fetch release.json from $ReleaseJsonUrl : $_"
}

try {
    $fullJson = Invoke-RestMethod -Uri $FullJsonUrl -TimeoutSec 10 -ErrorAction Stop
    if ($fullJson -and $fullJson.versions) {
        $historicalCount = $fullJson.versions.Count
    }
}
catch {
    Write-Warning "Could not fetch full.json from $FullJsonUrl : $_"
}

if (-not $latestVersion) {
    Write-Output "[check-tools-release] Operating in offline mode or download unavailable. Validated pinned version: $pinnedVersion"
    exit 0
}

Write-Output "[check-tools-release] Latest stable version upstream: $latestVersion (Catalog records: $historicalCount)"

if ($pinnedVersion -eq $latestVersion) {
    Write-Output "[check-tools-release] Status: UP TO DATE. Pinned version $pinnedVersion matches upstream release."
} else {
    Write-Output "[check-tools-release] Status: UPDATE AVAILABLE. Upstream version is $latestVersion (currently pinned: $pinnedVersion)."
}
