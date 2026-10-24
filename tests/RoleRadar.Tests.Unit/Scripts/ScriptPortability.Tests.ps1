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
        "verify-traceability-003.ps1",
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
}
