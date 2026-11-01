<#
.SYNOPSIS
    Executes the real-stack Playwright end-to-end integration test suite.
.DESCRIPTION
    Runs the full real-stack test: spins up isolated Mongo replica set, real Identity,
    API, and Web images on a throwaway Docker network with runtime secrets and certs,
    drives real Chromium with Playwright, and verifies Mongo audit logs directly.
    Guarantees complete cleanup in finally.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    Write-Output "==> [test-e2e-real] Checking Docker availability..."
    $dockerCheck = & docker version 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Docker CLI or daemon is not accessible. Exit code: $LASTEXITCODE"
    }

    if (-not $NoBuild) {
        Write-Output "==> [test-e2e-real] Building container images..."
        $identityTag = if ($env:ROLERADAR_IDENTITY_IMAGE) { $env:ROLERADAR_IDENTITY_IMAGE } else { "roleradar-identity:local" }
        $apiTag = if ($env:ROLERADAR_API_IMAGE) { $env:ROLERADAR_API_IMAGE } else { "roleradar-api:local" }
        $webTag = if ($env:ROLERADAR_WEB_IMAGE) { $env:ROLERADAR_WEB_IMAGE } else { "roleradar-web:local" }

        $images = @(
            @{ Tag = $identityTag; Dockerfile = "deploy/Dockerfile.identity" },
            @{ Tag = $apiTag; Dockerfile = "deploy/Dockerfile.api" },
            @{ Tag = $webTag; Dockerfile = "deploy/Dockerfile.web" }
        )

        foreach ($img in $images) {
            Write-Output "==> Building $($img.Tag)..."
            & docker build -f $img.Dockerfile -t $img.Tag .
            if ($LASTEXITCODE -ne 0) {
                throw "Failed building $($img.Tag)"
            }
        }
    }

    $env:ROLERADAR_REAL_STACK_E2E = "1"

    Write-Output "==> [test-e2e-real] Executing RealStackEndToEndTests in tests/RoleRadar.Tests.Integration..."
    $testArgs = @(
        "test",
        "tests/RoleRadar.Tests.Integration/RoleRadar.Tests.Integration.csproj",
        "-c", $Configuration,
        "--filter", "FullyQualifiedName~RealStackEndToEndTests"
    )

    & dotnet @testArgs
    $testExit = $LASTEXITCODE

    if ($testExit -ne 0) {
        throw "Real-stack E2E test execution failed with exit code $testExit"
    }

    Write-Output "==> [test-e2e-real] Real-stack end-to-end user journey and audit assertions passed successfully!"
}
finally {
    Remove-Item Env:\ROLERADAR_REAL_STACK_E2E -ErrorAction SilentlyContinue
    Pop-Location
}
