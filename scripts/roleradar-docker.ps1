[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$argList = [System.Collections.Generic.List[string]]::new()
if ($null -ne $Arguments -and $Arguments.Length -gt 0) {
    foreach ($a in $Arguments) {
        $argList.Add($a)
    }
}

if ($argList.Count -eq 0) {
    $argList.Add('--help')
}

$firstArg = $argList[0]

$isMcp = ($firstArg -eq 'mcp' -or $firstArg -eq '--mcp')
$isCli = -not $isMcp

$imageTag = if ($isMcp) {
    if (-not [string]::IsNullOrWhiteSpace($env:ROLERADAR_MCP_IMAGE)) { $env:ROLERADAR_MCP_IMAGE }
    elseif (-not [string]::IsNullOrWhiteSpace($env:ROLERADAR_IMAGE_TAG)) { $env:ROLERADAR_IMAGE_TAG }
    else { 'roleradar-mcp:test' }
} else {
    if (-not [string]::IsNullOrWhiteSpace($env:ROLERADAR_CLI_IMAGE)) { $env:ROLERADAR_CLI_IMAGE }
    elseif (-not [string]::IsNullOrWhiteSpace($env:ROLERADAR_IMAGE_TAG)) { $env:ROLERADAR_IMAGE_TAG }
    else { 'roleradar-cli:test' }
}

$forwardArgs = [System.Collections.Generic.List[string]]::new()
if ($isMcp) {
    for ($i = 1; $i -lt $argList.Count; $i++) {
        $forwardArgs.Add($argList[$i])
    }
} else {
    foreach ($a in $argList) {
        $forwardArgs.Add($a)
    }
}

$dockerArgs = [System.Collections.Generic.List[string]]::new()
$dockerArgs.Add('run')
$dockerArgs.Add('--rm')
$dockerArgs.Add('-i')
$dockerArgs.Add('--read-only')
$dockerArgs.Add('--tmpfs')
$dockerArgs.Add('/tmp:rw,noexec,nosuid,size=64m')
$dockerArgs.Add('--cap-drop')
$dockerArgs.Add('ALL')
$dockerArgs.Add('--security-opt')
$dockerArgs.Add('no-new-privileges')

$envVarsToPass = @(
    'ROLERADAR_MONGO_CONNECTION_STRING',
    'ROLERADAR_MONGO_DATABASE',
    'ROLERADAR_OWNER_ID',
    'ROLERADAR_LOG_LEVEL',
    'DOTNET_EnableDiagnostics',
    'DOTNET_SYSTEM_GLOBALIZATION_INVARIANT',
    'DOTNET_gcServer'
)

foreach ($varName in $envVarsToPass) {
    $val = [Environment]::GetEnvironmentVariable($varName)
    if (-not [string]::IsNullOrWhiteSpace($val)) {
        $dockerArgs.Add('-e')
        $dockerArgs.Add("$varName=$val")
    }
}

# Mount handling for CLI commands
if ($isCli -and $forwardArgs.Count -gt 0) {
    $subCmd = $forwardArgs[0]
    if ($subCmd -eq 'backup' -and $forwardArgs.Count -gt 1) {
        $destPath = $forwardArgs[1]
        if (-not (Test-Path -LiteralPath $destPath)) {
            New-Item -ItemType Directory -Path $destPath -Force | Out-Null
        }
        $absDest = (Resolve-Path $destPath).Path
        $dockerArgs.Add('-v')
        $dockerArgs.Add("${absDest}:/data/backup:rw")
        if (-not [System.OperatingSystem]::IsWindows()) {
            $uid = (id -u).Trim()
            $gid = (id -g).Trim()
            $dockerArgs.Add('--user')
            $dockerArgs.Add("${uid}:${gid}")
        }
        $forwardArgs[1] = '/data/backup'
    }
    elseif ($subCmd -eq 'restore' -and $forwardArgs.Count -gt 1) {
        $filePath = $forwardArgs[1]
        if (Test-Path -LiteralPath $filePath -PathType Leaf) {
            $absFile = (Resolve-Path $filePath).Path
            $fileDir = Split-Path -Parent $absFile
            $fileName = Split-Path -Leaf $absFile
            $dockerArgs.Add('-v')
            $dockerArgs.Add("${fileDir}:/data/restore:ro")
            $forwardArgs[1] = "/data/restore/$fileName"
        }
    }
    elseif ($subCmd -eq 'import' -and $forwardArgs.Count -gt 1) {
        $importPath = $forwardArgs[1]
        if (Test-Path -LiteralPath $importPath) {
            $absImport = (Resolve-Path $importPath).Path
            $dockerArgs.Add('-v')
            $dockerArgs.Add("${absImport}:/data/import:ro")
            $forwardArgs[1] = '/data/import'
        }
    }
}

$dockerArgs.Add($imageTag)
foreach ($f in $forwardArgs) {
    $dockerArgs.Add($f)
}

& docker @dockerArgs
exit $LASTEXITCODE
