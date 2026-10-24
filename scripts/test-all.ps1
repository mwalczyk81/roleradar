[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Configuration = "Release",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalArgs
)

$ErrorActionPreference = "Stop"

$slnRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $slnRoot

try {
    $testArgs = @("test", "RoleRadar.sln", "-c", $Configuration)
    if ($NoBuild) {
        $testArgs += "--no-build"
    }
    if ($AdditionalArgs) {
        $testArgs += $AdditionalArgs
    }

    Write-Output "==> Running all tests for RoleRadar.sln..."
    dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Test suite failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }

    Write-Output "==> All tests completed successfully!"
}
finally {
    Pop-Location
}
