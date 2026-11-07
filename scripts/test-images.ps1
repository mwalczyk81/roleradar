[CmdletBinding()]
param(
    [string]$CliImage = "roleradar-cli:test",
    [string]$McpImage = "roleradar-mcp:test",
    [string]$IdentityImage = "roleradar-identity:test",
    [string]$WebImage = "roleradar-web:test",
    [string]$Configuration = "Release",
    # Selected by the Lane trait (ADR 0036), never by class-name fragments: a new image-sensitive class carries [Trait("Lane", "images")]
    # and TestLaneCoverageTests fails when one does not. The scan test has its own lane ("scan") because it needs real reports.
    [string]$Filter = "Lane=images|Lane=web-container",
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
    Write-Output "==> [test-images] Verifying Docker container images..."
    if (-not $NoBuild) {
        $null = & docker image inspect $CliImage 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Output "==> Building $CliImage..."
            & docker build --target cli -t $CliImage .
            if ($LASTEXITCODE -ne 0) { throw "CLI image build failed with exit code $LASTEXITCODE" }
        }

        $null = & docker image inspect $McpImage 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Output "==> Building $McpImage..."
            & docker build --target mcp -t $McpImage .
            if ($LASTEXITCODE -ne 0) { throw "MCP image build failed with exit code $LASTEXITCODE" }
        }

        $null = & docker image inspect $IdentityImage 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Output "==> Building $IdentityImage..."
            & docker build -f deploy/Dockerfile.identity -t $IdentityImage .
            if ($LASTEXITCODE -ne 0) { throw "Identity image build failed with exit code $LASTEXITCODE" }
        }

        $null = & docker image inspect $WebImage 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Output "==> Building $WebImage..."
            & docker build -f deploy/Dockerfile.web -t $WebImage .
            if ($LASTEXITCODE -ne 0) { throw "Web image build failed with exit code $LASTEXITCODE" }
        }
    }

    $env:ROLERADAR_TEST_IMAGE = "1"
    $env:ROLERADAR_CLI_IMAGE = $CliImage
    $env:ROLERADAR_MCP_IMAGE = $McpImage
    $env:ROLERADAR_IDENTITY_IMAGE = $IdentityImage
    $env:ROLERADAR_WEB_IMAGE = $WebImage

    Write-Output "==> [test-images] Executing launched test suites against container images under read-only tmpfs..."
    $testArgs = @("test", "tests/RoleRadar.Tests.Integration", "-c", $Configuration)
    if (![string]::IsNullOrWhiteSpace($Filter)) {
        $testArgs += @("--filter", $Filter)
    }

    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Image integration test suite failed with exit code $LASTEXITCODE"
    }

    Write-Output "==> [test-images] All container image parity tests passed successfully!"
}
finally {
    Remove-Item Env:\ROLERADAR_TEST_IMAGE -ErrorAction SilentlyContinue
    Remove-Item Env:\ROLERADAR_CLI_IMAGE -ErrorAction SilentlyContinue
    Remove-Item Env:\ROLERADAR_MCP_IMAGE -ErrorAction SilentlyContinue
    Remove-Item Env:\ROLERADAR_IDENTITY_IMAGE -ErrorAction SilentlyContinue
    Remove-Item Env:\ROLERADAR_WEB_IMAGE -ErrorAction SilentlyContinue
    Pop-Location
}
