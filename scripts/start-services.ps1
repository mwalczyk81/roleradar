[CmdletBinding()]
param(
    [string]$ComposeFile = '',
    [string]$ProjectName = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = $env:ROLERADAR_COMPOSE_FILE
}
if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path (Split-Path -Parent $scriptDir) 'deploy/docker-compose.yml'
}

$composeArgs = @('-f', $ComposeFile)
if (-not [string]::IsNullOrWhiteSpace($ProjectName)) {
    $composeArgs += @('-p', $ProjectName)
}

# Ensure external data volume exists before starting compose services
$volInspect = & docker volume inspect roleradar-mongo-data 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Output "Creating external docker volume 'roleradar-mongo-data'..."
    & docker volume create roleradar-mongo-data | Out-Null
}

Write-Output "Starting RoleRadar data services using $ComposeFile..."
& docker compose @composeArgs up -d

Write-Output 'Waiting for MongoDB container to become healthy...'
$maxRetries = 30
$retryCount = 0
$healthy = $false

while ($retryCount -lt $maxRetries) {
    $containerId = (& docker compose @composeArgs ps -q mongo 2>$null)
    if (-not [string]::IsNullOrWhiteSpace($containerId)) {
        $status = (& docker inspect --format='{{json .State.Health.Status}}' $containerId.Trim() 2>$null)
        if ($status -eq '"healthy"') {
            $healthy = $true
            break
        }
    }
    Start-Sleep -Seconds 1
    $retryCount++
}

if (-not $healthy) {
    Write-Error "MongoDB container failed to report healthy status within $maxRetries seconds."
    exit 1
}

$portOutput = (& docker compose @composeArgs port mongo 27017 2>$null)
$endpoint = if (-not [string]::IsNullOrWhiteSpace($portOutput)) { $portOutput.Trim() } else { '127.0.0.1:27017' }
Write-Output "RoleRadar MongoDB service is healthy and ready on $endpoint (rs0)."
exit 0
