[CmdletBinding()]
param(
    [string]$Confirm = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

$cliArgs = @('wipe')
if (-not [string]::IsNullOrWhiteSpace($Confirm)) {
    $cliArgs += @('--confirm', $Confirm)
}

& dotnet run --project "$repoRoot\src\RoleRadar.Cli" --no-build -c Release -- @cliArgs
exit $LASTEXITCODE
