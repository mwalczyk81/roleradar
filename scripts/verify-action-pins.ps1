[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$workflowDir = Join-Path -Path $repoRoot -ChildPath '.github/workflows'

if (-not (Test-Path -LiteralPath $workflowDir -PathType Container)) {
    throw "Workflow directory not found at '$workflowDir'."
}

$ghCmd = Get-Command -Name 'gh' -ErrorAction SilentlyContinue
if ($null -eq $ghCmd) {
    throw "GitHub CLI ('gh') is required on PATH to verify workflow action commit SHAs."
}

$workflowFiles = @(Get-ChildItem -LiteralPath $workflowDir -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
if ($workflowFiles.Count -eq 0) {
    throw "No workflow files found under '$workflowDir'."
}

$usesRegex = '^\s*(?:-\s*)?uses:\s*(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)(?:/[A-Za-z0-9_./-]+)?@(?<sha>[0-9a-f]{40})\s+#\s*v\d+\.\d+\.\d+\b'
$verifiedPins = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$totalReferences = 0

foreach ($wf in $workflowFiles) {
    $lines = @(Get-Content -LiteralPath $wf.FullName -Encoding UTF8)
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*#') {
            continue
        }

        if ($line -match '^\s*(?:-\s*)?uses:\s*') {
            $totalReferences++
            if ($line -notmatch $usesRegex) {
                $lineNum = $i + 1
                throw "$($wf.Name):$lineNum -> '$($line.Trim())' is not pinned to a 40-character commit SHA with a '# vX.Y.Z' comment."
            }

            $ownerRepo = "$($Matches['owner'])/$($Matches['repo'])"
            $sha = $Matches['sha']
            $key = "$ownerRepo@$sha"

            if ($verifiedPins.Add($key)) {
                $resolvedSha = & gh api "repos/$ownerRepo/commits/$sha" --jq '.sha' 2>&1
                if ($LASTEXITCODE -ne 0 -or ($resolvedSha | Out-String).Trim() -ne $sha) {
                    throw "Failed to verify commit SHA '$sha' in '$ownerRepo' (referenced in $($wf.Name)): $resolvedSha"
                }
                Write-Output "[verify-action-pins] Verified $key"
            }
        }
    }
}

Write-Output "[verify-action-pins] Verified $($verifiedPins.Count) unique action SHA pin(s) across $totalReferences 'uses:' reference(s) in $($workflowFiles.Count) workflow file(s)."
