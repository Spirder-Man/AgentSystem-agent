# Download all .log files from remote server to local (base64 中转, 零编码损失)
param(
    [Parameter(Mandatory=$true)]
    [string]$RemoteDir,
    [Parameter(Mandatory=$true)]
    [string]$LocalDir,
    [Parameter(Mandatory=$true)]
    [string]$Hostname,
    [Parameter(Mandatory=$true)]
    [int]$Port,
    [Parameter(Mandatory=$true)]
    [string]$Password
)

$sshRunner = "$PSScriptRoot\..\ssh-runner\bin\Release\net8.0\SshRunner.exe"

if (-not (Test-Path $sshRunner)) {
    Write-Host "Building SshRunner..." -ForegroundColor Yellow
    dotnet build "$PSScriptRoot\..\ssh-runner\SshRunner.csproj" -c Release -q
}

if (-not (Test-Path $LocalDir)) {
    New-Item -ItemType Directory -Path $LocalDir -Force | Out-Null
}

# Get file list
Write-Host "Getting file list from server..." -ForegroundColor Cyan
$fileList = & $sshRunner $Hostname $Port root $Password "ls $RemoteDir/*.log $RemoteDir/summary.txt 2>/dev/null"
$files = $fileList -split "`n" | Where-Object { $_ -match '\.(log|txt)$' } | ForEach-Object { ($_ -split '/')[-1].Trim() }

Write-Host "Found $($files.Count) files to download" -ForegroundColor Green

$total = $files.Count
$current = 0

foreach ($fileName in $files) {
    $current++
    $localPath = Join-Path $LocalDir $fileName
    
    Write-Host "[$current/$total] Downloading $fileName ... " -NoNewline
    try {
        # ✅ 核心：base64 编码传输，保持二进制完整性
        $remotePath = "$RemoteDir/$fileName"
        $b64 = & $sshRunner $Hostname $Port root $Password "base64 -w0 `"$remotePath`""
        
        if ($b64 -and $b64.Trim().Length -gt 10) {
            $bytes = [Convert]::FromBase64String($b64.Trim())
            [System.IO.File]::WriteAllBytes($localPath, $bytes)
            $lines = ([System.Text.Encoding]::UTF8.GetString($bytes) -split "`n").Count
            Write-Host "OK ($($bytes.Length)b, $lines lines)" -ForegroundColor Green
        } else {
            Write-Host "EMPTY" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "FAIL: $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Done! Files saved to: $LocalDir" -ForegroundColor Cyan

# Integrity check
$logFiles = Get-ChildItem "$LocalDir\*.log" | Where-Object { $_.Name -notlike "*_head*" }
Write-Host "`n=== INTEGRITY CHECK ===" -ForegroundColor Cyan
Write-Host "  .log files: $($logFiles.Count)"
Write-Host "  summary.txt: $(Test-Path '$LocalDir\summary.txt')"

$badFiles = @()
$logFiles | ForEach-Object {
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($_.FullName))
    $lines = ($text -split "`n").Count
    if ($lines -le 1) { $badFiles += $_.Name }
}
if ($badFiles.Count -gt 0) {
    Write-Host "  ❌ 换行符损坏 ($($badFiles.Count) 个): $badFiles" -ForegroundColor Red
} else {
    Write-Host "  ✅ 换行符正常" -ForegroundColor Green
}
