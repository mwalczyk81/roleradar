# Pester Script Portability Tests for RoleRadar Automation Scripts
# Verifies syntax, cross-platform path handling, and script availability across Windows, macOS, and Linux.

BeforeDiscovery {
    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../../..")
    $scriptsDir = Join-Path $repoRoot "scripts"

    $requiredScripts = @(
        "backup.ps1",
        "restore.ps1",
        "wipe-data.ps1",
        "roleradar-docker.ps1",
        "denylist-check.ps1",
        "start-services.ps1",
        "test-all.ps1",
        "test-fast.ps1",
        "test-images.ps1",
        "verify-action-pins.ps1",
        "check-tools-release.ps1",
        "review-gate.ps1"
    )

    $allScripts = Get-ChildItem -Path $scriptsDir -Filter "*.ps1"
}

Describe "Script Portability and Cross-Platform Compatibility" {
    BeforeAll {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot "../../..")
        $scriptsDir = Join-Path $repoRoot "scripts"
    }

    Context "Repository Script Inventory and Existence" -ForEach $requiredScripts {
        It "Script '<_>' exists in scripts directory" {
            $scriptPath = Join-Path $scriptsDir $_
            (Test-Path -LiteralPath $scriptPath) | Should -Be $true
        }
    }

    Context "Script Syntax and AST Parsing" -ForEach $allScripts {
        It "Script '<_.Name>' parses cleanly without syntax errors" {
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile(
                $_.FullName,
                [ref]$tokens,
                [ref]$errors
            ) | Out-Null
            $errors.Count | Should -Be 0
        }

        It "Script '<_.Name>' does not contain hardcoded Windows backslash repo paths" {
            $content = Get-Content -LiteralPath $_.FullName -Raw
            # Ensure no 'scripts\', 'tests\', 'src\', 'deploy\' backslash paths
            $content | Should -Not -Match "['""][^'""]*?\b(scripts|deploy|src|tests|specs)\\[^'""]*?['""]"
        }
    }

    Context "Review Gate Smoke Parsing Invariants" {
        It "Reports WebSmoke as FAIL when smoke test output is skipped" {
            $mockSmokeOutput = @(
                "Test run for /workspace/RoleRadar.Tests.Integration.dll (.NETCoreApp,Version=v10.0)",
                "Passed!  - Failed:     0, Passed:     0, Skipped:     1, Total:     1, Duration: 2 ms"
            )
            $smokePassed = 0
            $smokeSkipped = -1
            $smokeFailed = -1
            foreach ($line in $mockSmokeOutput) {
                if ($line -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)') {
                    $smokeFailed = [int]$Matches[1]
                    $smokePassed = [int]$Matches[2]
                    $smokeSkipped = [int]$Matches[3]
                }
            }
            $webSmoke = if (0 -eq 0 -and $smokeFailed -eq 0 -and $smokePassed -ge 1 -and $smokeSkipped -eq 0) { "PASS" } else { "FAIL" }
            $webSmoke | Should -Be "FAIL"
        }

        It "Reports WebSmoke as PASS when smoke test output has passed >= 1 and skipped == 0" {
            $mockSmokeOutput = @(
                "Test run for /workspace/RoleRadar.Tests.Integration.dll (.NETCoreApp,Version=v10.0)",
                "Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 8200 ms"
            )
            $smokePassed = 0
            $smokeSkipped = -1
            $smokeFailed = -1
            foreach ($line in $mockSmokeOutput) {
                if ($line -match 'Failed:\s+(\d+),\s+Passed:\s+(\d+),\s+Skipped:\s+(\d+)') {
                    $smokeFailed = [int]$Matches[1]
                    $smokePassed = [int]$Matches[2]
                    $smokeSkipped = [int]$Matches[3]
                }
            }
            $webSmoke = if (0 -eq 0 -and $smokeFailed -eq 0 -and $smokePassed -ge 1 -and $smokeSkipped -eq 0) { "PASS" } else { "FAIL" }
            $webSmoke | Should -Be "PASS"
        }
    }

    # roleradar-docker.ps1 builds a docker command line and runs it. These tests put a stub `docker` first on PATH that only
    # prints the arguments it receives, so no container is ever started and nothing can reach Mongo.
    Context "Docker wrapper network option (ROLERADAR_DOCKER_NETWORK)" {
        BeforeAll {
            $wrapper = Join-Path (Resolve-Path (Join-Path $PSScriptRoot "../../..")) "scripts/roleradar-docker.ps1"
            $stubDir = Join-Path ([System.IO.Path]::GetTempPath()) ("roleradar-docker-stub-" + [guid]::NewGuid().ToString("N"))
            New-Item -ItemType Directory -Path $stubDir | Out-Null
            if ($IsWindows) {
                # cmd.exe splits arguments at commas and equals signs, so docker.cmd hands the original text (%*) to a pwsh script.
                Set-Content -LiteralPath (Join-Path $stubDir "docker-stub.ps1") -Value '$args | ForEach-Object { "ARG:$_" }'
                Set-Content -LiteralPath (Join-Path $stubDir "docker.cmd") -Value @(
                    '@echo off',
                    'pwsh -NoProfile -File "%~dp0docker-stub.ps1" %*'
                )
            } else {
                $stubPath = Join-Path $stubDir "docker"
                Set-Content -LiteralPath $stubPath -Value @('#!/bin/sh', 'for a in "$@"; do echo "ARG:$a"; done')
                chmod +x $stubPath
            }

            $touched = @(
                'ROLERADAR_DOCKER_NETWORK', 'ROLERADAR_CLI_IMAGE', 'ROLERADAR_MCP_IMAGE', 'ROLERADAR_IMAGE_TAG',
                'ROLERADAR_MONGO_CONNECTION_STRING', 'ROLERADAR_MONGO_DATABASE', 'ROLERADAR_OWNER_ID', 'ROLERADAR_LOG_LEVEL',
                'DOTNET_EnableDiagnostics', 'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT', 'DOTNET_gcServer', 'PATH'
            )
            $saved = @{}
            foreach ($name in $touched) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

            # Runs the wrapper in a child pwsh with a clean ROLERADAR_* environment and returns the docker arguments it produced.
            function Get-WrapperDockerArgs {
                param([string[]]$WrapperArguments, [string]$Network)
                foreach ($name in $touched) {
                    if ($name -ne 'PATH') { [Environment]::SetEnvironmentVariable($name, $null) }
                }
                [Environment]::SetEnvironmentVariable('PATH', "$stubDir$([System.IO.Path]::PathSeparator)$($saved['PATH'])")
                if ($null -ne $Network) { [Environment]::SetEnvironmentVariable('ROLERADAR_DOCKER_NETWORK', $Network) }
                try {
                    $lines = & pwsh -NoProfile -File $wrapper @WrapperArguments
                    return @($lines | Where-Object { $_ -like 'ARG:*' } | ForEach-Object { $_.Substring(4) })
                } finally {
                    foreach ($name in $touched) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
                }
            }
        }

        AfterAll {
            Remove-Item -LiteralPath $stubDir -Recurse -Force -ErrorAction SilentlyContinue
        }

        It "adds no network option and keeps the exact default command line when the variable is unset" {
            $args = Get-WrapperDockerArgs -WrapperArguments @('status')

            $args | Should -Be @(
                'run', '--rm', '-i', '--read-only', '--tmpfs', '/tmp:rw,noexec,nosuid,size=64m',
                '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges', 'roleradar-cli:test', 'status'
            )
        }

        It "adds no network option when the variable is empty or only whitespace" {
            (Get-WrapperDockerArgs -WrapperArguments @('status') -Network '') | Should -Not -Contain '--network'
            (Get-WrapperDockerArgs -WrapperArguments @('status') -Network '   ') | Should -Not -Contain '--network'
        }

        It "adds --network <name> before the image when the variable is set" {
            $args = Get-WrapperDockerArgs -WrapperArguments @('reconcile-read-models') -Network 'deploy_default'

            $networkAt = [array]::IndexOf($args, '--network')
            $networkAt | Should -BeGreaterThan -1
            $args[$networkAt + 1] | Should -Be 'deploy_default'
            [array]::IndexOf($args, 'roleradar-cli:test') | Should -BeGreaterThan ($networkAt + 1)
            $args[-1] | Should -Be 'reconcile-read-models'
        }

        It "applies to the MCP container too, and appears exactly once" {
            $args = Get-WrapperDockerArgs -WrapperArguments @('mcp') -Network 'deploy_default'

            @($args | Where-Object { $_ -eq '--network' }).Count | Should -Be 1
            $args | Should -Contain 'roleradar-mcp:test'
        }
    }
}
