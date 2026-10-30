<#
.SYNOPSIS
    Automated review gate for RoleRadar branches (cross-platform pwsh).
.DESCRIPTION
    Clones the repository to a clean temporary directory, verifies locked restore,
    checks code formatting, builds with warnings as errors, executes the test suite,
    computes merged Domain+Application coverage, builds both container images,
    and outputs a concise summary (max 15 lines).
.PARAMETER Branch
    Branch name to verify (defaults to current active branch).
#>
[CmdletBinding()]
param(
    [string]$Branch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

if (-not $Branch) {
    $Branch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
}

$tempBase = [System.IO.Path]::GetTempPath()
$cloneGuid = [Guid]::NewGuid().ToString("N")
$tempDir = Join-Path $tempBase "rr-review-$cloneGuid"

$gateResults = [ordered]@{
    Restore = "FAIL"
    Format = "FAIL"
    Build = "FAIL"
    Tests = "FAIL"
    Coverage = "FAIL"
    Images = "FAIL"
}

$testCounts = [ordered]@{
    Unit = 0
    Arch = 0
    Contract = 0
    Integration = 0
}

$coveragePct = 0.0
$cliSizeMb = "N/A"
$mcpSizeMb = "N/A"

try {
    # 1. Clone repository to temp folder and overlay uncommitted working-tree changes
    & git clone --branch $Branch "file:///$($repoRoot -replace '\\', '/')" $tempDir --quiet 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        # Fallback to local file clone without file URI
        & git clone --branch $Branch $repoRoot $tempDir --quiet 2>&1 | Out-Null
    }

    $porcelain = & git -C $repoRoot status --porcelain -uall
    foreach ($entry in $porcelain) {
        if ([string]::IsNullOrWhiteSpace($entry) -or $entry.Length -lt 4) { continue }
        $xy = $entry.Substring(0, 2)
        $relPath = $entry.Substring(3).Trim().Trim('"')
        if ($relPath -match ' -> ') {
            $parts = $relPath -split ' -> '
            $oldDest = Join-Path $tempDir $parts[0].Trim('"')
            if (Test-Path -LiteralPath $oldDest) { Remove-Item -LiteralPath $oldDest -Force }
            $relPath = $parts[1].Trim('"')
        }
        $srcPath = Join-Path $repoRoot $relPath
        $dstPath = Join-Path $tempDir $relPath
        if ($xy -match 'D' -or -not (Test-Path -LiteralPath $srcPath)) {
            if (Test-Path -LiteralPath $dstPath) { Remove-Item -LiteralPath $dstPath -Force }
        } else {
            $dstParent = Split-Path -Parent $dstPath
            if (-not (Test-Path -LiteralPath $dstParent)) {
                New-Item -ItemType Directory -Path $dstParent -Force | Out-Null
            }
            Copy-Item -LiteralPath $srcPath -Destination $dstPath -Force
        }
    }

    Push-Location $tempDir

    # 2. Restore with --locked-mode
    $null = & dotnet restore RoleRadar.sln --locked-mode 2>&1
    if ($LASTEXITCODE -eq 0) { $gateResults.Restore = "PASS" } else { throw "Locked restore failed." }

    # 3. Format verification
    $null = & dotnet format RoleRadar.sln --verify-no-changes 2>&1
    if ($LASTEXITCODE -eq 0) { $gateResults.Format = "PASS" } else { throw "dotnet format check failed." }

    # 4. Build with warnings as errors
    $null = & dotnet build RoleRadar.sln -c Release --no-restore -warnaserror 2>&1
    if ($LASTEXITCODE -eq 0) { $gateResults.Build = "PASS" } else { throw "dotnet build (-warnaserror) failed." }

    # 5. Run test suite with coverage via scripts/test-all.ps1
    $null = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "verify-devstack-canary.ps1") -Action Record 2>&1
    $testOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-all.ps1 -NoBuild --collect:"XPlat Code Coverage" --results-directory ./TestResults 2>&1
    if ($LASTEXITCODE -eq 0) {
        $gateResults.Tests = "PASS"
    } else {
        $failedLines = ($testOutput | Where-Object { $_ -match 'Failed\s+' -or $_ -match 'Error Message:' }) -join '; '
        throw "Test execution failed: $failedLines"
    }

    $canaryOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "verify-devstack-canary.ps1") -Action Verify 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Dev stack canary check failed: $canaryOutput"
    }

    foreach ($line in $testOutput) {
        if ($line -match 'Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+(\d+).*RoleRadar[.]Tests[.]Unit[.]dll') {
            $testCounts.Unit = [int]$Matches[1]
        } elseif ($line -match 'Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+(\d+).*RoleRadar[.]Tests[.]Architecture[.]dll') {
            $testCounts.Arch = [int]$Matches[1]
        } elseif ($line -match 'Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+(\d+).*RoleRadar[.]Tests[.]Contract[.]dll') {
            $testCounts.Contract = [int]$Matches[1]
        } elseif ($line -match 'Passed!\s+-\s+Failed:\s+0,\s+Passed:\s+(\d+).*RoleRadar[.]Tests[.]Integration[.]dll') {
            $testCounts.Integration = [int]$Matches[1]
        }
    }

    # 6. Compute CI-equivalent merged Domain+Application coverage
    $null = & dotnet tool restore 2>&1
    $null = & dotnet tool run reportgenerator `
        "-reports:TestResults/**/coverage.cobertura.xml" `
        "-targetdir:TestResults/merged" `
        "-reporttypes:Cobertura" `
        "-assemblyfilters:+RoleRadar.Domain;+RoleRadar.Application" `
        "-filefilters:-*.g.cs" 2>&1

    $mergedXml = "TestResults/merged/Cobertura.xml"
    if (Test-Path -LiteralPath $mergedXml) {
        [xml]$doc = Get-Content -LiteralPath $mergedXml
        $rawLineRate = $doc.DocumentElement.GetAttribute("line-rate")
        $rate = [double]::Parse($rawLineRate, [System.Globalization.CultureInfo]::InvariantCulture)
        $coveragePct = [math]::Round($rate * 100.0, 2)
        if ($coveragePct -ge 85.0) {
            $gateResults.Coverage = "PASS"
        }
    }

    # 7. Build both container images
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $null = & docker build --target cli -t "roleradar-cli:review-$cloneGuid" . 2>&1
    $null = & docker build --target mcp -t "roleradar-mcp:review-$cloneGuid" . 2>&1

    $cliBytes = & docker image inspect "roleradar-cli:review-$cloneGuid" --format '{{.Size}}' 2>$null
    $mcpBytes = & docker image inspect "roleradar-mcp:review-$cloneGuid" --format '{{.Size}}' 2>$null

    if ($cliBytes -and $mcpBytes) {
        $gateResults.Images = "PASS"
        $cliSizeMb = "$([math]::Round([int64]$cliBytes / 1MB, 1)) MB"
        $mcpSizeMb = "$([math]::Round([int64]$mcpBytes / 1MB, 1)) MB"
    }

    # Cleanup review images
    $null = & docker rmi -f "roleradar-cli:review-$cloneGuid" "roleradar-mcp:review-$cloneGuid" 2>&1
    $ErrorActionPreference = $prevEap
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$allPassed = @($gateResults.Values | Where-Object { $_ -ne "PASS" }).Count -eq 0
$totalTests = $testCounts.Unit + $testCounts.Arch + $testCounts.Contract + $testCounts.Integration

# Exactly concise summary (<= 15 lines)
Write-Output "=== RoleRadar Review Gate Summary: $Branch ==="
Write-Output "1. Locked Restore: $($gateResults.Restore)"
Write-Output "2. Code Format:   $($gateResults.Format)"
Write-Output "3. WarningsBuild: $($gateResults.Build)"
Write-Output "4. Tests:         $($gateResults.Tests) (Unit: $($testCounts.Unit), Arch: $($testCounts.Arch), Contract: $($testCounts.Contract), Integration: $($testCounts.Integration) | Total: $totalTests)"
Write-Output "5. Domain+App Cov: $coveragePct% (Gate: >= 85%) -> $($gateResults.Coverage)"
Write-Output "6. Image Builds:  $($gateResults.Images) (CLI: $cliSizeMb, MCP: $mcpSizeMb)"
Write-Output "Verdict: $(if ($allPassed) { 'ALL GATES PASSED' } else { 'GATE FAILURE' })"
Write-Output "================================================="

if (-not $allPassed) {
    exit 1
}
