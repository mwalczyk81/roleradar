[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [Alias('OutputDirectory')]
    [string]$Destination = '.',

    [string]$PassphraseEnv = '',

    [switch]$PassphraseStdin,

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

$cliArgs = @('backup', $Destination)
if (-not [string]::IsNullOrWhiteSpace($PassphraseEnv)) {
    $cliArgs += @('--passphrase-env', $PassphraseEnv)
}
if ($PassphraseStdin) {
    $cliArgs += '--passphrase-stdin'
}
$cliArgs += $RemainingArgs

& dotnet run --project "$repoRoot\src\RoleRadar.Cli" --no-build -c Release -- @cliArgs
exit $LASTEXITCODE
