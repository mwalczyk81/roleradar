<#
.SYNOPSIS
    Verifies that all 30 Feature 003 functional requirements (FR-001..FR-030)
    and 8 success criteria (SC-001..SC-008) in spec.md are cited in tasks.md,
    and optionally validates that referenced files exist on disk.
.PARAMETER SpecPath
    Path to spec.md (defaults to specs/003-linux-container-readiness/spec.md).
.PARAMETER TasksPath
    Path to tasks.md (defaults to specs/003-linux-container-readiness/tasks.md).
.PARAMETER VerifyCodeOnDisk
    When specified, verifies that implementation and test files cited in tasks.md exist on disk.
#>
[CmdletBinding()]
param(
    [string]$SpecPath = "specs/003-linux-container-readiness/spec.md",
    [string]$TasksPath = "specs/003-linux-container-readiness/tasks.md",
    [switch]$VerifyCodeOnDisk
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$fullSpecPath = Join-Path $repoRoot $SpecPath
$fullTasksPath = Join-Path $repoRoot $TasksPath

if (-not (Test-Path -LiteralPath $fullSpecPath -PathType Leaf)) {
    Write-Error "Spec file not found at: $fullSpecPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $fullTasksPath -PathType Leaf)) {
    Write-Error "Tasks file not found at: $fullTasksPath"
    exit 1
}

$specContent = Get-Content -LiteralPath $fullSpecPath -Raw -Encoding UTF8
$tasksContent = Get-Content -LiteralPath $fullTasksPath -Raw -Encoding UTF8

# Extract all FR-xxx from spec.md
$frMatches = [regex]::Matches($specContent, '-\s+\*\*(FR-\d+[a-z]?)\*\*')
$specFrs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($m in $frMatches) {
    [void]$specFrs.Add($m.Groups[1].Value.ToUpperInvariant())
}

# Extract all SC-xxx from spec.md
$scMatches = [regex]::Matches($specContent, '-\s+\*\*(SC-\d+[a-z]?)\*\*')
$specScs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($m in $scMatches) {
    [void]$specScs.Add($m.Groups[1].Value.ToUpperInvariant())
}

Write-Output "Discovered $($specFrs.Count) Functional Requirements and $($specScs.Count) Success Criteria in $SpecPath"

if ($specFrs.Count -ne 30) {
    Write-Error "Expected exactly 30 functional requirements in spec.md, but found $($specFrs.Count)."
    exit 1
}
if ($specScs.Count -ne 8) {
    Write-Error "Expected exactly 8 success criteria in spec.md, but found $($specScs.Count)."
    exit 1
}

$failures = [System.Collections.Generic.List[string]]::new()

# Verify that tasks.md cites every FR
foreach ($fr in $specFrs) {
    if ($tasksContent -notmatch "(?i)\b$([regex]::Escape($fr))\b") {
        $failures.Add("Missing requirement citation in tasks.md: $fr")
    }
}

# Verify that tasks.md cites every SC
foreach ($sc in $specScs) {
    if ($tasksContent -notmatch "(?i)\b$([regex]::Escape($sc))\b") {
        $failures.Add("Missing success criterion citation in tasks.md: $sc")
    }
}

if ($VerifyCodeOnDisk) {
    Write-Output "Verifying referenced files on disk..."
    $keyPaths = @(
        ".dockerignore",
        ".trivy-allowlist.yaml",
        "deploy/docker-compose.apps.yml",
        ".github/dependabot.yml",
        "Dockerfile",
        "scripts/roleradar-docker.ps1",
        "tests/RoleRadar.Tests.Architecture/ContainerReadinessArchitectureTests.cs",
        "tests/RoleRadar.Tests.Contract/ExternalDatabaseGuardUnitTests.cs",
        "tests/RoleRadar.Tests.Contract/Fixtures/ExternalMongoSafetyGuard.cs",
        "tests/RoleRadar.Tests.Integration/Hosting/ContainerTestHarness.cs",
        "tests/RoleRadar.Tests.Integration/Hosting/ContainerStartupSmokeTests.cs",
        "tests/RoleRadar.Tests.Integration/Hosting/ContainerImageMetadataTests.cs",
        "tests/RoleRadar.Tests.Integration/Hosting/ContainerColdStartBenchmarkTests.cs"
    )

    foreach ($kp in $keyPaths) {
        $fullKp = Join-Path $repoRoot $kp
        if (-not (Test-Path -LiteralPath $fullKp)) {
            $failures.Add("Expected file missing on disk: $kp")
        }
    }
}

Write-Output ""
Write-Output "================ Traceability Verification Summary ================"
Write-Output "Total Spec Requirements:   $($specFrs.Count)"
Write-Output "Total Success Criteria:    $($specScs.Count)"
Write-Output "Failures / Discrepancies:  $($failures.Count)"

if ($failures.Count -gt 0) {
    Write-Output ""
    Write-Output "Failures:"
    foreach ($f in $failures) {
        Write-Output "  - $f"
    }
    exit 1
}

Write-Output "Traceability verified: all 30 FRs and 8 SCs cited in tasks.md."
exit 0
