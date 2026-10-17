[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Destination = '.',

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

& dotnet run --project "$repoRoot\src\RoleRadar.Cli" --no-build -c Release -- backup $Destination @RemainingArgs
exit $LASTEXITCODE
