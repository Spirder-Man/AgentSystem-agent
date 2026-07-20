<#
.SYNOPSIS
    Agent1 Docs Full-Text Search
    Usage: .\search-docs.ps1 <keyword> [-Detail] [-Brief] [-FName] [-Content] [-Regex] [-Dir subdir] [-Top N] [-Outline N] [-Help]
#>

param(
    [Parameter(Position = 0)]
    [string]$Keyword,

    [switch]$Help,
    [switch]$Detail,
    [switch]$Brief,
    [switch]$FName,
    [switch]$Content,
    [switch]$Regex,
    [string]$Dir = "",
    [int]$Top = 15,
    [int]$Outline = 0
)

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$DocsPath  = Join-Path $ScriptDir "..\docs" | Resolve-Path
$Typora    = "D:\WuHan\Typora\Typora.exe"

# ============================================================
# HELP
# ============================================================
if ($Help -or (-not $Keyword)) {
    Write-Host ""
    Write-Host "  search-docs.ps1  --  Agent1 Doc Search" -ForegroundColor Cyan
    Write-Host "  =============================================" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  USAGE" -ForegroundColor Yellow
    Write-Host "  -----"
    Write-Host "  .\search-docs.ps1 KEYWORD              full-text search (filename + content)"
    Write-Host ""
    Write-Host "  OPTIONS" -ForegroundColor Yellow
    Write-Host "  -------"
    Write-Host "  -Detail      show matching line snippets"
    Write-Host "  -Brief       file paths only, one per line"
    Write-Host "  -FName       search filenames only"
    Write-Host "  -Content     search content only"
    Write-Host "  -Regex       use regex pattern (default: literal match)"
    Write-Host "  -Dir NAME    limit to subdirectory, e.g. -Dir testing"
    Write-Host "  -Top N       show top N files (default 15)"
    Write-Host "  -Outline N   show outline of result N"
    Write-Host "  -Help        show this help"
    Write-Host ""
    Write-Host "  INTERACTIVE (after search)" -ForegroundColor Yellow
    Write-Host "  ---------------------------"
    Write-Host "  1-15         open file in Typora"
    Write-Host "  o1-o15       show outline / table of contents"
    Write-Host "  f WORD       filter current results by keyword"
    Write-Host "  r            redisplay current results"
    Write-Host "  q            quit"
    Write-Host ""
    Write-Host "  EXAMPLES" -ForegroundColor Yellow
    Write-Host "  --------"
    Write-Host "  .\search-docs.ps1 BM25                   find all docs mentioning BM25"
    Write-Host "  .\search-docs.ps1 CoT -Outline 1         search CoT, show outline of #1"
    Write-Host "  .\search-docs.ps1 'CoT|ReAct' -Regex     regex search (OR match)"
    Write-Host "  .\search-docs.ps1 eval -Dir testing      search 'eval' under testing/"
    Write-Host "  .\search-docs.ps1 deploy -FName          find docs with 'deploy' in filename"
    Write-Host ""
    exit 0
}

# ============================================================
# SEARCH
# ============================================================
$SearchPath = if ($Dir) { Join-Path $DocsPath $Dir } else { $DocsPath }
if (-not (Test-Path $SearchPath)) {
    Write-Host "ERROR: directory not found: $SearchPath" -ForegroundColor Red
    exit 1
}

$results = @()

# get all .md and .html files
$allFiles = Get-ChildItem -Path $SearchPath -Recurse -File `
    | Where-Object { $_.Extension -match '\.(md|html)$' }

if (-not $Content) {
    $allFiles `
    | Where-Object { $_.Name -like "*$Keyword*" } `
    | ForEach-Object {
        $results += [PSCustomObject]@{ Path = $_.FullName.Replace($DocsPath.ToString() + "\", ""); Type = "FName"; Line = 0; Text = "" }
    }
}

if (-not $FName) {
    if ($Regex) {
        $allFiles | Select-String -Pattern $Keyword | ForEach-Object {
            $results += [PSCustomObject]@{ Path = $_.Path.Replace($DocsPath.ToString() + "\", ""); Type = "Content"; Line = $_.LineNumber; Text = $_.Line.Trim() }
        }
    } else {
        $allFiles | Select-String -Pattern $Keyword -SimpleMatch | ForEach-Object {
            $results += [PSCustomObject]@{ Path = $_.Path.Replace($DocsPath.ToString() + "\", ""); Type = "Content"; Line = $_.LineNumber; Text = $_.Line.Trim() }
        }
    }
}

if ($results.Count -eq 0) {
    Write-Host "No matches found for: $Keyword" -ForegroundColor DarkYellow
    exit 0
}

# ============================================================
# GROUP & RANK
# ============================================================
$grouped = $results | Group-Object Path | ForEach-Object {
    $fn  = ($_.Group | Where-Object Type -eq "FName").Count
    $ct  = ($_.Group | Where-Object Type -eq "Content").Count
    $first = $_.Group | Where-Object Type -eq "Content" | Select-Object -First 1
    $firstLine = if ($first) { $first.Line } else { 1 }
    $txt = if ($first) { $first.Text } else { "" }
    if ($txt.Length -gt 80) { $txt = $txt.Substring(0,77) + "..." }
    
    [PSCustomObject]@{
        Path      = $_.Name
        FNameHit  = $fn -gt 0
        Hits      = $ct
        Score     = ($fn * 1000) + $ct
        Snippet   = $txt
        FirstLine = $firstLine
        Lines     = @($_.Group | Where-Object Type -eq "Content")
    }
} | Sort-Object Score -Descending | Select-Object -First $Top

$totalHits = $results.Count

# active results (may be filtered later), SearchResults always holds original
$global:SearchResults = @($grouped)
$global:SearchDocsPath = $DocsPath
$activeResults = @($grouped)

# ============================================================
# REDISPLAY FUNCTION
# ============================================================
function Redisplay-Results($docs, $hits, $kw) {
    Write-Host ""
    Write-Host "  Keyword: " -NoNewline
    Write-Host $kw -ForegroundColor Yellow -NoNewline
    Write-Host "  |  " -NoNewline -ForegroundColor DarkGray
    Write-Host "$hits" -ForegroundColor White -NoNewline
    Write-Host " hits / " -NoNewline -ForegroundColor DarkGray
    Write-Host "$($docs.Count)" -ForegroundColor White -NoNewline
    Write-Host " files" -ForegroundColor DarkGray
    Write-Host "  " -NoNewline
    Write-Host "--------------------------------------------------" -ForegroundColor DarkGray

    $rank = 0
    foreach ($f in $docs) {
        $rank++
        $star = if ($f.FNameHit) { "*" } else { " " }

        if ($Brief) {
            Write-Host "  $($rank.ToString().PadLeft(2)) $star docs\$($f.Path):$($f.FirstLine)"
            continue
        }

        Write-Host "  $($rank.ToString().PadLeft(2)) " -NoNewline -ForegroundColor DarkGray
        Write-Host $star -NoNewline -ForegroundColor Yellow
        Write-Host " docs\$($f.Path):$($f.FirstLine) " -ForegroundColor White -BackgroundColor DarkBlue

        Write-Host "      " -NoNewline
        Write-Host "[$($f.Hits) hits" -ForegroundColor Yellow -NoNewline
        Write-Host " L$($f.FirstLine)]" -ForegroundColor DarkGray -NoNewline
        if ($f.Snippet) {
            Write-Host "  " -NoNewline
            Write-Host $f.Snippet -ForegroundColor Gray
        } else {
            Write-Host ""
        }

        if ($Detail -and $f.Lines.Count -gt 1) {
            $extra = $f.Lines | Select-Object -Skip 1 | Select-Object -First 3
            foreach ($l in $extra) {
                $s = $l.Text
                if ($s.Length -gt 100) { $s = $s.Substring(0,97) + "..." }
                Write-Host "           L$($l.Line.ToString().PadLeft(4)) " -NoNewline -ForegroundColor DarkGray
                Write-Host $s -ForegroundColor Gray
            }
            if ($f.Lines.Count -gt 4) {
                Write-Host "           " -NoNewline
                Write-Host "... +$($f.Lines.Count - 4) more" -ForegroundColor DarkGray
            }
        }
    }
    Write-Host "  " -NoNewline
    Write-Host "--------------------------------------------------" -ForegroundColor DarkGray
}

# ============================================================
# OUTLINE FUNCTION
# ============================================================
function Show-Outline($doc, $num) {
    $full = Join-Path $global:SearchDocsPath $doc.Path
    if (-not (Test-Path $full)) {
        Write-Host "  file not found: $full" -ForegroundColor Red
        return
    }
    Write-Host ""
    Write-Host "  ===== Outline of [$num] " -NoNewline -ForegroundColor Cyan
    Write-Host $doc.Path -ForegroundColor White -NoNewline
    Write-Host " =====" -ForegroundColor Cyan
    Write-Host ""
    try {
        $lines = Get-Content $full -Encoding UTF8 -ErrorAction Stop
        $inCode = $false
        $lineNo = 0
        foreach ($l in $lines) {
            $lineNo++
            if ($l -match '^```') { $inCode = -not $inCode; continue }
            if ($inCode) { continue }
            if ($l -match '^#{4}\s+(.+)') {
                Write-Host "          L$($lineNo.ToString().PadLeft(4)) " -NoNewline -ForegroundColor DarkGray
                Write-Host $Matches[1] -ForegroundColor DarkGray
            }
            elseif ($l -match '^#{3}\s+(.+)') {
                Write-Host "       L$($lineNo.ToString().PadLeft(4)) " -NoNewline -ForegroundColor DarkGray
                Write-Host $Matches[1] -ForegroundColor DarkGray
            }
            elseif ($l -match '^#{2}\s+(.+)') {
                Write-Host "    L$($lineNo.ToString().PadLeft(4)) " -NoNewline -ForegroundColor DarkGray
                Write-Host $Matches[1] -ForegroundColor Gray
            }
            elseif ($l -match '^#{1}\s+(.+)') {
                Write-Host "  L$($lineNo.ToString().PadLeft(4)) " -NoNewline -ForegroundColor DarkGray
                Write-Host $Matches[1] -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "  read error" -ForegroundColor Red
    }
    Write-Host ""
}

# ============================================================
# INITIAL OUTPUT
# ============================================================
Redisplay-Results $activeResults $totalHits $Keyword

# -Outline: non-interactive outline mode
if ($Outline -gt 0 -and $Outline -le $grouped.Count) {
    Show-Outline $grouped[$Outline - 1] $Outline
    exit 0
}

# ============================================================
# INTERACTIVE MODE
# ============================================================
Write-Host "  [1-$($activeResults.Count)] open  |  o1-o$($activeResults.Count) outline  |  f filter  |  r redisplay  |  q quit" -ForegroundColor Cyan
Write-Host ""

while ($true) {
    Write-Host "  > " -NoNewline -ForegroundColor Yellow
    $input = Read-Host
    
    if ($input -eq 'q' -or $input -eq 'quit') {
        Write-Host "  bye." -ForegroundColor DarkGray
        break
    }
    
    # redisplay: r
    if ($input -eq 'r') {
        Redisplay-Results $activeResults $totalHits $Keyword
        Write-Host "  [1-$($activeResults.Count)] open  |  o1-o$($activeResults.Count) outline  |  f filter  |  r redisplay  |  q quit" -ForegroundColor Cyan
        Write-Host ""
        continue
    }
    
    # filter: f keyword
    if ($input -match '^f\s+(.+)$') {
        $fkw = $Matches[1]
        $filtered = @()
        foreach ($doc in $activeResults) {
            $fullPath = Join-Path $global:SearchDocsPath $doc.Path
            $match = Select-String -Path $fullPath -Pattern $fkw -SimpleMatch | Select-Object -First 1
            if ($match) {
                $newDoc = $doc.PSObject.Copy()
                $newDoc.Snippet = $match.Line.Trim()
                if ($newDoc.Snippet.Length -gt 80) { $newDoc.Snippet = $newDoc.Snippet.Substring(0,77) + "..." }
                $newDoc.FirstLine = $match.LineNumber
                $filtered += $newDoc
            }
        }
        if ($filtered.Count -eq 0) {
            Write-Host "  no match for '$fkw' in current results" -ForegroundColor DarkYellow
        } else {
            $activeResults = @($filtered)
            Write-Host "  filter '$fkw'  =>  $($filtered.Count) files" -ForegroundColor Cyan
            Redisplay-Results $activeResults $totalHits $Keyword
        }
        Write-Host "  [1-$($activeResults.Count)] open  |  o1-o$($activeResults.Count) outline  |  f filter  |  r redisplay  |  q quit" -ForegroundColor Cyan
        Write-Host ""
        continue
    }
    
    # outline command: o1, o2, ...
    if ($input -match '^o(\d+)$') {
        $num = [int]$Matches[1]
        if ($num -ge 1 -and $num -le $activeResults.Count) {
            Show-Outline $activeResults[$num - 1] $num
        } else {
            Write-Host "  invalid: 1..$($activeResults.Count) only" -ForegroundColor DarkYellow
        }
        continue
    }
    
    # open command: 1, 2, ...
    if ($input -match '^\d+$') {
        $num = [int]$input
        if ($num -ge 1 -and $num -le $activeResults.Count) {
            $f = $activeResults[$num - 1]
            $target = "docs\$($f.Path)"
            Write-Host "  opening $target ..." -ForegroundColor DarkGray
            if ($f.Path -match '\.html$') {
                Start-Process $target
            } else {
                & $Typora $target
            }
        } else {
            Write-Host "  invalid: 1..$($activeResults.Count) only" -ForegroundColor DarkYellow
        }
        continue
    }
    
    Write-Host "  type: number=open  oN=outline  f WORD=filter  r=redisplay  q=quit" -ForegroundColor DarkGray
}
