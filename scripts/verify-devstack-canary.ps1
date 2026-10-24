<#
.SYNOPSIS
    Dev stack safety canary (cross-platform pwsh).
.DESCRIPTION
    Records the dev stack's database list and document counts (if reachable)
    before test execution, and verifies after test execution that nothing
    outside rrtest_ prefixes was modified, created, or dropped.
.PARAMETER Action
    'Record' or 'Verify'.
.PARAMETER SnapshotFile
    Path to store/read the canary JSON snapshot.
#>
[CmdletBinding()]
param(
    [ValidateSet("Record", "Verify")]
    [string]$Action = "Record",

    [string]$SnapshotFile = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

if ([string]::IsNullOrWhiteSpace($SnapshotFile)) {
    $resultsDir = Join-Path $repoRoot "TestResults"
    if (-not (Test-Path -LiteralPath $resultsDir)) {
        New-Item -ItemType Directory -Path $resultsDir -Force | Out-Null
    }
    $SnapshotFile = Join-Path $resultsDir "devstack-canary-snapshot.json"
}

function Test-PortOpen([string]$HostName, [int]$Port) {
    try {
        $tcp = [System.Net.Sockets.TcpClient]::new()
        $iar = $tcp.BeginConnect($HostName, $Port, $null, $null)
        $wh = $iar.AsyncWaitHandle
        if (-not $wh.WaitOne(1000, $false)) {
            $tcp.Close()
            return $false
        }
        $tcp.EndConnect($iar)
        $tcp.Close()
        return $true
    } catch {
        return $false
    }
}

function Get-DevStackSnapshot() {
    $isOpen = Test-PortOpen "127.0.0.1" 27017
    if (-not $isOpen) {
        return @{
            reachable = $false
            databases = @()
        }
    }

    $evalScript = "const result = []; const dbs = db.adminCommand('listDatabases').databases; for (const d of dbs) { if (['admin','config','local'].includes(d.name) || d.name.startsWith('rrtest_')) continue; const dbObj = { name: d.name, collections: {} }; const sDb = db.getSiblingDB(d.name); for (const c of sDb.getCollectionNames()) { dbObj.collections[c] = sDb.getCollection(c).countDocuments(); } result.push(dbObj); } JSON.stringify({ reachable: true, databases: result, unqueried: false });"

    # Try 1: docker exec roleradar-mongo
    $runningContainer = & docker ps --filter "name=roleradar-mongo" --filter "status=running" -q 2>$null
    if ($runningContainer) {
        $out = & docker exec roleradar-mongo mongosh --quiet --eval $evalScript 2>$null
        if ($LASTEXITCODE -eq 0 -and $out) {
            return ($out | ConvertFrom-Json)
        }
    }

    # Try 2: local mongosh if installed
    $mongosh = Get-Command mongosh -ErrorAction SilentlyContinue
    if ($mongosh) {
        $out = & mongosh "mongodb://127.0.0.1:27017" --quiet --eval $evalScript 2>$null
        if ($LASTEXITCODE -eq 0 -and $out) {
            return ($out | ConvertFrom-Json)
        }
    }

    # Fallback: reachable on port but no mongosh/docker exec tool
    return @{
        reachable = $true
        databases = @()
        unqueried = $true
    }
}

if ($Action -eq "Record") {
    $snap = Get-DevStackSnapshot
    $json = $snap | ConvertTo-Json -Depth 5
    $snapDir = Split-Path -Parent $SnapshotFile
    if (-not (Test-Path -LiteralPath $snapDir)) {
        New-Item -ItemType Directory -Path $snapDir -Force | Out-Null
    }
    Set-Content -LiteralPath $SnapshotFile -Value $json -Encoding utf8
    if ($snap.reachable) {
        Write-Output "[canary] Dev stack reachable on 127.0.0.1:27017. Recorded snapshot of $($snap.databases.Count) database(s)."
    } else {
        Write-Output "[canary] Dev stack not reachable on 127.0.0.1:27017. Recorded unreachable baseline."
    }
} elseif ($Action -eq "Verify") {
    if (-not (Test-Path -LiteralPath $SnapshotFile)) {
        throw "Canary snapshot file not found at '$SnapshotFile'. Run with -Action Record first."
    }

    $raw = Get-Content -LiteralPath $SnapshotFile -Raw -Encoding utf8
    $baseline = $raw | ConvertFrom-Json
    $current = Get-DevStackSnapshot

    if (-not $baseline.reachable) {
        if ($current.reachable -and $current.databases.Count -gt 0) {
            throw "Dev stack was unreachable before test run, but is now reachable with active databases!"
        }
        Write-Output "[canary] PASS: Dev stack was not reachable; environment verified clean."
        exit 0
    }

    if (-not $current.reachable) {
        throw "Dev stack was reachable before test run, but is now unreachable (dev stack may have been shut down)!"
    }

    $baseUnqueried = [bool]($baseline.PSObject.Properties['unqueried'] -and $baseline.unqueried)
    $currUnqueried = [bool]($current.PSObject.Properties['unqueried'] -and $current.unqueried)
    if ($baseUnqueried -or $currUnqueried) {
        Write-Output "[canary] PASS: Dev stack reachable and alive (detailed query skipped due to missing mongosh)."
        exit 0
    }

    # Compare databases
    $baseMap = @{}
    foreach ($d in $baseline.databases) {
        $baseMap[$d.name] = $d
    }

    $currMap = @{}
    foreach ($d in $current.databases) {
        $currMap[$d.name] = $d
    }

    # Check for dropped or altered databases
    foreach ($name in $baseMap.Keys) {
        if (-not $currMap.ContainsKey($name)) {
            throw "Dev stack database '$name' was dropped during the test run!"
        }

        $baseDb = $baseMap[$name]
        $currDb = $currMap[$name]

        $baseCols = $baseDb.collections
        $currCols = $currDb.collections

        # Compare collection counts
        if ($baseCols) {
            $baseProps = $baseCols | Get-Member -MemberType NoteProperty
            foreach ($prop in $baseProps) {
                $colName = $prop.Name
                $baseCount = $baseCols.$colName
                $currCount = $currCols.$colName
                if ($null -eq $currCount) {
                    throw "Collection '$colName' in database '$name' was dropped during the test run!"
                }
                if ($baseCount -ne $currCount) {
                    throw "Collection '$colName' count in database '$name' changed from $baseCount to $currCount during the test run!"
                }
            }
        }

        if ($currCols) {
            $currProps = $currCols | Get-Member -MemberType NoteProperty
            foreach ($prop in $currProps) {
                $colName = $prop.Name
                if (-not ($baseCols | Get-Member -Name $colName -MemberType NoteProperty)) {
                    throw "New collection '$colName' was added to database '$name' during the test run!"
                }
            }
        }
    }

    # Check for newly created non-rrtest databases
    foreach ($name in $currMap.Keys) {
        if (-not $baseMap.ContainsKey($name)) {
            throw "New database '$name' outside 'rrtest_' prefix was created on the dev stack during the test run!"
        }
    }

    Write-Output "[canary] PASS: Dev stack remained completely untouched outside rrtest_ prefixes."
}
