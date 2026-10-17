[CmdletBinding()]
param(
    [string]$Confirm = '',

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

& dotnet run --project "$repoRoot\src\RoleRadar.Cli" --no-build -c Release -- @cliArgs
exit $LASTEXITCODE
