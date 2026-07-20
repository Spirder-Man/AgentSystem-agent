# dedupe-docs.ps1 - One-shot doc deduplication tool (2026-07-18 repo normalization)
# Rule: only move files whose SHA256 hash is IDENTICAL to the kept copy.
# Duplicates are moved (never deleted) into _archive/docs-duplicates/ preserving relative paths.
# Pure-ASCII script: all Chinese/emoji filenames are resolved at runtime via Get-ChildItem.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $repo "docs"
$dup  = Join-Path $PSScriptRoot "docs-duplicates"
$moved = New-Object System.Collections.Generic.List[string]

function Move-Dup([string]$srcFile) {
    $rel  = $srcFile.Substring($repo.Length + 1)
    $dest = Join-Path $dup $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
    Move-Item -LiteralPath $srcFile -Destination $dest -Force
    $script:moved.Add($rel)
}

function Dedupe-Pair([string]$srcDir, [string]$keepDir) {
    if (-not (Test-Path $srcDir) -or -not (Test-Path $keepDir)) { return }
    Get-ChildItem -LiteralPath $srcDir -File | ForEach-Object {
        $keepFile = Join-Path $keepDir $_.Name
        if (Test-Path -LiteralPath $keepFile) {
            $h1 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            $h2 = (Get-FileHash -LiteralPath $keepFile -Algorithm SHA256).Hash
            if ($h1 -eq $h2) { Move-Dup $_.FullName }
        }
    }
}

function Dedupe-AssetsDir([string]$srcDir, [string]$keepDir) {
    # Compare same-named subdirectories file-by-file; move whole dir only if ALL files match.
    if (-not (Test-Path $srcDir) -or -not (Test-Path $keepDir)) { return }
    Get-ChildItem -LiteralPath $srcDir -Directory | ForEach-Object {
        $keepSub = Join-Path $keepDir $_.Name
        if (-not (Test-Path -LiteralPath $keepSub)) { return }
        $srcFiles = Get-ChildItem -LiteralPath $_.FullName -Recurse -File
        $allMatch = $true
        foreach ($f in $srcFiles) {
            $relInside = $f.FullName.Substring($_.FullName.Length + 1)
            $twin = Join-Path $keepSub $relInside
            if (-not (Test-Path -LiteralPath $twin)) { $allMatch = $false; break }
            $h1 = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
            $h2 = (Get-FileHash -LiteralPath $twin -Algorithm SHA256).Hash
            if ($h1 -ne $h2) { $allMatch = $false; break }
        }
        if ($allMatch -and $srcFiles.Count -gt 0) {
            $rel  = $_.FullName.Substring($repo.Length + 1)
            $dest = Join-Path $dup $rel
            New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
            Move-Item -LiteralPath $_.FullName -Destination $dest -Force
            $script:moved.Add($rel + "\ (whole dir, " + $srcFiles.Count + " files)")
        }
    }
}

# --- Group A: docs root vs category subdirs ---
Dedupe-Pair $docs (Join-Path $docs "architecture")
Dedupe-Pair $docs (Join-Path $docs "technical-principles")
Dedupe-Pair $docs (Join-Path $docs "testing\integration")
Dedupe-AssetsDir $docs (Join-Path $docs "technical-principles")

# --- Group B: docs/testing root vs unit/integration/manual ---
$testing = Join-Path $docs "testing"
Dedupe-Pair $testing (Join-Path $testing "integration")
Dedupe-Pair $testing (Join-Path $testing "unit")
Dedupe-Pair $testing (Join-Path $testing "manual")

# --- Group C: docs/architecture/_archive vs current versions ---
$archArchive = Join-Path $docs "architecture\_archive"
Dedupe-Pair $archArchive (Join-Path $docs "architecture")
Dedupe-Pair $archArchive (Join-Path $docs "articles")

# --- Group D: docs/_archive vs category dirs ---
$docsArchive = Join-Path $docs "_archive"
Dedupe-Pair $docsArchive (Join-Path $docs "technical-principles")
Dedupe-Pair $docsArchive (Join-Path $docs "project")
Dedupe-Pair $docsArchive (Join-Path $docs "learning-notes")
Dedupe-Pair $docsArchive (Join-Path $docs "articles")

# --- Group E: docs/articles vs learning-notes (keep learning-notes copy) ---
Dedupe-Pair (Join-Path $docs "articles") (Join-Path $docs "learning-notes")

# --- Group F: intra-dir identical copies inside technical-principles (backup twins) ---
$tp = Join-Path $docs "technical-principles"
$groups = Get-ChildItem -LiteralPath $tp -File |
    ForEach-Object { [pscustomobject]@{ File = $_; Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } |
    Group-Object Hash | Where-Object { $_.Count -gt 1 }
foreach ($g in $groups) {
    $keep = $g.Group | Sort-Object { $_.File.Name.Length } | Select-Object -First 1
    foreach ($item in $g.Group) {
        if ($item.File.FullName -ne $keep.File.FullName) { Move-Dup $item.File.FullName }
    }
}

# --- Report ---
$report = Join-Path $dup "moved-list.txt"
$moved | Out-File -FilePath $report -Encoding UTF8
Write-Output ("MOVED_COUNT=" + $moved.Count)
Write-Output ("REPORT=" + $report)
