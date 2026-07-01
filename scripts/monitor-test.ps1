# ═══════════════════════════════════════════════════════════
# monitor-test.ps1 — 远程测试实时流式监控看板
#
# 设计原则: 直接跟踪测试执行的日志输出，不再依赖 status.json 的
# 滞后计数器。每次轮询一次 SSH，读取新增日志行并实时回显。
#
# 用法:
#   .\scripts\monitor-test.ps1 -LogPath "/root/autodl-tmp/test-nohup2.log"
#   .\scripts\monitor-test.ps1 -LogPath "..." -SshPassword "..." -Interval 3
# ═══════════════════════════════════════════════════════════
param(
    [Parameter(Mandatory=$true)]
    [string]$LogPath,

    [int]$Interval = 3,

    [string]$SshExe,
    [string]$SshHost = "connect.nmb2.seetacloud.com",
    [int]$SshPort = 37103,
    [string]$SshUser = "root",
    [string]$SshPassword = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 自动检测 SshRunner 路径
if (-not $SshExe) {
    $candidates = @(
        "$PSScriptRoot\..\ssh-runner\bin\Release\net8.0\SshRunner.exe",
        "$PSScriptRoot\..\ssh-runner\bin\Debug\net8.0\SshRunner.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $SshExe = (Resolve-Path $c).Path; break }
    }
    if (-not $SshExe) {
        Write-Host "ERROR: SshRunner.exe not found. Use -SshExe parameter." -ForegroundColor Red
        exit 1
    }
}

if ([string]::IsNullOrEmpty($SshPassword)) {
    $secure = Read-Host -Prompt "SSH Password" -AsSecureString
    $SshPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}

Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Agent1 Remote Test Stream Monitor              ║" -ForegroundColor Cyan
Write-Host "║   Log: $LogPath" -ForegroundColor Cyan
Write-Host "║   Poll: ${Interval}s | Ctrl+C to stop            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$lastLine = 0
$startTime = Get-Date
$maxRetries = 2
$passCount = 0
$failCount = 0
$skipCount = 0
$currentLayer = ""

while ($true) {
    $output = $null

    for ($retry = 0; $retry -lt $maxRetries; $retry++) {
        try {
            $nextLine = $lastLine + 1
            $remoteCmd = "wc -l '$LogPath' 2>/dev/null; echo '---'; tail -n +$nextLine '$LogPath' 2>/dev/null"
            $output = & $SshExe $SshHost $SshPort $SshUser $SshPassword $remoteCmd 2>$null
            if ($LASTEXITCODE -eq 0 -and $output) { break }
        } catch {}
        if ($retry -lt $maxRetries - 1) { Start-Sleep -Seconds 2 }
    }

    if (-not $output) {
        $elapsed = [math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)
        Write-Host "[$(Get-Date -Format HH:mm:ss)] Waiting... (${elapsed}min)" -ForegroundColor DarkGray
        Start-Sleep -Seconds $Interval
        continue
    }

    # 解析: wc -l 行数 + --- 分隔符 + 新增日志内容
    $contentLines = $output -split "`n"
    $wcLine = $contentLines[0]  # e.g. "42 /path/to/log"
    $newLineCount = 0
    if ($wcLine -match '^(\d+)') { $newLineCount = [int]$Matches[1] }

    # 跳过 wc 行和分隔符行
    $newContent = @()
    $skipHeader = $true
    foreach ($l in $contentLines) {
        if ($skipHeader) {
            if ($l -eq '---' -or $l -match '^\d+\s') { continue }
            $skipHeader = $false
        }
        $newContent += $l
    }
    foreach ($line in $newContent) {
        $trimmed = $line.Trim()
        if (-not $trimmed) { continue }

        # 高亮通过/失败
        if ($trimmed -match '✅ 通过|PASS') {
            Write-Host "  $trimmed" -ForegroundColor Green
            $passCount++
        }
        elseif ($trimmed -match '❌ 失败|FAIL|评测异常|生成错误') {
            Write-Host "  $trimmed" -ForegroundColor Red
            $failCount++
        }
        elseif ($trimmed -match '⚠️|跳过|SKIP') {
            Write-Host "  $trimmed" -ForegroundColor Yellow
            $skipCount++
        }
        # 分层标题
        elseif ($trimmed -match '^── .+ ──$') {
            Write-Host ""
            Write-Host "  $trimmed" -ForegroundColor Magenta
            $currentLayer = $trimmed
        }
        # 进度行
        elseif ($trimmed -match '已完成.*条用例') {
            Write-Host "  $trimmed" -ForegroundColor Cyan
        }
        # 汇总报告
        elseif ($trimmed -match '════|测试汇总报告|总耗时|详细日志') {
            Write-Host ""
            Write-Host "  $trimmed" -ForegroundColor Cyan
        }
        # 普通日志
        else {
            Write-Host "  $trimmed" -ForegroundColor Gray
        }
    }

    $lastLine = $newLineCount

    # 检测完成 (汇总报告已出现)
    $combined = $newContent -join "`n"
    if ($combined -match '测试汇总报告' -or $combined -match '总耗时') {
        Write-Host ""
        Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Green
        Write-Host "  TEST COMPLETE!  PASS: $passCount  FAIL: $failCount  SKIP: $skipCount" -ForegroundColor Green
        Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Green
        break
    }

    Start-Sleep -Seconds $Interval
}
