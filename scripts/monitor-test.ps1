# ═══════════════════════════════════════════════════════════
# monitor-test.ps1 — 远程测试实时监控看板
#
# 设计原则: 每次轮询只需一次 SSH 连接，读取一个小 JSON 文件
# 不再多次调用 ps/ls/wc/tail，彻底消除 SSH 超时问题
#
# 用法:
#   .\scripts\monitor-test.ps1 -StatusPath "/root/autodl-tmp/test-results/20260701_095938/status.json"
#   .\scripts\monitor-test.ps1 -StatusPath "..." -Interval 10  # 10秒轮询
# ═══════════════════════════════════════════════════════════
param(
    [Parameter(Mandatory=$true)]
    [string]$StatusPath,               # 远程 status.json 路径
    
    [int]$Interval = 10,               # 轮询间隔(秒)
    
    [string]$SshExe = "$PSScriptRoot\..\ssh-runner\bin\Release\net8.0\SshRunner.exe",
    [string]$SshHost = "connect.nmb2.seetacloud.com",
    [int]$SshPort = 37103,
    [string]$SshUser = "root",
    [string]$SshPassword = ""
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ([string]::IsNullOrEmpty($SshPassword)) {
    $SshPassword = Read-Host -Prompt "SSH 密码" -AsSecureString
    $SshPassword = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SshPassword))
}

$prevLineCount = 0
$startTime = Get-Date

Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║   Agent1 远程测试实时监控看板                      ║" -ForegroundColor Cyan
Write-Host "║   状态文件: $StatusPath" -ForegroundColor Cyan
Write-Host "║   轮询间隔: ${Interval}s | Ctrl+C 退出           ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$maxRetries = 2

while ($true) {
    $jsonRaw = $null
    
    # 单次 SSH 调用获取状态（带重试）
    for ($retry = 0; $retry -lt $maxRetries; $retry++) {
        try {
            $jsonRaw = & $SshExe $SshHost $SshPort $SshUser $SshPassword "cat $StatusPath 2>/dev/null; cat ${StatusPath}.lines 2>/dev/null" 2>$null
            if ($LASTEXITCODE -eq 0 -and $jsonRaw) { break }
        } catch {}
        if ($retry -lt $maxRetries - 1) { Start-Sleep -Seconds 2 }
    }
    
    if (-not $jsonRaw) {
        Write-Host "[$(Get-Date -Format HH:mm:ss)] ⚠️ 无法获取状态，等待重试..." -ForegroundColor Yellow
        Start-Sleep -Seconds $Interval
        continue
    }
    
    # 尝试 JSON 解析
    $status = $null
    try {
        # 分离 JSON 和 .lines 内容
        $jsonPart, $linesPart = $jsonRaw -split "`n", 2 | Where-Object { $_ -match '^\s*\{' }
        if (-not $jsonPart) { $jsonPart = ($jsonRaw -split "`n" | Select-Object -First 1) }
        
        $status = $jsonPart | ConvertFrom-Json -ErrorAction Stop
    } catch {
        # JSON 解析失败，尝试解析 .lines 格式
    }
    
    # 清屏刷新
    Clear-Host
    $elapsed = [math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)
    Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║   Agent1 远程测试实时监控 | 已监控 ${elapsed}min       ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    
    if ($status) {
        # ── JSON 模式 ──
        $totalElapsed = [math]::Round($status.elapsed_s / 60, 1)
        
        # 进度条
        $pct = if ($status.total -gt 0) { [math]::Round($status.passed / [Math]::Max($status.total, 1) * 100) } else { 0 }
        $barLen = 40
        $filled = [math]::Round($pct / 100 * $barLen)
        $bar = "█" * $filled + "░" * ($barLen - $filled)
        
        Write-Host ""
        Write-Host "  状态: " -NoNewline
        if ($status.status -eq "completed") {
            Write-Host "✅ 已完成" -ForegroundColor Green
        } elseif ($status.status -eq "running") {
            Write-Host "🔄 运行中" -ForegroundColor Yellow
        }
        
        Write-Host "  进度: [$bar] ${pct}%" -ForegroundColor Cyan
        Write-Host "  耗时: ${totalElapsed}min | 已完成: $($status.total) 项" -ForegroundColor White
        Write-Host "  ✅ 通过: $($status.passed)  ❌ 失败: $($status.failed)  ⚠️ 跳过: $($status.skipped)" -ForegroundColor White
        
        # 当前测试
        if ($status.current_test -and $status.current_test.id) {
            Write-Host ""
            Write-Host "  🔄 当前: [$($status.current_test.id)] $($status.current_test.name)" -ForegroundColor Yellow
        }
        
        # 最近 8 项结果
        if ($status.results -and $status.results.Count -gt 0) {
            Write-Host ""
            Write-Host "  ── 最近测试结果 ──" -ForegroundColor DarkGray
            $recent = $status.results | Select-Object -Last 8
            foreach ($r in $recent) {
                $icon = switch ($r.result) {
                    "pass"   { "✅" }
                    "fail"   { "❌" }
                    "skip"   { "⚠️" }
                    default  { "  " }
                }
                $color = switch ($r.result) {
                    "pass" { "Green" }
                    "fail" { "Red" }
                    default { "Yellow" }
                }
                $name = ($r.name.PadRight(30).Substring(0, 30))
                Write-Host "    $icon [$($r.id.PadRight(5))] $name  $($r.elapsed_s)s" -ForegroundColor $color
            }
        }
    } else {
        # ── 降级 .lines 模式 ──
        Write-Host ""
        Write-Host "  ⚠️ JSON 不可用，显示原始行:" -ForegroundColor Yellow
        $lines = $jsonRaw -split "`n" | Where-Object { $_ -match '\|' }
        $lines | Select-Object -Last 15 | ForEach-Object { Write-Host "    $_" }
    }
    
    # 如果完成，退出
    if ($status -and $status.status -eq "completed") {
        Write-Host ""
        Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Green
        Write-Host "  测试全部完成！✅ 通过: $($status.passed)  ❌ 失败: $($status.failed)" -ForegroundColor Green
        Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Green
        break
    }
    
    Start-Sleep -Seconds $Interval
}
