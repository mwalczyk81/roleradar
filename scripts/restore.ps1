[CmdletBinding()]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingPlainTextForPassword', 'PassphraseEnv')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingPlainTextForPassword', 'Passphrase')]
param(
    [Parameter(Position = 0)]
    [Alias('ArchivePath')]
    [string]$File = '',

    [string]$PassphraseEnv = '',

    [switch]$PassphraseStdin,

    [switch]$ForceOverwrite,

    [switch]$UseDocker,

    [Parameter(DontShow = $true)]
    [string]$Passphrase = '',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PSBoundParameters.ContainsKey('Passphrase') -or
    $File -match '^--?passphrase(=.*)?$' -or
    ($RemainingArgs | Where-Object { $_ -match '^--?passphrase(=.*)?$' })) {
    [Console]::Error.WriteLine('Supplying passphrase via CLI argument is forbidden for security. Use interactive prompt, --passphrase-env, or --passphrase-stdin.')
    exit 1
}

if ([string]::IsNullOrWhiteSpace($File)) {
    [Console]::Error.WriteLine('Archive file path is required.')
    exit 1
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir

$cliArgs = @('restore', $File)
if (-not [string]::IsNullOrWhiteSpace($PassphraseEnv)) {
    $cliArgs += @('--passphrase-env', $PassphraseEnv)
}
if ($PassphraseStdin) {
    $cliArgs += '--passphrase-stdin'
}
if ($ForceOverwrite) {
    $cliArgs += '--force-overwrite'
}
$cliArgs += $RemainingArgs

if ($UseDocker -or $env:ROLERADAR_USE_DOCKER -eq '1') {
    & (Join-Path $scriptDir 'roleradar-docker.ps1') @cliArgs
    exit $LASTEXITCODE
}

& dotnet run --project "$repoRoot/src/RoleRadar.Cli" --no-build -c Release -- @cliArgs
exit $LASTEXITCODE
