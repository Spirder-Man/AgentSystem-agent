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
    [string]$ReportDir = "",        # 远程报告子目录名，留空=自动发现最新
    [string]$ProjectRoot = "",      # 项目根目录，留空=自动推断
    [switch]$OpenAfterDownload = $true,
    [switch]$SendEmail = $true,      # 下载完成后发送邮件通知
    [switch]$Scheduled = $false      # 定时任务模式：不弹窗，强制发邮件
)

$ErrorActionPreference = "Continue"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# -- 定时任务模式 --
if ($Scheduled) {
    $OpenAfterDownload = $false     # 无人值守不弹窗
    $SendEmail = $true              # 必须发邮件通知
    $ReportDir = ""                 # 强制自动发现最新
}

# -- 路径推断 --
if (-not $ProjectRoot) {
    $ProjectRoot = Resolve-Path "$PSScriptRoot\.."
}
$SshExe = "$ProjectRoot\ssh-runner\bin\Release\net8.0\SshRunner.exe"
$LocalLogDir = "$ProjectRoot\eval_reports\analysis"

# -- SSH 配置 --
$Hostname = "connect.nmb2.seetacloud.com"
$Port = "37103"
$User = "root"
$Password = "2732232706@QQ.com"
$RemoteEvalDir = "/root/autodl-tmp/agent-system/eval_reports"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Download Post-deploy Analysis Report" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# -- 确定报告目录 --
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

# -- 创建本地目录 --
New-Item -ItemType Directory -Path $LocalDir -Force | Out-Null
New-Item -ItemType Directory -Path "${LocalDir}\log_slices" -Force | Out-Null

# -- 下载 analysis.md --
Write-Host ""
Write-Host ">>> [1/4] Downloading analysis.md..." -ForegroundColor Yellow
$analysisB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteDir}/analysis.md 2>/dev/null"
if ($analysisB64 -and $analysisB64.Trim() -ne "") {
    $bytes = [Convert]::FromBase64String($analysisB64.Trim())
    [System.IO.File]::WriteAllBytes("${LocalDir}\analysis.md", $bytes)
    $msg = "  [OK] " + $LocalDir + "\analysis.md (" + $bytes.Length + " bytes)"
    Write-Host $msg -ForegroundColor Green
} else {
    Write-Host "  [WARN] analysis.md not found on remote, run post-deploy-analyze.sh first" -ForegroundColor Yellow
}

# -- 下载 summary.json --
Write-Host ">>> [2/4] Downloading summary.json..." -ForegroundColor Yellow
$summaryB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteDir}/summary.json 2>/dev/null"
if ($summaryB64 -and $summaryB64.Trim() -ne "") {
    $bytes = [Convert]::FromBase64String($summaryB64.Trim())
    [System.IO.File]::WriteAllBytes("${LocalDir}\summary.json", $bytes)
    $msg = "  [OK] " + $LocalDir + "\summary.json (" + $bytes.Length + " bytes)"
    Write-Host $msg -ForegroundColor Green
}

# -- 下载日志切片 --
Write-Host ">>> [3/4] Downloading log slices..." -ForegroundColor Yellow
$logFiles = @("api-eval-window.log", "llama-llm-tail.log", "llama-embed-tail.log")
foreach ($logFile in $logFiles) {
    $logB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteDir}/log_slices/${logFile} 2>/dev/null"
    if ($logB64 -and $logB64.Trim() -ne "") {
        $bytes = [Convert]::FromBase64String($logB64.Trim())
        [System.IO.File]::WriteAllBytes("${LocalDir}\log_slices\${logFile}", $bytes)
        $msg = "  [OK] " + $logFile + " (" + $bytes.Length + " bytes)"
        Write-Host $msg -ForegroundColor Green
    } else {
        $msg = "  [SKIP] " + $logFile + " (not found or empty)"
        Write-Host $msg -ForegroundColor Gray
    }
}

# -- 下载原始日志文件 --
Write-Host ">>> [4/4] Downloading original logs from /root/autodl-tmp/logs/..." -ForegroundColor Yellow
$RemoteLogDir = "/root/autodl-tmp/logs"
$origLogs = @("agent1-api.log", "llama-server.log", "llama-embed.log")
foreach ($origLog in $origLogs) {
    $logB64 = & $SshExe $Hostname $Port $User $Password "base64 -w0 ${RemoteLogDir}/${origLog} 2>/dev/null"
    if ($logB64 -and $logB64.Trim() -ne "") {
        $bytes = [Convert]::FromBase64String($logB64.Trim())
        [System.IO.File]::WriteAllBytes("${LocalDir}\${origLog}", $bytes)
        $msg = "  [OK] " + $origLog + " (" + $bytes.Length + " bytes)"
        Write-Host $msg -ForegroundColor Green
    } else {
        $msg = "  [SKIP] " + $origLog + " (not found or empty)"
        Write-Host $msg -ForegroundColor Gray
    }
}

# -- 完整性验证 --
Write-Host ""
Write-Host ">>> Verifying integrity..." -ForegroundColor Yellow
Get-ChildItem -Recurse $LocalDir -File | ForEach-Object {
    $text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($_.FullName))
    $lines = ($text -split "`n").Count
    if ($_.Extension -eq ".md" -and $lines -le 5) {
        $msg = "  [CORRUPT] " + $_.Name + " (" + $lines + " lines)"
        Write-Host $msg -ForegroundColor Red
    } elseif ($lines -le 1) {
        $msg = "  [CORRUPT] " + $_.Name + " (" + $lines + " lines)"
        Write-Host $msg -ForegroundColor Red
    } else {
        $msg = "  [OK] " + $_.Name + " (" + $lines + " lines)"
        Write-Host $msg -ForegroundColor Green
    }
}

# -- 打开报告 --
if ($OpenAfterDownload -and (Test-Path "${LocalDir}\analysis.md")) {
    Write-Host ""
    Write-Host ">>> Opening analysis report..." -ForegroundColor Yellow
    Start-Process "${LocalDir}\analysis.md"
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Download complete" -ForegroundColor Cyan
Write-Host "  Report: ${LocalDir}\analysis.md" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# ============================================
# 邮件通知：日志已就绪，请人工介入分析
# ============================================
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
                $mail.SubjectEncoding = [System.Text.Encoding]::UTF8
                $mail.Subject = "[Agent1] Daily Eval Report Ready -- ${ReportDir}"
                $templateFile = "$PSScriptRoot\email-body-template.txt"
                $bodyContent = [System.IO.File]::ReadAllText($templateFile, [System.Text.Encoding]::UTF8)
                $bodyContent = $bodyContent.Replace('{{ReportDir}}', $ReportDir).Replace('{{LocalDir}}', $LocalDir)
                $bodyFile = [System.IO.Path]::GetTempFileName()
                [System.IO.File]::WriteAllText($bodyFile, $bodyContent, [System.Text.Encoding]::UTF8)
                $bodyBytes = [System.IO.File]::ReadAllBytes($bodyFile)
                $bodyStream = New-Object System.IO.MemoryStream(,$bodyBytes)
                $altView = New-Object System.Net.Mail.AlternateView($bodyStream, "text/plain; charset=utf-8")
                $altView.TransferEncoding = [System.Net.Mime.TransferEncoding]::Base64
                $mail.AlternateViews.Add($altView)
                # 添加附件：报告 + 日志
                $attachFiles = @(
                    "${LocalDir}\analysis.md",
                    "${LocalDir}\summary.json",
                    "${LocalDir}\log_slices\llama-llm-tail.log",
                    "${LocalDir}\log_slices\llama-embed-tail.log",
                    "${LocalDir}\agent1-api.log",
                    "${LocalDir}\llama-server.log",
                    "${LocalDir}\llama-embed.log"
                )
                foreach ($attachFile in $attachFiles) {
                    if (Test-Path $attachFile) {
                        $attachment = New-Object System.Net.Mail.Attachment($attachFile)
                        $mail.Attachments.Add($attachment)
                    }
                }
                $smtp.Send($mail)
                Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue
                $msg = "  [OK] Email sent to: " + $recipient
                Write-Host $msg -ForegroundColor Green
            } catch {
                Write-Host "  [WARN] Email send failed: $_" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  [SKIP] SMTP config incomplete in .env, skip email" -ForegroundColor Gray
        }
    } else {
        Write-Host "  [SKIP] .env not found, skip email" -ForegroundColor Gray
    }
}
