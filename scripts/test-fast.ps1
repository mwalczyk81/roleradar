[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$slnRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $slnRoot

try {
    $projects = @(
        "tests/RoleRadar.Tests.Architecture/RoleRadar.Tests.Architecture.csproj",
        "tests/RoleRadar.Tests.Unit/RoleRadar.Tests.Unit.csproj",
        "tests/RoleRadar.Tests.Contract/RoleRadar.Tests.Contract.csproj"
    )

    $testArgs = @("test")
    if ($NoBuild) {
        $testArgs += "--no-build"
    }
    $testArgs += @("-c", $Configuration)

    foreach ($proj in $projects) {
        Write-Host "==> Running $proj..." -ForegroundColor Cyan
        dotnet @testArgs $proj
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Tests failed in $proj with exit code $LASTEXITCODE"
            exit $LASTEXITCODE
        }
    }

    Write-Host "==> Fast test loop completed successfully!" -ForegroundColor Green
}
finally {
    Pop-Location
}
