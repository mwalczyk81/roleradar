[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$File,

    [switch]$ForceOverwrite,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($arg in $RemainingArgs) {
    if ($arg -match '^--passphrase(=.*)?$') {
        Write-Error 'Supplying passphrase via CLI argument is forbidden for security. Use interactive prompt, --passphrase-env, or --passphrase-stdin.'
        exit 1
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

$cliArgs = @('restore', $File)
if ($ForceOverwrite) {
    $cliArgs += '--force-overwrite'
}
$cliArgs += $RemainingArgs

& dotnet run --project "$repoRoot\src\RoleRadar.Cli" --no-build -c Release -- @cliArgs
exit $LASTEXITCODE
