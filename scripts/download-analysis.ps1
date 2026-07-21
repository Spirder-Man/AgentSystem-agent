# ============================================================
# Download & Open Post-deploy Analysis Report
#
# 用法（本地 PowerShell）：
#   .\scripts\download-analysis.ps1
#   .\scripts\download-analysis.ps1 -ReportDir "20260721_1450"
#
# 前置条件：
#   - SshRunner.exe 已编译
#   - 远程 post-deploy-analyze.sh 已执行
# ============================================================

param(
    [string]$ReportDir = "",        # 远程报告子目录名，留空=下载最新
    [string]$ProjectRoot = "",      # 项目根目录，留空=自动推断
    [switch]$OpenAfterDownload = $true,
    [switch]$SendEmail = $true       # 下载完成后发送邮件通知
)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# ── 路径推断 ──
if (-not $ProjectRoot) {
    $ProjectRoot = Resolve-Path "$PSScriptRoot\.."
}
$SshExe = "$ProjectRoot\ssh-runner\bin\Release\net8.0\SshRunner.exe"
$LocalLogDir = "$ProjectRoot\eval_reports\analysis"

# ── SSH 配置（需每次确认） ──
$Hostname = "connect.nmb2.seetacloud.com"
$Port = "37103"
$User = "root"
$Password = "2732232706@QQ.com"
$RemoteEvalDir = "/root/autodl-tmp/agent-system/eval_reports"

Write-Host "╔══════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Download Post-deploy Analysis Report    ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

# ── 确定报告目录 ──
if (-not $ReportDir) {
    Write-Host ">>> Discovering latest report..." -ForegroundColor Yellow
    $lsOutput = & $SshExe $Hostname $Port $User $Password "ls -dt ${RemoteEvalDir}/20* 2>/dev/null | head -1"
    if (-not $lsOutput -or $lsOutput -match "SSH_ERROR|No such file") {
        Write-Host "[FATAL] No eval reports found on remote. Run post-deploy-analyze.sh first." -ForegroundColor Red
        exit 1
    }
    $ReportDir = ($lsOutput -split '/')[-1].Trim()
    Write-Host "  Latest: $ReportDir" -ForegroundColor Green
}

$RemoteDir = "${RemoteEvalDir}/${ReportDir}"
$LocalDir = "${LocalLogDir}\${ReportDir}"

# ── 创建本地目录 ──
New-Item -ItemType Directory -Path $LocalDir -Force | Out-Null
New-Item -ItemType Directory -Path "${LocalDir}\log_slices" -Force | Out-Null

# ── 下载 analysis.md ──
Write-Host ""
Write-Host ">>> [1/3] Downloading analysis.md..." -ForegroundColor Yellow
$analysisB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteDir}/analysis.md 2>/dev/null"
if ($analysisB64 -and $analysisB64.Trim() -ne "") {
    $bytes = [Convert]::FromBase64String($analysisB64.Trim())
    [System.IO.File]::WriteAllBytes("${LocalDir}\analysis.md", $bytes)
    Write-Host "  ✅ ${LocalDir}\analysis.md ($($bytes.Length) bytes)" -ForegroundColor Green
} else {
    Write-Host "  ⚠️ analysis.md not found on remote — run post-deploy-analyze.sh first" -ForegroundColor Yellow
}

# ── 下载 summary.json ──
Write-Host ">>> [2/3] Downloading summary.json..." -ForegroundColor Yellow
$summaryB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteDir}/summary.json 2>/dev/null"
if ($summaryB64 -and $summaryB64.Trim() -ne "") {
    $bytes = [Convert]::FromBase64String($summaryB64.Trim())
    [System.IO.File]::WriteAllBytes("${LocalDir}\summary.json", $bytes)
    Write-Host "  ✅ ${LocalDir}\summary.json ($($bytes.Length) bytes)" -ForegroundColor Green
}

# ── 下载日志切片 ──
Write-Host ">>> [3/3] Downloading log slices..." -ForegroundColor Yellow
$logFiles = @("api-eval-window.log", "llama-llm-tail.log", "llama-embed-tail.log")
foreach ($logFile in $logFiles) {
    $logB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteDir}/log_slices/${logFile} 2>/dev/null"
    if ($logB64 -and $logB64.Trim() -ne "") {
        $bytes = [Convert]::FromBase64String($logB64.Trim())
        [System.IO.File]::WriteAllBytes("${LocalDir}\log_slices\${logFile}", $bytes)
        Write-Host "  ✅ ${logFile} ($($bytes.Length) bytes)" -ForegroundColor Green
    } else {
        Write-Host "  ⏭️ ${logFile} (not found or empty)" -ForegroundColor Gray
    }
}

# ── 完整性验证 ──
Write-Host ""
Write-Host ">>> Verifying integrity..." -ForegroundColor Yellow
Get-ChildItem -Recurse $LocalDir -File | ForEach-Object {
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($_.FullName))
    $lines = ($text -split "`n").Count
    if ($_.Extension -eq ".md" -and $lines -le 5) {
        Write-Host "  ❌ CORRUPT: $($_.Name) ($lines lines)" -ForegroundColor Red
    } elseif ($lines -le 1) {
        Write-Host "  ❌ CORRUPT: $($_.Name) ($lines lines)" -ForegroundColor Red
    } else {
        Write-Host "  ✅ $($_.Name) ($lines lines)" -ForegroundColor Green
    }
}

# ── 打开报告 ──
if ($OpenAfterDownload -and (Test-Path "${LocalDir}\analysis.md")) {
    Write-Host ""
    Write-Host ">>> Opening analysis report..." -ForegroundColor Yellow
    Start-Process "${LocalDir}\analysis.md"
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Download complete                        ║" -ForegroundColor Cyan
Write-Host "║  Report: ${LocalDir}\analysis.md          ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan

# ═══════════════════════════════════════
# 邮件通知：日志已就绪，请人工介入分析
# ═══════════════════════════════════════
if ($SendEmail) {
    Write-Host ""
    Write-Host ">>> Sending email notification..." -ForegroundColor Yellow
    
    # 读取 .env 中的 SMTP 配置
    $envFile = "$ProjectRoot\.env"
    if (Test-Path $envFile) {
        $envContent = Get-Content $envFile -Raw
        $smtpHost = if ($envContent -match 'ALERT_SMTP_HOST=(.+)') { $Matches[1].Trim() } else { "" }
        $smtpUser = if ($envContent -match 'ALERT_EMAIL_USER=(.+)') { $Matches[1].Trim() } else { "" }
        $smtpPass = if ($envContent -match 'ALERT_EMAIL_PASSWORD=([^\r\n]+)') { $Matches[1].Trim() } else { "" }
        $recipient = if ($envContent -match 'ALERT_RECIPIENT_EMAILS=([^\r\n]+)') { $Matches[1].Trim() } else { "" }
        
        if ($smtpHost -and $smtpUser -and $smtpPass -and $recipient) {
            try {
                $smtpParts = $smtpHost -split ':'
                $smtpServer = $smtpParts[0]
                $smtpPort = if ($smtpParts.Length -gt 1) { [int]$smtpParts[1] } else { 587 }
                
                $smtp = New-Object Net.Mail.SmtpClient($smtpServer, $smtpPort)
                $smtp.EnableSsl = $true
                $smtp.Credentials = New-Object System.Net.NetworkCredential($smtpUser, $smtpPass)
                
                $mail = New-Object Net.Mail.MailMessage
                $mail.From = $smtpUser
                $recipient -split ',' | ForEach-Object { $mail.To.Add($_.Trim()) }
                $mail.Subject = "[Agent1] 每日评测报告已就绪 — ${ReportDir}"
                $mail.Body = @"
═══════════════════════════════════
  Agent1 化工合规 AI — 每日远程评测
═══════════════════════════════════

评测日期：${ReportDir}
本地路径：${LocalDir}

已下载文件：
  ├── analysis.md       — D1-D6 六维度分析报告（数据已填充）
  ├── summary.json      — 原始评测指标
  └── log_slices/       — API/llama.cpp 日志切片

═══════════════════════════════════
  ⚠️ 请进行人工介入分析
═══════════════════════════════════

打开 analysis.md，使用 Qoder 加载 remote-log-analysis 技能进行深度分析：
  > 帮我深度分析 eval_reports\analysis\${ReportDir}\analysis.md

— Agent1 自动质量监控系统
"@
                $smtp.Send($mail)
                Write-Host "  ✅ Email sent to: ${recipient}" -ForegroundColor Green
            } catch {
                Write-Host "  ⚠️ Email send failed: $_" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  ⏭️ SMTP config incomplete in .env, skip email" -ForegroundColor Gray
        }
    } else {
        Write-Host "  ⏭️ .env not found, skip email" -ForegroundColor Gray
    }
}
