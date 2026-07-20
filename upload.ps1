# ==========================================
# Agent1 容器化部署 — 一键 SCP 上传脚本
# 将 Docker 构建所需文件批量上传至 a800-prod
# ==========================================
# 使用方式:
#   .\upload.ps1                          # 上传到默认路径
#   .\upload.ps1 -DryRun                  # 预览（不实际传输）
#   .\upload.ps1 -Server a800-prod -RemotePath /root/agent-deploy/agent-system
# ==========================================

param(
    [string]$Server   = "a800-prod",                              # SSH 别名
    [string]$RemotePath = "/root/agent-deploy/agent-system",      # 服务器目标目录
    [switch]$DryRun                                               # 仅列出文件，不上传
)

$ErrorActionPreference = "Stop"
$ScriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = (Resolve-Path "$ScriptDir").Path

Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Agent1 容器化部署 — SCP 批量上传   ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "  服务器  : $Server"        -ForegroundColor DarkCyan
Write-Host "  远程路径: $RemotePath"    -ForegroundColor DarkCyan
Write-Host "  本地路径: $ProjectDir"    -ForegroundColor DarkCyan
Write-Host ""

# ═══════════════════════════════════════
# 1. 前置检查
# ═══════════════════════════════════════

Write-Host "[1/5] 前置检查..." -ForegroundColor Yellow

# 1.1 检查 scp 可用
$null = Get-Command scp -ErrorAction Stop
Write-Host "  scp 可用" -ForegroundColor Green

# 1.2 测试 SSH 连通性
Write-Host "  测试 SSH 连接..." -NoNewline
$ErrorActionPreference = "Continue"
$sshTest = ssh -o ConnectTimeout=5 -o StrictHostKeyChecking=accept-new $Server "echo OK" 2>&1
$sshOk = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = "Stop"
if ($sshOk) {
    Write-Host " 连通" -ForegroundColor Green
} else {
    Write-Host " 失败" -ForegroundColor Red
    if ($sshTest -match "Connection refused|timed out|No route|UNREACHABLE") {
        Write-Host "  原因: $($sshTest.Trim())" -ForegroundColor DarkRed
    }
    Write-Host "  请确认: ① SSH 配置正确  ② 服务器可达  ③ 端口 32024 放行" -ForegroundColor DarkYellow
    Write-Host "  手动测试: ssh $Server" -ForegroundColor DarkYellow
    exit 1
}

# 1.3 创建远程目录
if (-not $DryRun) {
    Write-Host "  创建远程目录..." -NoNewline
    $ErrorActionPreference = "Continue"
    ssh -o StrictHostKeyChecking=accept-new $Server "mkdir -p $RemotePath" 2>&1 | Out-Null
    $mkdirOk = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = "Stop"
    if ($mkdirOk) {
        Write-Host " 就绪" -ForegroundColor Green
    } else {
        Write-Host " 失败" -ForegroundColor Red
        exit 1
    }
}

# ═══════════════════════════════════════
# 2. 检查 .env 文件
# ═══════════════════════════════════════

Write-Host "[2/5] 检查 .env 文件..." -ForegroundColor Yellow

$envPath = Join-Path $ProjectDir ".env"
$envExamplePath = Join-Path $ProjectDir ".env.example"

if (-not (Test-Path $envPath)) {
    Write-Host "  未找到 .env 文件！" -ForegroundColor Red
    if (Test-Path $envExamplePath) {
        Write-Host "  已从 .env.example 自动复制，请立即编辑密码！" -ForegroundColor DarkYellow
        Copy-Item $envExamplePath $envPath
    } else {
        Write-Host "  错误: .env.example 也不存在" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "  .env 已存在" -ForegroundColor Green
}

# 快速检查 .env 中关键变量是否已填写
$envContent = Get-Content $envPath -Raw -Encoding UTF8
$warnings = @()
if ($envContent -match 'DB_PASSWORD=7758521')        { $warnings += "DB_PASSWORD 是默认值" }
if ($envContent -match 'JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx') { $warnings += "JWT_KEY 是默认值" }
if ($envContent -match 'ALERT_EMAIL_PASSWORD=你的QQ邮箱授权码')      { $warnings += "ALERT_EMAIL_PASSWORD 未填写" }
if ($warnings.Count -gt 0) {
    Write-Host "  警告: 以下配置使用了默认值，生产环境请务必修改：" -ForegroundColor DarkYellow
    $warnings | ForEach-Object { Write-Host "    - $_" -ForegroundColor DarkYellow }
}

# ═══════════════════════════════════════
# 3. 构建上传文件清单
# ═══════════════════════════════════════

Write-Host "[3/5] 构建上传清单..." -ForegroundColor Yellow

# 单独文件
$singleFiles = @(
    "docker-compose.yml",
    "Dockerfile",
    "Dockerfile.llama",
    ".dockerignore",
    "nuget.config",
    "init_database.sql",
    ".env"
)

# 目录（递归上传）
$directories = @(
    "Agent1",
    "Agent1.Api",
    "Data",
    "prometheus",
    "grafana"
)

# 验证本地文件存在
$missing = @()
foreach ($f in $singleFiles) {
    if (-not (Test-Path (Join-Path $ProjectDir $f))) {
        $missing += $f
    }
}
foreach ($d in $directories) {
    if (-not (Test-Path (Join-Path $ProjectDir $d))) {
        $missing += "$d/"
    }
}

if ($missing.Count -gt 0) {
    Write-Host "  缺失文件:" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "    - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "  单文件: $($singleFiles.Count) 个" -ForegroundColor Green
Write-Host "  目录  : $($directories.Count) 个" -ForegroundColor Green
Write-Host "  全部就绪" -ForegroundColor Green

# ═══════════════════════════════════════
# 4. 执行上传
# ═══════════════════════════════════════

if ($DryRun) {
    Write-Host ""
    Write-Host "[DRY RUN] 以下文件将被上传到 $RemotePath :" -ForegroundColor Magenta
    Write-Host ""
    Write-Host "  ── 单文件 ──" -ForegroundColor DarkGray
    foreach ($f in $singleFiles) {
        Write-Host "    $f"
    }
    Write-Host "  ── 目录 ──" -ForegroundColor DarkGray
    foreach ($d in $directories) {
        Write-Host "    $d/"
    }
    Write-Host ""
    Write-Host "  ⚠ knowledgebase/ 和 models/ 需要单独处理（见脚本末尾提示）" -ForegroundColor DarkYellow
    Write-Host ""
    Write-Host "  DryRun 完成，未实际传输任何文件。" -ForegroundColor Cyan
    exit 0
}

Write-Host "[4/5] 开始上传..." -ForegroundColor Yellow
Write-Host ""

$totalItems = $singleFiles.Count + $directories.Count
$current = 0
$failed = @()
$startTime = Get-Date

# ── 上传单文件 ──
foreach ($f in $singleFiles) {
    $current++
    $local  = Join-Path $ProjectDir $f
    $remote = "$Server`:$RemotePath/"
    Write-Host "  [$current/$totalItems] $f " -NoNewline -ForegroundColor Gray
    
    $out = scp -q $local $remote 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK" -ForegroundColor Green
    } else {
        Write-Host "FAIL" -ForegroundColor Red
        $failed += $f
        Write-Host "    $out" -ForegroundColor DarkRed
    }
}

# ── 上传目录 ──
foreach ($d in $directories) {
    $current++
    $local  = Join-Path $ProjectDir $d
    $remote = "$Server`:$RemotePath/"
    Write-Host "  [$current/$totalItems] $d/ " -NoNewline -ForegroundColor Gray
    
    $out = scp -r -q $local $remote 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "OK" -ForegroundColor Green
    } else {
        Write-Host "FAIL" -ForegroundColor Red
        $failed += "$d/"
        Write-Host "    $out" -ForegroundColor DarkRed
    }
}

$elapsed = ((Get-Date) - $startTime).TotalSeconds
Write-Host ""
Write-Host "  耗时: $([math]::Round($elapsed, 1)) 秒" -ForegroundColor DarkCyan

# ═══════════════════════════════════════
# 5. 上传结果汇总
# ═══════════════════════════════════════

Write-Host "[5/5] 上传结果汇总..." -ForegroundColor Yellow

if ($failed.Count -eq 0) {
    Write-Host "  全部 $totalItems 项上传成功" -ForegroundColor Green
} else {
    Write-Host "  成功: $($totalItems - $failed.Count) / $totalItems" -ForegroundColor Yellow
    Write-Host "  失败: $failed" -ForegroundColor Red
}

# 远程验证
Write-Host "  远程验证..." -NoNewline
$verify = ssh $Server "ls $RemotePath/docker-compose.yml $RemotePath/Dockerfile $RemotePath/Dockerfile.llama $RemotePath/.env 2>&1"
if ($LASTEXITCODE -eq 0) {
    Write-Host " 关键文件存在" -ForegroundColor Green
} else {
    Write-Host " 验证失败" -ForegroundColor Red
    Write-Host "  $verify" -ForegroundColor DarkRed
}

# ═══════════════════════════════════════
# 6. 后续操作提示
# ═══════════════════════════════════════

Write-Host ""
Write-Host "╔══════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  后续操作                            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

$steps = @(
    @{Step="1"; Desc="SSH 登录服务器";       Cmd="ssh $Server"},
    @{Step="2"; Desc="下载模型文件到 models/"; Cmd="mkdir -p $RemotePath/models"},
    @{Step="";  Desc="  ";                    Cmd="wget -c -O $RemotePath/models/qwen3-8b-q4_k_m.gguf https://hf-mirror.com/bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf"},
    @{Step="";  Desc="  ";                    Cmd="wget -c -O $RemotePath/models/nomic-embed-text-v1.5.Q8_0.gguf https://hf-mirror.com/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.Q8_0.gguf"},
    @{Step="3"; Desc="确认 knowledgebase/ 就位"; Cmd="ls $RemotePath/knowledgebase/"},
    @{Step="4"; Desc="构建镜像 (首次 15~35 分钟)"; Cmd="cd $RemotePath && docker compose build"},
    @{Step="5"; Desc="启动全部服务";           Cmd="cd $RemotePath && docker compose up -d"},
    @{Step="6"; Desc="查看日志";               Cmd="cd $RemotePath && docker compose logs -f"},
    @{Step="7"; Desc="验证健康检查";           Cmd="curl http://localhost:5000/health/live"},
    @{Step="8"; Desc="Swagger 文档";           Cmd="curl http://localhost:5000/swagger"}
)

foreach ($s in $steps) {
    $prefix = if ($s.Step) { "[$($s.Step)]" } else { "    " }
    Write-Host "  $prefix $($s.Desc)" -ForegroundColor White
    Write-Host "       $($s.Cmd)" -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "  注意: Docker 镜像构建需要 GPU 环境，A800 约需 15~35 分钟。" -ForegroundColor DarkYellow
Write-Host ""