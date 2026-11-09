# Pester tests for the History and ImageMetadata modes of scripts/denylist-check.ps1 (feature 012, FR-026).
# Everything runs in a throwaway git repository with a throwaway denylist; the real private denylist is never read.
# Pester 5 syntax (CI pins 5.9.1).

BeforeAll {
    $script:repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../../..')
    $script:checker = Join-Path $script:repoRoot 'scripts/denylist-check.ps1'
    $script:term = 'zebrafinch-fixture-term'
    $script:otherTerm = 'quokka-fixture-term'

    function New-ScratchRepo {
        $dir = Join-Path ([System.IO.Path]::GetTempPath()) ("rr-denylist-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dir | Out-Null
        Push-Location $dir
        try {
            git init -q -b main . 2>&1 | Out-Null
            git config user.name 'Scratch Author'
            git config user.email 'scratch@example.com'
            git config commit.gpgsign false
        } finally { Pop-Location }
        return $dir
    }

    function Add-ScratchCommit([string]$Dir, [string]$File, [string]$Content, [string]$Message) {
        Push-Location $Dir
        try {
            $parent = Split-Path -Parent (Join-Path $Dir $File)
            if (-not (Test-Path $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
            [System.IO.File]::WriteAllText((Join-Path $Dir $File), $Content, (New-Object System.Text.UTF8Encoding($false)))
            git add -A 2>&1 | Out-Null
            git commit -q -m $Message 2>&1 | Out-Null
        } finally { Pop-Location }
    }

    function New-Denylist([string[]]$Lines) {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("rr-denylist-" + [guid]::NewGuid().ToString('N') + '.txt')
        # BOM, CRLF, comment and blank lines: the loader must treat them like the existing modes do.
        $text = (@('# comment line', '', '   ') + $Lines + @('')) -join "`r`n"
        [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($true)))
        return $path
    }

    function Invoke-Checker([string]$Dir, [string]$Denylist, [string]$Mode) {
        Push-Location $Dir
        try {
            $env:ROLERADAR_DENYLIST_PATH = $Denylist
            $out = & pwsh -NoProfile -File $script:checker -Mode $Mode 2>&1 | Out-String
            return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $out }
        } finally {
            Remove-Item Env:ROLERADAR_DENYLIST_PATH -ErrorAction SilentlyContinue
            Pop-Location
        }
    }

    function New-PngWithText([string]$Path, [string]$Keyword, [string]$Text) {
        function Chunk([string]$Type, [byte[]]$Data) {
            $typeBytes = [System.Text.Encoding]::ASCII.GetBytes($Type)
            $crcInput = $typeBytes + $Data
            [uint64]$crc = 4294967295
            foreach ($b in $crcInput) {
                $crc = $crc -bxor [uint64]$b
                for ($i = 0; $i -lt 8; $i++) {
                    if (($crc -band 1) -eq 1) { $crc = ($crc -shr 1) -bxor [uint64]3988292384 } else { $crc = $crc -shr 1 }
                }
            }
            $crc = $crc -bxor [uint64]4294967295
            $len = [System.BitConverter]::GetBytes([uint32]$Data.Length); [array]::Reverse($len)
            $crcBytes = [System.BitConverter]::GetBytes([uint32]$crc); [array]::Reverse($crcBytes)
            return $len + $typeBytes + $Data + $crcBytes
        }
        $sig = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
        $ihdr = [byte[]](0,0,0,1, 0,0,0,1, 8, 2, 0, 0, 0)
        $ms = New-Object System.IO.MemoryStream
        $z = New-Object System.IO.Compression.ZLibStream($ms, [System.IO.Compression.CompressionMode]::Compress, $true)
        $raw = [byte[]](0, 0x9a, 0x9a, 0x9a); $z.Write($raw, 0, $raw.Length); $z.Dispose()
        $bytes = $sig + (Chunk 'IHDR' $ihdr)
        if ($Text) {
            $bytes += (Chunk 'tEXt' ([System.Text.Encoding]::Latin1.GetBytes("$Keyword`0$Text")))
        }
        $bytes += (Chunk 'IDAT' $ms.ToArray()) + (Chunk 'IEND' ([byte[]]@()))
        [System.IO.File]::WriteAllBytes($Path, $bytes)
    }
}

Describe 'denylist-check History mode' {
    It 'exits 0 and prints counts only when the history is clean' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'a.txt' 'harmless content' 'first commit'
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'History'
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'commits scanned: 1'
        $r.Output | Should -Match 'matching commit messages or authors: 0'
        $r.Output | Should -Match 'matching file contents: 0'
        $r.Output | Should -Not -Match $script:term
    }

    It 'fails on a match in file content, names the file and never prints the term' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'notes/secret.txt' "see $($script:term) here" 'add notes'
        Add-ScratchCommit $repo 'notes/secret.txt' 'cleaned' 'clean notes'
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'History'
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'matching file contents: 1'
        $r.Output | Should -Match 'notes/secret\.txt'
        $r.Output | Should -Not -Match $script:term
    }

    It 'fails on a match in a commit message and in an author name' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'a.txt' 'x' "mentions $($script:term) in the message"
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'History'
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'matching commit messages or authors: 1'
        $r.Output | Should -Not -Match $script:term

        $repo2 = New-ScratchRepo
        Push-Location $repo2
        try {
            git config user.name "Dr $($script:otherTerm)"
            Add-ScratchCommit $repo2 'a.txt' 'x' 'plain message'
        } finally { Pop-Location }
        $list2 = New-Denylist @($script:otherTerm)
        $r2 = Invoke-Checker $repo2 $list2 'History'
        $r2.ExitCode | Should -Be 1
        $r2.Output | Should -Match 'matching commit messages or authors: 1'
        $r2.Output | Should -Not -Match $script:otherTerm
    }

    It 'matches whole words only, like the existing modes' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'a.txt' "prefix$($script:term)suffix" 'embedded in a longer word'
        $list = New-Denylist @($script:term)
        (Invoke-Checker $repo $list 'History').ExitCode | Should -Be 0
    }

    It 'ignores comment lines, blank lines and a BOM in the denylist' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'a.txt' 'comment-only-term appears here' 'c1'
        $list = New-Denylist @('# comment-only-term')
        # Only a comment mentions the term, so there are no active terms: the loader refuses, as the existing modes do.
        $r = Invoke-Checker $repo $list 'History'
        $r.ExitCode | Should -Not -Be 0
        $r.Output | Should -Match 'no active terms'
    }

    It 'refuses to run without a denylist' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'a.txt' 'x' 'c1'
        $r = Invoke-Checker $repo (Join-Path ([System.IO.Path]::GetTempPath()) 'does-not-exist.txt') 'History'
        $r.ExitCode | Should -Not -Be 0
    }
}

Describe 'denylist-check override warning' {
    It 'prints a loud warning naming ROLERADAR_DENYLIST_PATH whenever the override is used' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'a.txt' 'harmless' 'c1'
        $list = New-Denylist @($script:term)
        foreach ($mode in @('History', 'ImageMetadata')) {
            $r = Invoke-Checker $repo $list $mode
            $r.Output | Should -Match 'WARNING.*ROLERADAR_DENYLIST_PATH'
            $r.Output | Should -Match 'not the private denylist'
        }
    }
}

Describe 'denylist-check ImageMetadata mode' {
    It 'exits 0 when the images carry no term and prints counts only' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'README.md' 'x' 'c1'
        New-Item -ItemType Directory -Path (Join-Path $repo 'docs/images') -Force | Out-Null
        New-PngWithText (Join-Path $repo 'docs/images/clean.png') $null $null
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'ImageMetadata'
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'images scanned: 1'
        $r.Output | Should -Match 'matching images: 0'
    }

    It 'fails on a term in a PNG text chunk, names the file and never prints the term' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'README.md' 'x' 'c1'
        New-Item -ItemType Directory -Path (Join-Path $repo 'docs/images') -Force | Out-Null
        New-PngWithText (Join-Path $repo 'docs/images/leaky.png') 'Comment' "made by $($script:term)"
        New-PngWithText (Join-Path $repo 'docs/images/clean.png') $null $null
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'ImageMetadata'
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Match 'matching images: 1'
        $r.Output | Should -Match 'leaky\.png'
        $r.Output | Should -Not -Match 'clean\.png'
        $r.Output | Should -Not -Match $script:term
    }

    It 'fails on a term in an image file name' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'README.md' 'x' 'c1'
        New-Item -ItemType Directory -Path (Join-Path $repo 'docs/images') -Force | Out-Null
        New-PngWithText (Join-Path $repo "docs/images/$($script:term).png") $null $null
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'ImageMetadata'
        $r.ExitCode | Should -Be 1
        $r.Output | Should -Not -Match $script:term
    }

    It 'passes when docs/images does not exist' {
        $repo = New-ScratchRepo
        Add-ScratchCommit $repo 'README.md' 'x' 'c1'
        $list = New-Denylist @($script:term)
        $r = Invoke-Checker $repo $list 'ImageMetadata'
        $r.ExitCode | Should -Be 0
        $r.Output | Should -Match 'images scanned: 0'
    }
}
