[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$composeFile = Join-Path (Split-Path -Parent $scriptDir) 'deploy\docker-compose.yml'

Write-Host "Starting RoleRadar data services using $composeFile..."
& docker compose -f $composeFile up -d

Write-Host 'Waiting for MongoDB container to become healthy...'
$maxRetries = 30
$retryCount = 0
$healthy = $false

while ($retryCount -lt $maxRetries) {
    $status = (& docker inspect --format='{{json .State.Health.Status}}' roleradar-mongo 2>$null)
    if ($status -eq '"healthy"') {
        $healthy = $true
        break
    }
    Start-Sleep -Seconds 1
    $retryCount++
}

if (-not $healthy) {
    Write-Error "MongoDB container failed to report healthy status within $maxRetries seconds."
    exit 1
}

Write-Host 'RoleRadar MongoDB service is healthy and ready on 127.0.0.1:27017 (rs0).'
exit 0
