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
    [string]$Branch,
    [switch]$IncludeE2E
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")

# Reads the last "Failed: n, Passed: n, Skipped: n" summary line of a dotnet test run.
function Get-TestCount {
    param([object[]]$Output)
    $result = [pscustomobject]@{ Failed = -1; Passed = 0; Skipped = -1 }
    foreach ($line in $Output) {
        if ("$line" -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)') {
            $result.Failed = [int]$Matches[1]
            $result.Passed = [int]$Matches[2]
            $result.Skipped = [int]$Matches[3]
        }
    }
    return $result
}

if (-not $Branch) {
    $Branch = (git -C $repoRoot rev-parse --abbrev-ref HEAD).Trim()
}

$tempBase = [System.IO.Path]::GetTempPath()
$cloneGuid = [Guid]::NewGuid().ToString("N")
$tempDir = Join-Path $tempBase "rr-review-$cloneGuid"

$gateResults = [ordered]@{
    Restore = "FAIL"
    Format = "FAIL"
    ScriptLint = "FAIL"
    Build = "FAIL"
    Typecheck = "FAIL"
    Tests = "FAIL"
    Coverage = "FAIL"
    Images = "FAIL"
    Scan = "FAIL"
    ImageLane = "FAIL"
    WebSmoke = "FAIL"
}

# Per-lane results parsed from the test run's own "Failed: n, Passed: n, Skipped: n" lines (null until parsed).
$laneCounts = [ordered]@{
    Unit = $null
    Architecture = $null
    Contract = $null
    Integration = $null
}

$coveragePct = 0.0
$coverageSummary = $null
$scanReports = 0
$scanCritical = 0
$scanTestCounts = [pscustomobject]@{ Failed = -1; Passed = 0; Skipped = -1 }
$imageLaneCounts = [pscustomobject]@{ Failed = -1; Passed = 0; Skipped = -1 }
# Image sizes in bytes as reported by docker image inspect (null until measured); sizes are rendered from the summary.
$cliBytes = $null
$mcpBytes = $null
$identityBytes = $null
$webBytes = $null
$apiBytes = $null

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

    # 3b. Script lint: PSScriptAnalyzer directly, with the settings file CI uses (Severity Warning and Error).
    # -EnableExit makes the exit code the number of findings. CI pins the module at 1.25.0.
    $lintOutput = & pwsh -NoProfile -Command "Invoke-ScriptAnalyzer -Path scripts -Recurse -Settings scripts/psscriptanalyzer-settings.psd1 -EnableExit" 2>&1
    if ($LASTEXITCODE -eq 0) { $gateResults.ScriptLint = "PASS" } else { throw "PSScriptAnalyzer failed (module 1.25.0 required): $(@($lintOutput) -join "`n")" }

    # 4. Build with warnings as errors, the way CI builds: with no Mongo reachable. The API project generates its OpenAPI
    # document at build time by starting the API (dotnet-getdocument), and anything the API logs at warn or above while it
    # starts is written to stderr, which MSBuild's Exec task turns into a build error (MSB3077). With the dev Mongo up at its
    # default address that start-up is quiet, so a regression there passes this gate and fails CI. The connection string points
    # at a port nothing listens on, for this command only, and the previous value (or its absence) is restored afterwards.
    # --no-incremental makes sure the API project is really built, so the generator really runs.
    $mongoVariable = "ROLERADAR_MONGO_CONNECTION_STRING"
    $previousMongo = [Environment]::GetEnvironmentVariable($mongoVariable)
    [Environment]::SetEnvironmentVariable($mongoVariable, "mongodb://127.0.0.1:1/?directConnection=true")
    try {
        $buildOutput = & dotnet build RoleRadar.sln -c Release --no-restore --no-incremental -warnaserror 2>&1
        $buildExitCode = $LASTEXITCODE
    }
    finally {
        [Environment]::SetEnvironmentVariable($mongoVariable, $previousMongo)
    }
    if ($buildExitCode -eq 0) {
        $gateResults.Build = "PASS"
    } else {
        $buildErrors = @($buildOutput | ForEach-Object { "$_" } | Where-Object { $_ -match "error" } | Select-Object -Unique | Select-Object -First 10)
        throw "dotnet build (-warnaserror, no Mongo reachable) failed: $($buildErrors -join "`n")"
    }

    # 5. Web typecheck
    if (Test-Path (Join-Path $repoRoot "web/package.json")) {
        $typecheckOutput = & pnpm --dir (Join-Path $repoRoot "web") run typecheck 2>&1
        if ($LASTEXITCODE -eq 0) {
            $gateResults.Typecheck = "PASS"
        } else {
            throw "Web typecheck failed: $typecheckOutput"
        }
    } else {
        $gateResults.Typecheck = "SKIP"
    }

    # 6. Run test suite with coverage via scripts/test-all.ps1. This is the default run: it excludes only the lanes that
    # need something it does not have (scan: the Trivy reports, web-container and real-stack: their own steps below). The
    # images lane is deliberately NOT excluded: those classes also run here in host mode, as they always did, and run
    # again in step 7c against the review images (image mode). Docker-only classes therefore run twice, on purpose.
    $null = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "verify-devstack-canary.ps1") -Action Record 2>&1
    $testOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-all.ps1 -NoBuild --collect:"XPlat Code Coverage" --results-directory ./TestResults --filter "Lane!=scan&Lane!=web-container&Lane!=real-stack" 2>&1
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
        if ("$line" -match '(?:Passed|Failed)!\s+-\s+Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+).*RoleRadar[.]Tests[.](Unit|Architecture|Contract|Integration)[.]dll') {
            $laneCounts[$Matches[4]] = [ordered]@{ passed = [int]$Matches[2]; failed = [int]$Matches[1]; skipped = [int]$Matches[3] }
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
        $rawBranchRate = $doc.DocumentElement.GetAttribute("branch-rate")
        $branchRate = 0.0
        $branchKnown = [double]::TryParse($rawBranchRate, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$branchRate)
        $coverageProjects = [ordered]@{}
        foreach ($package in @($doc.DocumentElement.SelectNodes("//package"))) {
            $packageLine = 0.0
            $packageBranch = 0.0
            $lineKnown = [double]::TryParse($package.GetAttribute("line-rate"), [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$packageLine)
            $packageBranchKnown = [double]::TryParse($package.GetAttribute("branch-rate"), [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$packageBranch)
            $coverageProjects[$package.GetAttribute("name")] = [ordered]@{
                linePct = if ($lineKnown) { [math]::Round($packageLine * 100.0, 1) } else { $null }
                branchPct = if ($packageBranchKnown) { [math]::Round($packageBranch * 100.0, 1) } else { $null }
            }
        }
        $coverageSummary = [ordered]@{
            overallLinePct = $coveragePct
            overallBranchPct = if ($branchKnown) { [math]::Round($branchRate * 100.0, 1) } else { $null }
            projects = $coverageProjects
        }
        if ($coveragePct -ge 85.0) {
            $gateResults.Coverage = "PASS"
        }
    }

    # 7. Build all container images (CLI, MCP, Identity, Web, API)
    if ($IncludeE2E) {
        $gateResults['RealStackE2E'] = "FAIL"
    }

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $null = & docker build --target cli -t "roleradar-cli:review-$cloneGuid" . 2>&1
    $null = & docker build --target mcp -t "roleradar-mcp:review-$cloneGuid" . 2>&1
    $null = & docker build -f deploy/Dockerfile.identity -t "roleradar-identity:review-$cloneGuid" . 2>&1
    $null = & docker build -f deploy/Dockerfile.web -t "roleradar-web:review-$cloneGuid" . 2>&1
    $null = & docker build -f deploy/Dockerfile.api -t "roleradar-api:review-$cloneGuid" . 2>&1

    $cliBytes = & docker image inspect "roleradar-cli:review-$cloneGuid" --format '{{.Size}}' 2>$null
    $mcpBytes = & docker image inspect "roleradar-mcp:review-$cloneGuid" --format '{{.Size}}' 2>$null
    $identityBytes = & docker image inspect "roleradar-identity:review-$cloneGuid" --format '{{.Size}}' 2>$null
    $webBytes = & docker image inspect "roleradar-web:review-$cloneGuid" --format '{{.Size}}' 2>$null
    $apiBytes = & docker image inspect "roleradar-api:review-$cloneGuid" --format '{{.Size}}' 2>$null

    if ($cliBytes -and $mcpBytes -and $identityBytes -and $webBytes -and $apiBytes) {
        $gateResults.Images = "PASS"
    }

    # 7b. Container scan (feature 010, ADR 0036). Trivy runs on the images built above with the same pinned image and flags
    # as CI (failing severity CRITICAL, --ignore-unfixed) and writes the reports the scan test reads. The mounts are plain
    # paths, proven from pwsh on Windows; from Git Bash the CI form needs MSYS_NO_PATHCONV=1 and pwd -W.
    $scanDir = Join-Path $tempDir "TestResults"
    $trivyCache = Join-Path $tempDir ".trivy-cache"
    New-Item -ItemType Directory -Force -Path $scanDir, $trivyCache | Out-Null
    $scanTargets = [ordered]@{
        cli = "roleradar-cli:review-$cloneGuid"
        mcp = "roleradar-mcp:review-$cloneGuid"
        identity = "roleradar-identity:review-$cloneGuid"
        api = "roleradar-api:review-$cloneGuid"
        web = "roleradar-web:review-$cloneGuid"
    }
    foreach ($target in $scanTargets.Keys) {
        # Trivy exits 1 on a CRITICAL finding but still writes the report; the scan test below is what judges it.
        $null = & docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -v "${scanDir}:/TestResults" -v "${trivyCache}:/root/.cache" aquasec/trivy:0.59.1 image --list-all-pkgs --severity=CRITICAL --exit-code 1 --ignore-unfixed --format json --output "/TestResults/trivy-report-$target.json" $scanTargets[$target] 2>&1
        $reportPath = Join-Path $scanDir "trivy-report-$target.json"
        if (Test-Path -LiteralPath $reportPath) {
            $scanReports++
            # Only for the summary line; the scan test below is what judges the reports. A result entry without
            # Vulnerabilities (or a clean report without Results) is normal Trivy output, so probe the properties.
            try {
                $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
                $resultsProperty = $report.PSObject.Properties['Results']
                $results = if ($null -ne $resultsProperty) { @($resultsProperty.Value) } else { @() }
                foreach ($result in $results) {
                    $vulnerabilitiesProperty = $result.PSObject.Properties['Vulnerabilities']
                    if ($null -eq $vulnerabilitiesProperty) { continue }
                    foreach ($vulnerability in @($vulnerabilitiesProperty.Value)) {
                        if ($vulnerability.Severity -eq "CRITICAL") { $scanCritical++ }
                    }
                }
            } catch {
                Write-Warning "Could not read counts from ${reportPath}: $($_.Exception.Message)"
            }
        }
    }

    $env:ROLERADAR_REQUIRE_SCAN = "1"
    $scanTestOutput = & dotnet test tests/RoleRadar.Tests.Integration -c Release --no-build --filter "Lane=scan" 2>&1
    $scanTestExit = $LASTEXITCODE
    Remove-Item Env:\ROLERADAR_REQUIRE_SCAN -ErrorAction SilentlyContinue
    $scanTestCounts = Get-TestCount $scanTestOutput
    if ($scanTestExit -eq 0 -and $scanTestCounts.Failed -eq 0 -and $scanTestCounts.Passed -ge 1 -and $scanTestCounts.Skipped -eq 0 -and $scanReports -eq $scanTargets.Count) {
        $gateResults.Scan = "PASS"
    } else {
        Write-Warning "Container scan failed, skipped or incomplete (reports=$scanReports, passed=$($scanTestCounts.Passed), skipped=$($scanTestCounts.Skipped), exit=$scanTestExit): $($scanTestOutput -join "`n")"
    }

    # 7c. Images lane (Lane=images) against the review images, the same ones that were just scanned.
    $imageLaneOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-images.ps1 -CliImage "roleradar-cli:review-$cloneGuid" -McpImage "roleradar-mcp:review-$cloneGuid" -IdentityImage "roleradar-identity:review-$cloneGuid" -WebImage "roleradar-web:review-$cloneGuid" -Filter "Lane=images" -NoBuild 2>&1
    $imageLaneExit = $LASTEXITCODE
    $imageLaneCounts = Get-TestCount $imageLaneOutput
    if ($imageLaneExit -eq 0 -and $imageLaneCounts.Failed -eq 0 -and $imageLaneCounts.Passed -ge 1 -and $imageLaneCounts.Skipped -eq 0) {
        $gateResults.ImageLane = "PASS"
    } else {
        Write-Warning "Images lane failed, skipped a test or ran no tests (passed=$($imageLaneCounts.Passed), failed=$($imageLaneCounts.Failed), skipped=$($imageLaneCounts.Skipped), exit=$imageLaneExit): $($imageLaneOutput -join "`n")"
    }

    # 8. Web container smoke test (Lane=web-container)
    $smokeOutput = & dotnet test tests/RoleRadar.Tests.Integration -c Release --no-build --filter "Lane=web-container" 2>&1
    $smokePassed = 0
    $smokeSkipped = -1
    $smokeFailed = -1
    foreach ($line in $smokeOutput) {
        if ($line -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)') {
            $smokeFailed = [int]$Matches[1]
            $smokePassed = [int]$Matches[2]
            $smokeSkipped = [int]$Matches[3]
        }
    }
    if ($LASTEXITCODE -eq 0 -and $smokeFailed -eq 0 -and $smokePassed -ge 1 -and $smokeSkipped -eq 0) {
        $gateResults.WebSmoke = "PASS"
    } else {
        $gateResults.WebSmoke = "FAIL"
        Write-Warning "Web container smoke test failed or skipped (passed=$smokePassed, skipped=$smokeSkipped, exit=$LASTEXITCODE): $($smokeOutput -join "`n")"
    }

    # 9. Optional Real-Stack Playwright E2E test
    if ($IncludeE2E) {
        Write-Output "==> [review-gate] Executing real-stack Playwright E2E suite (-IncludeE2E)..."
        $origIdentityImg = $env:ROLERADAR_IDENTITY_IMAGE
        $origApiImg = $env:ROLERADAR_API_IMAGE
        $origWebImg = $env:ROLERADAR_WEB_IMAGE
        try {
            $env:ROLERADAR_IDENTITY_IMAGE = "roleradar-identity:review-$cloneGuid"
            $env:ROLERADAR_API_IMAGE = "roleradar-api:review-$cloneGuid"
            $env:ROLERADAR_WEB_IMAGE = "roleradar-web:review-$cloneGuid"
            $e2eOutput = & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "test-e2e-real.ps1") -Configuration Release -NoBuild 2>&1
            if ($LASTEXITCODE -eq 0) {
                $gateResults['RealStackE2E'] = "PASS"
            } else {
                $gateResults['RealStackE2E'] = "FAIL"
                Write-Warning "Real-stack E2E failed: $($e2eOutput -join "`n")"
            }
        }
        finally {
            $env:ROLERADAR_IDENTITY_IMAGE = $origIdentityImg
            $env:ROLERADAR_API_IMAGE = $origApiImg
            $env:ROLERADAR_WEB_IMAGE = $origWebImg
        }
    }

    # Cleanup review images
    $null = & docker rmi -f "roleradar-cli:review-$cloneGuid" "roleradar-mcp:review-$cloneGuid" "roleradar-identity:review-$cloneGuid" "roleradar-web:review-$cloneGuid" "roleradar-api:review-$cloneGuid" 2>&1
    $ErrorActionPreference = $prevEap
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $tempDir) {
        Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$allPassed = @($gateResults.Values | Where-Object { $_ -ne "PASS" -and $_ -ne "SKIP" }).Count -eq 0

# One object, written once and rendered below, so no number is typed by hand (feature 011). The Markdown tables the
# gate used to write were dropped with build-run-summary.ps1; CI builds its own summary (web/scripts/metrics-record.mjs).
$imageSizes = [ordered]@{ cli = $cliBytes; mcp = $mcpBytes; identity = $identityBytes; web = $webBytes; api = $apiBytes }
$imageList = @(foreach ($name in $imageSizes.Keys) {
        if ($imageSizes[$name]) { [ordered]@{ name = $name; sizeBytes = [int64]$imageSizes[$name] } }
    })
$gateSummary = [ordered]@{
    schemaVersion = 1
    branch = $Branch
    gates = $gateResults
    tests = $laneCounts
    coverage = $coverageSummary
    images = $imageList
    scan = [ordered]@{
        reports = $scanReports
        critical = $scanCritical
        severitiesScanned = @("CRITICAL")
        testPassed = $scanTestCounts.Passed
        testSkipped = $scanTestCounts.Skipped
    }
    imageLane = [ordered]@{ passed = $imageLaneCounts.Passed; failed = $imageLaneCounts.Failed; skipped = $imageLaneCounts.Skipped }
}
$summaryDirectory = Join-Path $repoRoot "TestResults"
New-Item -ItemType Directory -Force -Path $summaryDirectory | Out-Null
$gateSummary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $summaryDirectory "gate-summary.json") -Encoding utf8NoBOM

# Console summary, within the 15-line budget. A value that is missing shows as "unavailable" and fails the gate; nothing
# is ever defaulted.
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$script:anyUnavailable = $false
function Get-Cell {
    param([AllowNull()][object]$Value, [string]$Format = "0")
    if ($null -eq $Value) { $script:anyUnavailable = $true; return "unavailable" }
    return ([double]$Value).ToString($Format, $invariant)
}
function Get-LanePassed {
    param([string]$Lane)
    if ($null -eq $laneCounts[$Lane]) { return $null }
    return $laneCounts[$Lane].passed
}
function Get-SizeMb {
    param([string]$Name)
    if (-not $imageSizes[$Name]) { return "N/A" }
    return "$([math]::Round([int64]$imageSizes[$Name] / 1MB, 1).ToString($invariant)) MB"
}

$passedCounts = @("Unit", "Architecture", "Contract", "Integration" | ForEach-Object { Get-LanePassed $_ })
$totalPassed = if (@($passedCounts | Where-Object { $null -eq $_ }).Count -eq 0) { ($passedCounts | Measure-Object -Sum).Sum } else { $null }
$overallCoverage = if ($null -ne $coverageSummary) { $coverageSummary.overallLinePct } else { $null }
$highText = "HIGH: not scanned (policy CRITICAL)"
$consoleLines = @(
    "=== RoleRadar Review Gate Summary: $Branch ==="
    "1. Locked Restore: $($gateResults.Restore)"
    "2. Code Format:   $($gateResults.Format)"
    "3. WarningsBuild: $($gateResults.Build)"
    "4. Typecheck:     $($gateResults.Typecheck)"
    "5. Tests:         $($gateResults.Tests) (Unit: $(Get-Cell (Get-LanePassed 'Unit')), Arch: $(Get-Cell (Get-LanePassed 'Architecture')), Contract: $(Get-Cell (Get-LanePassed 'Contract')), Integration: $(Get-Cell (Get-LanePassed 'Integration')) | Total: $(Get-Cell $totalPassed))"
    "6. Domain+App Cov: $(Get-Cell $overallCoverage '0.##')% (Gate: >= 85%) -> $($gateResults.Coverage)"
    "7. Image Builds:  $($gateResults.Images) (CLI: $(Get-SizeMb 'cli'), MCP: $(Get-SizeMb 'mcp'), Identity: $(Get-SizeMb 'identity'), Web: $(Get-SizeMb 'web'), API: $(Get-SizeMb 'api'))"
    "8. Web image + smoke: $($gateResults.WebSmoke)"
    "9. Container scan:    $($gateResults.Scan) (reports: $(Get-Cell $scanReports), CRITICAL: $(Get-Cell $scanCritical), $highText; test passed: $(Get-Cell $scanTestCounts.Passed), skipped: $(Get-Cell $scanTestCounts.Skipped))"
    "10. Images lane:      $($gateResults.ImageLane) (passed: $(Get-Cell $imageLaneCounts.Passed), failed: $(Get-Cell $imageLaneCounts.Failed), skipped: $(Get-Cell $imageLaneCounts.Skipped))"
)
if ($gateResults.Contains("RealStackE2E")) {
    $consoleLines += "11. Real-stack E2E:   $($gateResults['RealStackE2E'])"
}
$consoleLines += "Script lint:        $($gateResults.ScriptLint)"
$verdictFailed = $script:anyUnavailable -or -not $allPassed
$consoleLines += "Verdict: $(if ($verdictFailed) { 'GATE FAILURE' } else { 'ALL GATES PASSED' })"
$consoleLines += "================================================="
$consoleLines | ForEach-Object { Write-Output $_ }

if ($verdictFailed) {
    exit 1
}
