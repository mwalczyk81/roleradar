[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$slnRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $slnRoot

try {
    $testArgs = @("test", "RoleRadar.sln", "-c", $Configuration)
    if ($NoBuild) {
        $testArgs += "--no-build"
    }

    Write-Host "==> Running all tests for RoleRadar.sln..." -ForegroundColor Cyan
    dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Test suite failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Host "==> All tests completed successfully!" -ForegroundColor Green
}
finally {
    Pop-Location
}
