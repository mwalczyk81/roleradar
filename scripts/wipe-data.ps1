[CmdletBinding()]
param(
    [string]$Confirm = '',

    [switch]$UseDocker,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

$cliArgs = @('wipe')
if (-not [string]::IsNullOrWhiteSpace($Confirm)) {
    $cliArgs += @('--confirm', $Confirm)
}
$cliArgs += $RemainingArgs

if ($UseDocker -or $env:ROLERADAR_USE_DOCKER -eq '1') {
    & (Join-Path $scriptDir 'roleradar-docker.ps1') @cliArgs
    exit $LASTEXITCODE
}

& dotnet run --project "$repoRoot/src/RoleRadar.Cli" --no-build -c Release -- @cliArgs
exit $LASTEXITCODE
