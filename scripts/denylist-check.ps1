[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Staged', 'CommitMsg', 'PrePush', 'History', 'ImageMetadata')]
    [string]$Mode,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localAppData)) { $localAppData = $env:LOCALAPPDATA }
    $denylistPath = Join-Path -Path "$localAppData" -ChildPath 'roleradar\private\denylist.txt'
} else {
    $xdgDataHome = $env:XDG_DATA_HOME
    if ([string]::IsNullOrWhiteSpace($xdgDataHome)) {
        $homeDir = if (-not [string]::IsNullOrWhiteSpace($env:HOME)) { $env:HOME } else { [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile) }
        $xdgDataHome = Join-Path -Path $homeDir -ChildPath '.local/share'
    }
    $denylistPath = Join-Path -Path $xdgDataHome -ChildPath 'roleradar/private/denylist.txt'
}

# Test seam for the Pester tests (a throwaway list). Unset in normal use, so the private list stays outside the repository.
if (-not [string]::IsNullOrWhiteSpace($env:ROLERADAR_DENYLIST_PATH)) {
    $denylistPath = $env:ROLERADAR_DENYLIST_PATH
    # Loud on purpose: a result obtained with a substitute list says nothing about the private list. Never set this for a real check.
    Write-Warning 'WARNING: ROLERADAR_DENYLIST_PATH is set. This run uses that file, not the private denylist, so a clean result proves nothing about real names.'
}

if (-not (Test-Path -LiteralPath $denylistPath -PathType Leaf)) {
    $msg = "LOUD WARNING: Local private denylist not found at '$denylistPath'. Create it (one term per line) or set ROLERADAR_ALLOW_NO_DENYLIST=1 to bypass."
    if ($env:ROLERADAR_ALLOW_NO_DENYLIST -eq '1') { Write-Warning $msg; return }
    throw $msg
}

$terms = @(
    Get-Content -LiteralPath $denylistPath -Encoding UTF8 |
    ForEach-Object { $_.TrimStart([char]0xFEFF).Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and -not $_.StartsWith('#') }
)
if ($terms.Count -eq 0) {
    throw "Private denylist at '$denylistPath' exists but contains no active terms."
}

# History and ImageMetadata (feature 012, FR-026): one-time local checks before announcing the repository.
# Output rule: counts and file names only, never matched text. A file name that itself matches is redacted.
if ($Mode -eq 'History' -or $Mode -eq 'ImageMetadata') {
    $combined = '(?i)(?<!\w)(?:' + (($terms | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')(?!\w)'
    $repoTop = @(& git rev-parse --show-toplevel 2>$null)
    $repoTop = if ($repoTop.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($repoTop[0])) { $repoTop[0] } else { (Get-Location).Path }

    function Get-SafeName([string]$name) {
        if ($name -match $combined) { return '(file name redacted)' }
        return $name
    }

    if ($Mode -eq 'History') {
        $revs = @(& git rev-list --all 2>$null)
        Write-Output "History: commits scanned: $($revs.Count)"

        $messageHits = 0
        $messageHitIds = New-Object System.Collections.Generic.List[string]
        foreach ($rev in $revs) {
            $meta = (@(& git log -1 --format='%an%n%ae%n%cn%n%ce%n%B' $rev 2>$null)) -join "`n"
            if ($meta -match $combined) { $messageHits++; $messageHitIds.Add($rev.Substring(0, 7)) }
        }
        Write-Output "History: matching commit messages or authors: $messageHits"
        foreach ($id in $messageHitIds) { Write-Output "  commit $id" }

        $patternFile = Join-Path ([System.IO.Path]::GetTempPath()) ("rr-denylist-patterns-" + [guid]::NewGuid().ToString('N') + '.txt')
        $fileHits = New-Object System.Collections.Generic.List[string]
        try {
            [System.IO.File]::WriteAllText($patternFile, (($terms) -join "`n") + "`n", (New-Object System.Text.UTF8Encoding($false)))
            for ($i = 0; $i -lt $revs.Count; $i += 100) {
                $chunk = @($revs[$i..([Math]::Min($i + 99, $revs.Count - 1))])
                foreach ($line in @(& git grep -l -a -i -w -F -f $patternFile @chunk 2>$null)) {
                    $sep = $line.IndexOf(':')
                    if ($sep -lt 7) { continue }
                    $fileHits.Add("$($line.Substring(0, 7)):$(Get-SafeName $line.Substring($sep + 1))")
                }
            }
        } finally {
            Remove-Item -LiteralPath $patternFile -Force -ErrorAction SilentlyContinue
        }
        Write-Output "History: matching file contents: $($fileHits.Count)"
        foreach ($hit in $fileHits) { Write-Output "  $hit" }

        if (($messageHits + $fileHits.Count) -gt 0) { exit 1 }
        exit 0
    }

    # ImageMetadata
    $imageDir = Join-Path $repoTop 'docs/images'
    $images = @(if (Test-Path -LiteralPath $imageDir -PathType Container) { Get-ChildItem -LiteralPath $imageDir -File -Recurse })
    Write-Output "ImageMetadata: images scanned: $($images.Count)"

    $matching = New-Object System.Collections.Generic.List[string]
    $withTextChunks = 0
    foreach ($image in $images) {
        $relative = 'docs/images/' + [System.IO.Path]::GetRelativePath($imageDir, $image.FullName).Replace('\', '/')
        $bytes = [System.IO.File]::ReadAllBytes($image.FullName)
        $hit = ($image.Name -match $combined) -or ([System.Text.Encoding]::Latin1.GetString($bytes) -match $combined)

        # Decode PNG text chunks, including the compressed ones, so a term hidden by compression is still found.
        $png = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
        if ($bytes.Length -gt 8 -and -not (Compare-Object $bytes[0..7] $png)) {
            $pos = 8
            $sawText = $false
            while ($pos + 8 -le $bytes.Length) {
                $len = ([int]$bytes[$pos] -shl 24) -bor ([int]$bytes[$pos + 1] -shl 16) -bor ([int]$bytes[$pos + 2] -shl 8) -bor [int]$bytes[$pos + 3]
                $type = [System.Text.Encoding]::ASCII.GetString($bytes, $pos + 4, 4)
                if ($len -lt 0 -or $pos + 12 + $len -gt $bytes.Length) { break }
                if ($type -in @('tEXt', 'zTXt', 'iTXt', 'eXIf')) {
                    $sawText = $true
                    $data = $bytes[($pos + 8)..($pos + 7 + $len)]
                    $decoded = [System.Text.Encoding]::UTF8.GetString($data)
                    if ($type -eq 'zTXt' -or $type -eq 'iTXt') {
                        # Try to inflate everything after the first NUL; failures are ignored (the raw scan above still ran).
                        for ($k = 0; $k -lt $data.Length - 2; $k++) {
                            if ($data[$k + 1] -eq 0x78) {
                                try {
                                    $ms = New-Object System.IO.MemoryStream (, [byte[]]$data[($k + 1)..($data.Length - 1)])
                                    $z = New-Object System.IO.Compression.ZLibStream($ms, [System.IO.Compression.CompressionMode]::Decompress)
                                    $out = New-Object System.IO.MemoryStream
                                    $z.CopyTo($out)
                                    $decoded += [System.Text.Encoding]::UTF8.GetString($out.ToArray())
                                } catch {
                                    # Not zlib data at this offset: ignored on purpose, the raw scan above still ran.
                                    Write-Verbose "ImageMetadata: chunk payload at offset $k is not zlib data."
                                }
                            }
                        }
                    }
                    if ($decoded -match $combined) { $hit = $true }
                }
                $pos += 12 + $len
            }
            if ($sawText) { $withTextChunks++ }
        }

        if ($hit) { $matching.Add((Get-SafeName $relative)) }
    }
    Write-Output "ImageMetadata: images with text metadata chunks: $withTextChunks"
    Write-Output "ImageMetadata: matching images: $($matching.Count)"
    foreach ($name in $matching) { Write-Output "  $name" }
    if ($matching.Count -gt 0) { exit 1 }
    exit 0
}

$textToScan = switch ($Mode) {
    'Staged' {
        @(& git diff --cached -U0 --no-color 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') } | ForEach-Object { $_.Substring(1) }) -join "`n"
    }
    'CommitMsg' {
        if ($RemainingArgs.Count -eq 0 -or -not (Test-Path -LiteralPath $RemainingArgs[0] -PathType Leaf)) {
            throw 'CommitMsg mode requires a valid commit message file path.'
        }
        @(Get-Content -LiteralPath $RemainingArgs[0] -Encoding UTF8 | Where-Object { -not $_.StartsWith('#') }) -join "`n"
    }
    'PrePush' {
        $fromRef = $env:PRE_COMMIT_FROM_REF
        $toRef = if ([string]::IsNullOrWhiteSpace($env:PRE_COMMIT_TO_REF)) { 'HEAD' } else { $env:PRE_COMMIT_TO_REF }
        if (-not [string]::IsNullOrWhiteSpace($fromRef) -and $fromRef -notmatch '^0+$') {
            $msgs = @(& git log --no-patch --format=%B "$fromRef..$toRef" 2>$null)
            $diffs = @(& git log -p -U0 --no-color --format= "$fromRef..$toRef" 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') } | ForEach-Object { $_.Substring(1) })
        } else {
            $revs = @(& git rev-list $toRef --not --remotes 2>$null)
            $msgs = if ($revs.Count -gt 0) { @($revs | & git log --stdin --no-walk --no-patch --format=%B 2>$null) } else { @() }
            $diffs = if ($revs.Count -gt 0) { @($revs | & git log --stdin --no-walk -p -U0 --no-color --format= 2>$null | Where-Object { $_.StartsWith('+') -and -not $_.StartsWith('+++') } | ForEach-Object { $_.Substring(1) }) } else { @() }
        }
        @($msgs + $diffs) -join "`n"
    }
}

foreach ($term in $terms) {
    if ($textToScan -match "(?i)(?<!\w)$([regex]::Escape($term))(?!\w)") {
        throw "Private denylist check ($Mode) failed: input matched a forbidden private term."
    }
}
