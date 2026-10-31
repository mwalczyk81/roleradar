<#
.SYNOPSIS
    Verifies that GitHub Actions CI job and task gating behaves correctly across change scenarios.

.DESCRIPTION
    Dynamically parses path-filter definitions and job/step if: expressions out of
    .github/workflows/ci.yml at runtime, then evaluates change scenarios:
      1. docs-only: executes only pre-commit and gate (skips windows-verify, Testcontainers, images, and web)
      2. web-only: executes pre-commit, images build, web, and gate; skips windows-verify and Testcontainers
      3. backend-only: executes pre-commit, windows-verify, ubuntu backend (Testcontainers, coverage, images), and gate; skips web
      4. workflow-only: executes all jobs and steps (runs everything)
      5. dockerignore-only: executes pre-commit, DockerIgnore unit tests, container image builds, and gate; skips windows-verify, Testcontainers, and web
      6. failed-changes: simulates a failed changes job; downstream jobs are skipped and the gate fails

.PARAMETER Scenario
    The scenario to test ('DocsOnly', 'WebOnly', 'BackendOnly', 'WorkflowOnly', 'DockerignoreOnly', 'FailedChanges', or 'All'). Defaults to 'All'.
#>

[CmdletBinding()]
param(
    [ValidateSet('DocsOnly', 'WebOnly', 'BackendOnly', 'WorkflowOnly', 'DockerignoreOnly', 'FailedChanges', 'All')]
    [string]$Scenario = 'All'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-WorkflowDefinition {
    param(
        [string]$WorkflowPath
    )

    if (-not (Test-Path -LiteralPath $WorkflowPath)) {
        throw "Workflow file not found at '$WorkflowPath'."
    }

    $lines = Get-Content -LiteralPath $WorkflowPath
    $filters = @{}
    $jobIfs = @{}
    $stepIfs = @{}

    $inFilters = $false
    $currentFilter = $null
    $inJobs = $false
    $currentJob = $null
    $currentStep = $null

    foreach ($line in $lines) {
        # --- Parse dorny/paths-filter filters: | block ---
        if (-not $inFilters) {
            if ($line -match '^\s*filters:\s*\|\s*$') {
                $inFilters = $true
            }
        } elseif ($inFilters) {
            if ($line -match '^\s{12}([a-zA-Z0-9_-]+):\s*$') {
                $currentFilter = $Matches[1]
                $filters[$currentFilter] = [System.Collections.Generic.List[string]]::new()
            } elseif ($line -match '^\s{14}-\s+[''"]?([^''"]+)[''"]?\s*$') {
                if ($null -eq $currentFilter) {
                    throw "Encountered filter pattern before filter name: '$line'"
                }
                $filters[$currentFilter].Add($Matches[1])
            } elseif ($line -match '^\s{0,10}\S') {
                $inFilters = $false
            }
        }

        # --- Parse jobs and steps ---
        if ($line -match '^jobs:\s*$') {
            $inJobs = $true
            continue
        }

        if ($inJobs) {
            if ($line -match '^[a-zA-Z0-9_-]+:') {
                # A top-level non-indented key terminates jobs block
                break
            }

            # Job identifier (2 spaces)
            if ($line -match '^  ([a-zA-Z0-9_-]+):\s*$') {
                $currentJob = $Matches[1]
                $currentStep = $null
                continue
            }

            # Job-level if: (4 spaces)
            if ($line -match '^    if:\s*(.+)$') {
                if ($null -ne $currentJob -and $null -eq $currentStep) {
                    $jobIfs[$currentJob] = $Matches[1].Trim()
                }
                continue
            }

            # Step name (6 spaces)
            if ($line -match '^      -\s+name:\s*(.+)$') {
                $currentStep = $Matches[1].Trim()
                continue
            }

            # Step-level if: (8 spaces)
            if ($line -match '^        if:\s*(.+)$') {
                if ($null -ne $currentStep) {
                    $stepIfs[$currentStep] = $Matches[1].Trim()
                }
                continue
            }
        }
    }

    if ($filters.Count -eq 0) {
        throw "Failed to parse path filters from '$WorkflowPath'."
    }

    $resultFilters = @{}
    foreach ($k in $filters.Keys) {
        $resultFilters[$k] = $filters[$k].ToArray()
    }

    return @{
        Filters = $resultFilters
        JobIfs = $jobIfs
        StepIfs = $stepIfs
    }
}

function Test-WorkflowIf {
    param(
        [string]$Expression,
        [hashtable]$Outputs,
        [string]$EventName = 'pull_request'
    )

    if ([string]::IsNullOrWhiteSpace($Expression)) {
        return $true
    }

    $trimmed = $Expression.Trim()
    if ($trimmed -eq 'always()') {
        return $true
    }

    $terms = $trimmed -split '\|\|'
    foreach ($term in $terms) {
        $t = $term.Trim()

        # Check form: github.event_name == '...'
        if ($t -match '^github\.event_name\s*==\s*''([^'']+)''$') {
            if ($EventName -eq $Matches[1]) {
                return $true
            }
            continue
        }

        # Check form: needs.changes.outputs.<name> == 'true'
        if ($t -match '^needs\.changes\.outputs\.([a-zA-Z0-9_-]+)\s*==\s*''true''$') {
            $name = $Matches[1]
            if ($Outputs.ContainsKey($name) -and $Outputs[$name] -eq 'true') {
                return $true
            }
            continue
        }

        # Any unrecognised form must fail loudly
        throw "Unsupported if: expression term '$t' in expression '$Expression'"
    }

    return $false
}

function Test-PathMatch {
    param(
        [string[]]$Files,
        [string[]]$Patterns
    )

    foreach ($file in $Files) {
        $normalized = $file.Replace('\', '/')
        foreach ($pattern in $Patterns) {
            if ($pattern.EndsWith('/**')) {
                $prefix = $pattern.Substring(0, $pattern.Length - 3)
                if ($normalized.StartsWith($prefix) -or $normalized.StartsWith("$prefix/")) {
                    return $true
                }
            } elseif ($pattern.StartsWith('*')) {
                $suffix = $pattern.Substring(1)
                if ($normalized.EndsWith($suffix)) {
                    return $true
                }
            } else {
                if ($normalized -eq $pattern -or $normalized.StartsWith("$pattern/")) {
                    return $true
                }
            }
        }
    }
    return $false
}

function Test-GateResult {
    param(
        [string]$ChangesResult,
        [string]$PreCommitResult,
        [string]$UbuntuResult,
        [string]$WebResult,
        [string]$WindowsResult
    )

    # In ci-gate: changes must be "success"
    if ($ChangesResult -ne 'success') {
        return $false
    }

    # Every downstream job result must be "success" or "skipped"
    $all = @($ChangesResult, $PreCommitResult, $UbuntuResult, $WebResult, $WindowsResult)
    foreach ($r in $all) {
        if ($r -ne 'success' -and $r -ne 'skipped') {
            return $false
        }
    }
    return $true
}

# Resolve workflow and parse rules at runtime
$workflowPath = Join-Path $PSScriptRoot '../.github/workflows/ci.yml'
$wf = Get-WorkflowDefinition -WorkflowPath $workflowPath

function Test-Scenario {
    param(
        [string]$Name,
        [string[]]$ChangedFiles,
        [string]$SimulatedChangesResult = 'success',
        [hashtable]$Expected
    )

    Write-Output "=================================================================="
    Write-Output "Evaluating CI Scenario: $Name"
    Write-Output "Simulated 'changes' job result: $SimulatedChangesResult"
    Write-Output "Changed files ($($ChangedFiles.Count)):"
    foreach ($f in $ChangedFiles) {
        Write-Output "  - $f"
    }

    # Evaluate dorny/paths-filter outputs from parsed filters
    $outputs = @{}
    foreach ($filterName in $wf.Filters.Keys) {
        $matched = Test-PathMatch -Files $ChangedFiles -Patterns $wf.Filters[$filterName]
        $outputs[$filterName] = if ($matched) { 'true' } else { 'false' }
    }

    Write-Output "`nParsed Filter Match Results:"
    foreach ($k in $wf.Filters.Keys) {
        Write-Output "  $k`: $($outputs[$k])"
    }

    # Evaluate jobs
    $jobPreCommit = $true # Unconditional step, does not depend on changes
    $jobGate = $true      # Runs unconditionally via if: always()

    $jobWindowsVerify = $false
    $jobUbuntu = $false
    $stepDockerIgnore = $false
    $stepTestcontainers = $false
    $stepCoverageGate = $false
    $stepUbuntuImages = $false
    $jobWeb = $false

    if ($SimulatedChangesResult -eq 'success') {
        # Evaluate parsed if: expressions for downstream jobs
        $jobWindowsVerify = Test-WorkflowIf -Expression $wf.JobIfs['windows-verify'] -Outputs $outputs
        $jobUbuntu = Test-WorkflowIf -Expression $wf.JobIfs['ubuntu-build-and-test'] -Outputs $outputs
        $jobWeb = Test-WorkflowIf -Expression $wf.JobIfs['web'] -Outputs $outputs

        # Evaluate steps inside ubuntu-build-and-test if job runs
        if ($jobUbuntu) {
            $stepDockerIgnore = Test-WorkflowIf -Expression $wf.StepIfs['Run DockerIgnore coverage unit tests'] -Outputs $outputs
            $stepTestcontainers = Test-WorkflowIf -Expression $wf.StepIfs['Run full test suite (Unit, Architecture, Contract, Integration) with Coverage'] -Outputs $outputs
            $stepCoverageGate = Test-WorkflowIf -Expression $wf.StepIfs['Verify >=85% line coverage across RoleRadar.Domain and RoleRadar.Application'] -Outputs $outputs
            $stepUbuntuImages = Test-WorkflowIf -Expression $wf.StepIfs['Build local container images (never pushed/published per ADR-0016)'] -Outputs $outputs
        }
    }

    # Calculate job results as GitHub Actions would report them to ci-gate
    $preCommitResult = if ($jobPreCommit) { 'success' } else { 'skipped' }
    $windowsResult = if ($SimulatedChangesResult -ne 'success') {
        'skipped'
    } elseif ($jobWindowsVerify) {
        'success'
    } else {
        'skipped'
    }

    $ubuntuResult = if ($SimulatedChangesResult -ne 'success') {
        'skipped'
    } elseif ($jobUbuntu) {
        'success'
    } else {
        'skipped'
    }

    $webResult = if ($SimulatedChangesResult -ne 'success') {
        'skipped'
    } elseif ($jobWeb) {
        'success'
    } else {
        'skipped'
    }

    $gatePasses = Test-GateResult `
        -ChangesResult $SimulatedChangesResult `
        -PreCommitResult $preCommitResult `
        -UbuntuResult $ubuntuResult `
        -WebResult $webResult `
        -WindowsResult $windowsResult

    Write-Output "`nJob & Step Execution Decision:"
    Write-Output "  [Job]  changes (simulated result):                     $SimulatedChangesResult"
    Write-Output "  [Job]  pre-commit (unconditional):                     $jobPreCommit (result: $preCommitResult)"
    Write-Output "  [Job]  windows-verify (backend-gated):                 $jobWindowsVerify (result: $windowsResult)"
    Write-Output "  [Job]  ubuntu-build-and-test (path-gated):             $jobUbuntu (result: $ubuntuResult)"
    Write-Output "  [Step] ubuntu: DockerIgnore Unit Tests (images-gated): $stepDockerIgnore"
    Write-Output "  [Step] ubuntu: Testcontainers Suite (backend-gated):   $stepTestcontainers"
    Write-Output "  [Step] ubuntu: 85% Line Coverage Gate (backend-gated): $stepCoverageGate"
    Write-Output "  [Step] ubuntu: Container Image Builds (path-gated):    $stepUbuntuImages"
    Write-Output "  [Job]  web (web-gated):                                $jobWeb (result: $webResult)"
    Write-Output "  [Job]  ci-gate execution (unconditional if: always):   $jobGate"
    Write-Output "  [Gate] ci-gate evaluation result:                      $(if ($gatePasses) { 'PASS' } else { 'FAIL' })"

    # Assertions
    $errors = [System.Collections.Generic.List[string]]::new()

    if ($Expected.ContainsKey('JobPreCommit') -and $jobPreCommit -ne $Expected['JobPreCommit']) {
        $errors.Add("Expected JobPreCommit=$($Expected['JobPreCommit']), actual=$jobPreCommit")
    }
    if ($Expected.ContainsKey('JobWindowsVerify') -and $jobWindowsVerify -ne $Expected['JobWindowsVerify']) {
        $errors.Add("Expected JobWindowsVerify=$($Expected['JobWindowsVerify']), actual=$jobWindowsVerify")
    }
    if ($Expected.ContainsKey('JobUbuntu') -and $jobUbuntu -ne $Expected['JobUbuntu']) {
        $errors.Add("Expected JobUbuntu=$($Expected['JobUbuntu']), actual=$jobUbuntu")
    }
    if ($Expected.ContainsKey('DockerIgnoreTests') -and $stepDockerIgnore -ne $Expected['DockerIgnoreTests']) {
        $errors.Add("Expected DockerIgnoreTests=$($Expected['DockerIgnoreTests']), actual=$stepDockerIgnore")
    }
    if ($Expected.ContainsKey('Testcontainers') -and $stepTestcontainers -ne $Expected['Testcontainers']) {
        $errors.Add("Expected Testcontainers=$($Expected['Testcontainers']), actual=$stepTestcontainers")
    }
    if ($Expected.ContainsKey('CoverageGate') -and $stepCoverageGate -ne $Expected['CoverageGate']) {
        $errors.Add("Expected CoverageGate=$($Expected['CoverageGate']), actual=$stepCoverageGate")
    }
    if ($Expected.ContainsKey('JobUbuntuImages') -and $stepUbuntuImages -ne $Expected['JobUbuntuImages']) {
        $errors.Add("Expected JobUbuntuImages=$($Expected['JobUbuntuImages']), actual=$stepUbuntuImages")
    }
    if ($Expected.ContainsKey('JobWeb') -and $jobWeb -ne $Expected['JobWeb']) {
        $errors.Add("Expected JobWeb=$($Expected['JobWeb']), actual=$jobWeb")
    }
    if ($Expected.ContainsKey('JobGate') -and $jobGate -ne $Expected['JobGate']) {
        $errors.Add("Expected JobGate=$($Expected['JobGate']), actual=$jobGate")
    }
    if ($Expected.ContainsKey('GatePasses') -and $gatePasses -ne $Expected['GatePasses']) {
        $errors.Add("Expected GatePasses=$($Expected['GatePasses']), actual=$gatePasses")
    }

    if ($errors.Count -gt 0) {
        Write-Error "Scenario '$Name' FAILED with $($errors.Count) assertion error(s):"
        foreach ($err in $errors) {
            Write-Error "  - $err"
        }
        throw "Scenario '$Name' assertion failure."
    }

    Write-Output "Result: PASSED (all scenario expectations satisfied)"
}

$scenariosToRun = if ($Scenario -eq 'All') {
    @('DocsOnly', 'WebOnly', 'BackendOnly', 'WorkflowOnly', 'DockerignoreOnly', 'FailedChanges')
} else {
    @($Scenario)
}

foreach ($s in $scenariosToRun) {
    switch ($s) {
        'DocsOnly' {
            Test-Scenario -Name 'Docs-Only Change' -ChangedFiles @(
                'docs/adr/0024-nx-and-nx-dotnet-monorepo-orchestration.md',
                'specs/005b-management-ui/spec.md',
                'specs/005b-management-ui/tasks.md',
                'README.md'
            ) -Expected @{
                JobPreCommit = $true
                JobWindowsVerify = $false
                JobUbuntu = $false
                DockerIgnoreTests = $false
                Testcontainers = $false
                CoverageGate = $false
                JobUbuntuImages = $false
                JobWeb = $false
                JobGate = $true
                GatePasses = $true
            }
        }
        'WebOnly' {
            Test-Scenario -Name 'Web-Only Change' -ChangedFiles @(
                'web/app/routes/postings._index.tsx',
                'web/package.json',
                'web/app/components/TransitionPostingDialog.tsx'
            ) -Expected @{
                JobPreCommit = $true
                JobWindowsVerify = $false
                JobUbuntu = $true
                DockerIgnoreTests = $true
                Testcontainers = $false
                CoverageGate = $false
                JobUbuntuImages = $true
                JobWeb = $true
                JobGate = $true
                GatePasses = $true
            }
        }
        'BackendOnly' {
            Test-Scenario -Name 'Backend-Only Change' -ChangedFiles @(
                'src/RoleRadar.Domain/Entities/Posting.cs',
                'src/RoleRadar.Application/Commands/TransitionPostingCommand.cs',
                'tests/RoleRadar.Tests.Unit/Domain/PostingTests.cs'
            ) -Expected @{
                JobPreCommit = $true
                JobWindowsVerify = $true
                JobUbuntu = $true
                DockerIgnoreTests = $true
                Testcontainers = $true
                CoverageGate = $true
                JobUbuntuImages = $true
                JobWeb = $false
                JobGate = $true
                GatePasses = $true
            }
        }
        'WorkflowOnly' {
            Test-Scenario -Name 'Workflow-Only Change' -ChangedFiles @(
                '.github/workflows/ci.yml'
            ) -Expected @{
                JobPreCommit = $true
                JobWindowsVerify = $true
                JobUbuntu = $true
                DockerIgnoreTests = $true
                Testcontainers = $true
                CoverageGate = $true
                JobUbuntuImages = $true
                JobWeb = $true
                JobGate = $true
                GatePasses = $true
            }
        }
        'DockerignoreOnly' {
            Test-Scenario -Name 'Dockerignore-Only Change' -ChangedFiles @(
                '.dockerignore'
            ) -Expected @{
                JobPreCommit = $true
                JobWindowsVerify = $false
                JobUbuntu = $true
                DockerIgnoreTests = $true
                Testcontainers = $false
                CoverageGate = $false
                JobUbuntuImages = $true
                JobWeb = $false
                JobGate = $true
                GatePasses = $true
            }
        }
        'FailedChanges' {
            Test-Scenario -Name 'Failed Changes Job (Gate Failure Simulation)' -ChangedFiles @(
                'src/RoleRadar.Domain/Entities/Posting.cs'
            ) -SimulatedChangesResult 'failure' -Expected @{
                JobPreCommit = $true
                JobWindowsVerify = $false
                JobUbuntu = $false
                DockerIgnoreTests = $false
                Testcontainers = $false
                CoverageGate = $false
                JobUbuntuImages = $false
                JobWeb = $false
                JobGate = $true
                GatePasses = $false
            }
        }
    }
}

Write-Output "`n[verify-ci-affected] All tested scenarios passed successfully."
