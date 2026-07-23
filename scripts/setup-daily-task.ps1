# ============================================================
# Setup Windows Scheduled Task - Agent1 Daily Report Download
#
# 用法（管理员 PowerShell）：
#   .\scripts\setup-daily-task.ps1
#
# 前置条件：
#   AutoDL 网页控制台已设置 2:50 开机 / 4:00 关机
#
# 注册后每天早上 4:00 自动：
#   1. SSH 发现最新远程评测报告
#   2. 下载全部文件到本地
#   3. 发送邮件通知（含附件）
# ============================================================

$TaskName = "Agent1-DailyReport"
$ScriptPath = "$PSScriptRoot\download-analysis.ps1"

# 管理员权限检查
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
if (-not $isAdmin) {
    Write-Host "[FATAL] This script requires Administrator privileges." -ForegroundColor Red
    Write-Host "        Right-click PowerShell -> Run as Administrator, then retry." -ForegroundColor Yellow
    exit 1
}

# 检查是否已存在，有则先删
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "[INFO] Removing existing task: $TaskName" -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
}

# 删除旧的 AutoPowerOn 任务（如果有）
$oldPower = Get-ScheduledTask -TaskName "Agent1-AutoPowerOn" -ErrorAction SilentlyContinue
if ($oldPower) {
    Write-Host "[INFO] Removing obsolete task: Agent1-AutoPowerOn" -ForegroundColor Yellow
    Unregister-ScheduledTask -TaskName "Agent1-AutoPowerOn" -Confirm:$false -ErrorAction SilentlyContinue
}

# 创建任务动作
$Action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ScriptPath`" -Scheduled" `
    -WorkingDirectory "$PSScriptRoot\.."

# 每天 4:00 触发（AutoDL 网页已设 2:50 开机 / 4:00 关机）
$Trigger = New-ScheduledTaskTrigger -Daily -At 4:00AM

$Principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest

$Settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RunOnlyIfNetworkAvailable `
    -MultipleInstances IgnoreNew

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $Action `
    -Trigger $Trigger `
    -Principal $Principal `
    -Settings $Settings `
    -Description "Daily auto-download Agent1 remote eval report and send email notification" `
    -Force

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Task registered: $TaskName" -ForegroundColor Green
Write-Host "  Schedule: Daily 4:00 AM" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Full daily pipeline:" -ForegroundColor Cyan
Write-Host "    2:50 AM  AutoDL web: power-on" -ForegroundColor White
Write-Host "    3:00 AM  Remote cron: post-deploy-eval.sh" -ForegroundColor White
Write-Host "    3:25 AM  Remote cron: post-deploy-analyze.sh" -ForegroundColor White
Write-Host "    4:00 AM  Local: download + email to lcy.050801@qq.com" -ForegroundColor White
Write-Host "    4:00 AM  AutoDL web: power-off" -ForegroundColor White
