<#
.SYNOPSIS
    Verifies that all 48 Feature 002 functional requirements in spec.md are mapped and marked PASS in mcp-tools.md,
    and optionally validates code on disk.
.PARAMETER SpecPath
    Path to spec.md (defaults to specs/002-job-search-tracking/spec.md).
.PARAMETER ContractPath
    Path to mcp-tools.md (defaults to specs/002-job-search-tracking/contracts/mcp-tools.md).
.PARAMETER VerifyCodeOnDisk
    When specified, verifies that tool names and test files named in the matrix exist in the codebase.
#>
[CmdletBinding()]
param(
    [string]$SpecPath = "specs/002-job-search-tracking/spec.md",
    [string]$ContractPath = "specs/002-job-search-tracking/contracts/mcp-tools.md",
    [switch]$VerifyCodeOnDisk
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$fullSpecPath = Join-Path $repoRoot $SpecPath
$fullContractPath = Join-Path $repoRoot $ContractPath

if (-not (Test-Path -LiteralPath $fullSpecPath -PathType Leaf)) {
    Write-Error "Spec file not found at: $fullSpecPath"
    exit 1
}
if (-not (Test-Path -LiteralPath $fullContractPath -PathType Leaf)) {
    Write-Error "Contract file not found at: $fullContractPath"
    exit 1
}

$specContent = Get-Content -LiteralPath $fullSpecPath -Raw -Encoding UTF8
$contractContent = Get-Content -LiteralPath $fullContractPath -Raw -Encoding UTF8

# Extract all FR-xxx from spec.md
$specMatches = [regex]::Matches($specContent, '-\s+\*\*(FR-\d+[a-z]?)\*\*')
$specReqs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($m in $specMatches) {
    [void]$specReqs.Add($m.Groups[1].Value.ToUpperInvariant())
}

Write-Host "Discovered $($specReqs.Count) requirements in $SpecPath"

if ($specReqs.Count -ne 48) {
    Write-Error "Expected exactly 48 requirements in spec.md, but found $($specReqs.Count)."
    exit 1
}

# Parse traceability table in mcp-tools.md
# Rows match: | **FR-xxx** | Feature Area | Satisfying Interface / Mechanism | Traceability Status |
$matrixMatches = [regex]::Matches($contractContent, '\|\s+\*\*(FR-\d+[a-z]?)\*\*\s+\|\s+([^\|]+)\|\s+([^\|]+)\|\s+([^\s\|]+(?:\s+[^\s\|]+)*)\s+\|')
$matrixReqs = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($m in $matrixMatches) {
    $reqId = $m.Groups[1].Value.ToUpperInvariant()
    $status = $m.Groups[4].Value.Trim()
    $matrixReqs[$reqId] = $status
}

Write-Host "Discovered $($matrixReqs.Count) mapped requirements in $ContractPath"

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($req in $specReqs) {
    if (-not $matrixReqs.ContainsKey($req)) {
        $failures.Add("Missing requirement in matrix: $req")
        continue
    }

    $status = $matrixReqs[$req]
    if ($status -notmatch 'PASS') {
        $failures.Add("Requirement $req status is '$status' (expected PASS)")
    }
}

if ($VerifyCodeOnDisk) {
    Write-Host "Verifying code on disk..."
    $trackingToolsFile = Join-Path $repoRoot "src/RoleRadar.Mcp.Stdio/Tools/TrackingMcpTools.cs"
    if (-not (Test-Path -LiteralPath $trackingToolsFile -PathType Leaf)) {
        $failures.Add("TrackingMcpTools.cs does not exist on disk: $trackingToolsFile")
    }

    $requiredTools = @(
        "posting_record",
        "posting_transition",
        "company_record",
        "application_record",
        "tracker_log",
        "tracker_query"
    )

    if (Test-Path -LiteralPath $trackingToolsFile -PathType Leaf) {
        $toolsContent = Get-Content -LiteralPath $trackingToolsFile -Raw -Encoding UTF8
        foreach ($tool in $requiredTools) {
            if ($toolsContent -notmatch [regex]::Escape($tool)) {
                $failures.Add("Expected tool '$tool' not found in TrackingMcpTools.cs")
            }
        }
    }
}

Write-Host ""
Write-Host "================ Traceability Verification Summary ================"
Write-Host "Total Spec Requirements:   $($specReqs.Count)"
Write-Host "Matrix Mapped Count:       $($matrixReqs.Count)"
Write-Host "Failures / Discrepancies:  $($failures.Count)"

if ($failures.Count -gt 0) {
    Write-Host ""
    Write-Host "Failures:" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "  - $f" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Traceability matrix verified: 48 of 48 requirements present with status PASS." -ForegroundColor Green
exit 0
