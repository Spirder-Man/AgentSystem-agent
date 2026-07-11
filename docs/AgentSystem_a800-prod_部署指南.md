# AgentSystem — a800-prod 服务器部署完整指南

> **目标服务器**：a800-prod (`ssh a800-prod` → 111.172.214.123:32024, root)
> **项目**：AgentSystem v4.0 — 化工园区危化品合规审查 AI Agent
> **技术栈**：.NET 8 + llama.cpp CUDA + PostgreSQL 16 + pgvector
> **编制日期**：2026-07-06

---

## 前置说明

### 硬件资源需求

| 资源 | 最低要求 | 本项目占用（预估） |
|------|---------|------------------|
| GPU | 1× NVIDIA GPU（≥8GB VRAM） | Qwen3-8B Q4_K_M: ~7GB + Embed: ~1GB |
| 内存 | 16 GB | ~8 GB |
| 磁盘 | 30 GB 可用 | 模型 ~5GB + 代码 ~200MB + DB ~1GB |
| CUDA | ≥11.6 | 需支持 sm_80（A800 架构） |

### a800-prod 服务器已知信息（来自历史项目）

- GPU：4× A800 40GB（总计 160GB VRAM）
- 内存：254 GB
- 已部署服务：deepseekServe（占用部分端口和显存）
- **注意**：部署前需确认已有服务不冲突

---

## Task 1: 服务器环境预检

### 1.1 SSH 连接验证

```bash
# 在你的 Windows 终端执行
ssh a800-prod
```

> 连接参数：`Host 111.172.214.123, Port 32024, User root`
> SSH 配置位于 `C:\Users\lcy\.ssh\config`

### 1.2 操作系统与架构检测

```bash
# ===== 在服务器上执行 =====

# 系统基本信息
uname -a
cat /etc/os-release

# 架构确认（应为 x86_64，A800 是 x86 架构）
uname -m
```

### 1.3 GPU / CUDA 环境检查

```bash
# GPU 状态
nvidia-smi

# CUDA 版本
nvcc --version

# 如果 nvcc 未找到，检查：
ls /usr/local/cuda/bin/
/usr/local/cuda/bin/nvcc --version

# GPU 计算能力（A800 = 8.0，对应 sm_80）
nvidia-smi --query-gpu=compute_cap --format=csv
```

**预期输出**：
```
compute_cap
8.0
```

> **关键判断**：A800 的 CUDA 计算能力为 8.0，llama.cpp 编译时需使用 `-DCMAKE_CUDA_ARCHITECTURES="80"`。

### 1.4 磁盘空间检查

```bash
df -h
# 重点关注 / 或 /root 分区可用空间 > 30GB
```

### 1.5 内存检查

```bash
free -h
# 应至少预留 8GB 给本项目
```

### 1.6 端口占用检查

```bash
# 本项目将占用以下端口，确认未被占用：
for port in 8080 8081 5000 5432; do
    echo "=== Port $port ==="
    ss -tlnp | grep ":$port " || echo "  (空闲)"
done
```

### 1.7 网络连通性检查

```bash
# 如果服务器有外网，检查关键站点连通性：
curl -s --connect-timeout 5 https://gitee.com > /dev/null && echo "Gitee: OK" || echo "Gitee: FAIL"
curl -s --connect-timeout 5 https://hf-mirror.com > /dev/null && echo "HF-Mirror: OK" || echo "HF-Mirror: FAIL"
curl -s --connect-timeout 5 https://github.com > /dev/null && echo "GitHub: OK" || echo "GitHub: FAIL"

# 包管理器源
yum repolist 2>/dev/null || dnf repolist 2>/dev/null || apt update 2>/dev/null
```

---

## Task 2: 基础依赖安装

### 2.1 确定操作系统分发版

```bash
source /etc/os-release
echo "OS: $ID $VERSION_ID"
```

根据输出选择对应的安装方式：

---

#### 方案 A：Ubuntu / Debian 系列（使用 APT）

```bash
# === 2.1.1 安装编译工具链 ===
apt update
apt install -y build-essential cmake git wget curl

# === 2.1.2 安装 .NET 8 SDK ===
# 方法1：Microsoft APT 源（推荐）
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update
apt install -y dotnet-sdk-8.0

# 方法2：如果方法1网络不通，使用 dotnet-install 脚本
# curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 8.0 --install-dir /usr/share/dotnet
# ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet

# 验证
dotnet --version   # 应输出 8.0.xxx

# === 2.1.3 安装 PostgreSQL 16 + pgvector ===
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /usr/share/keyrings/postgresql.gpg --yes
echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] http://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" | tee /etc/apt/sources.list.d/pgdg.list
apt update
apt install -y postgresql-16 postgresql-client-16 postgresql-16-pgvector
```

---

#### 方案 B：CentOS / RHEL / AlmaLinux 8/9（使用 DNF/YUM）

```bash
# === 2.1.1 安装编译工具链 ===
dnf groupinstall -y "Development Tools"
dnf install -y cmake git wget curl

# 如果 GCC 版本过低（< 8），安装 GCC 工具集：
# dnf install -y gcc-toolset-10
# source /opt/rh/gcc-toolset-10/enable
# 或永久启用：echo "source /opt/rh/gcc-toolset-10/enable" >> /etc/profile

# === 2.1.2 安装 .NET 8 SDK ===
# Microsoft 官方 RPM 源
rpm -Uvh https://packages.microsoft.com/config/rhel/8/packages-microsoft-prod.rpm
dnf install -y dotnet-sdk-8.0

# 验证
dotnet --version

# === 2.1.3 安装 PostgreSQL 16 + pgvector ===
dnf install -y https://download.postgresql.org/pub/repos/yum/reporpms/EL-8-x86_64/pgdg-redhat-repo-latest.noarch.rpm
dnf -y module disable postgresql   # 禁用系统自带旧版
dnf install -y postgresql16-server postgresql16 postgresql16-contrib

# pgvector 扩展
dnf install -y pgvector_16
# 如果 dnf 找不到 pgvector，手动编译安装（见 Task 10 排障指南）

# 初始化数据库
/usr/pgsql-16/bin/postgresql-16-setup initdb
systemctl enable postgresql-16
systemctl start postgresql-16
```

---

#### 方案 C：Kylin Linux V10（国产系统）

```bash
# Kylin V10 基于 CentOS 8，兼容 RHEL 8 包

# === 编译工具链 ===
dnf groupinstall -y "Development Tools"
dnf install -y cmake git wget curl

# GCC 版本检查（Kylin V10 自带 GCC 7.3，需要 ≥8.0）
gcc --version
# 如果 < 8.0，需要安装 GCC 10+（参考 Task 10 排障指南）

# === .NET 8 SDK ===
# 使用 RHEL 8 源
rpm -Uvh https://packages.microsoft.com/config/rhel/8/packages-microsoft-prod.rpm
dnf install -y dotnet-sdk-8.0
dotnet --version

# === PostgreSQL 16 ===
dnf install -y https://download.postgresql.org/pub/repos/yum/reporpms/EL-8-x86_64/pgdg-redhat-repo-latest.noarch.rpm
dnf -y module disable postgresql
dnf install -y postgresql16-server postgresql16 postgresql16-contrib
/usr/pgsql-16/bin/postgresql-16-setup initdb
systemctl enable postgresql-16
systemctl start postgresql-16
```

---

## Task 3: 项目代码克隆与分支切换

### 3.1 创建工作目录

```bash
mkdir -p /root/agent-deploy
cd /root/agent-deploy
```

### 3.2 克隆仓库

```bash
# 方式1：HTTPS 克隆（推荐，无需配置 SSH Key）
git clone https://gitee.com/liuchao_yue/agent-system.git

# 方式2：SSH 克隆（需先在 Gitee 配置 SSH 公钥）
# git clone git@gitee.com:liuchao_yue/agent-system.git

cd agent-system
```

### 3.3 切换分支

```bash
# 查看所有分支
git branch -a

# 切换到主开发分支（README 明确指向此分支）
git checkout linux原生编译模型llama.cpp
git pull origin linux原生编译模型llama.cpp

# 确认当前分支
git branch
git log --oneline -5
```

### 3.4 验证项目文件完整性

```bash
# 确认关键文件存在
ls -la Agent1/Agent1.csproj
ls -la Agent1.Api/Agent1.Api.csproj
ls -la Agent1.sln
ls -la init_database.sql
ls -la docker-compose.yml
ls -la Agent1/appsettings.json
```

---

## Task 4: llama.cpp CUDA 编译

### 4.1 确认 CUDA 环境

```bash
# CUDA 路径确认
ls /usr/local/cuda/bin/nvcc
/usr/local/cuda/bin/nvcc --version

# 如果 CUDA 路径不是 /usr/local/cuda，查找：
find / -name "nvcc" -type f 2>/dev/null
```

### 4.2 克隆 llama.cpp

```bash
cd /root/agent-deploy

# 优先从国内镜像克隆
git clone https://gitclone.com/github.com/ggerganov/llama.cpp.git

# 如果 gitclone 不通，尝试 GitHub 直连：
# git clone https://github.com/ggerganov/llama.cpp.git
```

### 4.3 CMake 配置（CUDA 版）

```bash
cd /root/agent-deploy/llama.cpp

# A800 架构为 sm_80，对应 CMAKE_CUDA_ARCHITECTURES="80"
# 注意：不是 "80-virtual"，不要加 -real/-virtual 后缀

cmake -B build \
  -DGGML_CUDA=ON \
  -DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc \
  -DCMAKE_CUDA_ARCHITECTURES="80"

# 如果需要指定宿主编译器（GCC 版本问题）：
# 先查看 nvcc 兼容的 GCC 版本范围：
# /usr/local/cuda/bin/nvcc --check-gcc
# 如果系统 GCC 版本过高/过低，可能需要指定：
# -DCMAKE_CUDA_HOST_COMPILER=/usr/bin/gcc
```

### 4.4 编译

```bash
# 多核并行编译（-j$(nproc) 使用全部 CPU 核心）
cmake --build build --config Release -j$(nproc)

# 编译时间：约 5-15 分钟，取决于 CPU 核心数
```

### 4.5 验证编译产物

```bash
# 检查关键二进制文件
ls -lh build/bin/llama-server
ls -lh build/bin/llama-cli

# 验证可执行
build/bin/llama-server --version 2>&1 | head -5

# 确认 CUDA 支持
build/bin/llama-server --version 2>&1 | grep -i cuda
# 预期输出包含 "cuBLAS" 或 "CUDA"
```

> **编译失败常见原因**：
> - `nvcc not found` → 检查 CUDA 安装路径
> - `Unsupported gcc version` → GCC 版本与 CUDA 不兼容，见 Task 10
> - `CMake Error: CMAKE_CUDA_ARCHITECTURES` → A800 必须是 `"80"`

---

## Task 5: GGUF 模型文件下载

### 5.1 创建模型目录

```bash
mkdir -p /root/agent-deploy/models
```

### 5.2 下载 LLM 推理模型（Qwen3-8B Q4_K_M）

```bash
# 约 4.7 GB，下载时间取决于网络带宽

# 方式1：HuggingFace 镜像（推荐）
wget -O /root/agent-deploy/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  "https://hf-mirror.com/bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf"

# 方式2：ModelScope（国内更快）
# pip install modelscope
# modelscope download --model Qwen/Qwen3-8B-GGUF --include "*.gguf" --local_dir /root/agent-deploy/models

# 方式3：如果外网不通，在本地 PC 下载后用 SCP 上传
```

### 5.3 下载嵌入模型（nomic-embed-text-v1.5 F16）

```bash
# 约 274 MB

wget -O /root/agent-deploy/models/nomic-embed-text-v1.5.f16.gguf \
  "https://hf-mirror.com/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf"
```

### 5.4 SCP 上传备选方案（无网络环境）

如果你的服务器完全无法访问外网，在 **Windows 本地**执行：

```powershell
# 1. 本地下载模型（Windows PowerShell）
mkdir D:\models
Invoke-WebRequest -Uri "https://hf-mirror.com/bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf" -OutFile "D:\models\Qwen_Qwen3-8B-Q4_K_M.gguf"
Invoke-WebRequest -Uri "https://hf-mirror.com/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf" -OutFile "D:\models\nomic-embed-text-v1.5.f16.gguf"

# 2. SCP 上传到 a800-prod
scp -P 32024 D:\models\Qwen_Qwen3-8B-Q4_K_M.gguf root@111.172.214.123:/root/agent-deploy/models/
scp -P 32024 D:\models\nomic-embed-text-v1.5.f16.gguf root@111.172.214.123:/root/agent-deploy/models/
```

### 5.5 验证模型文件

```bash
# 确认文件大小正常
ls -lh /root/agent-deploy/models/

# 预期：
# Qwen_Qwen3-8B-Q4_K_M.gguf        ~4.7GB
# nomic-embed-text-v1.5.f16.gguf    ~274MB

# 验证 GGUF 文件完整性（读取文件头）
/root/agent-deploy/llama.cpp/build/bin/llama-cli -m /root/agent-deploy/models/Qwen_Qwen3-8B-Q4_K_M.gguf --version 2>&1 | head -10
```

---

## Task 6: PostgreSQL 数据库初始化

### 6.1 启动 PostgreSQL 服务

```bash
# === Ubuntu/Debian ===
pg_ctlcluster 16 main start
# 或
systemctl start postgresql

# === CentOS/RHEL/Kylin ===
systemctl start postgresql-16
# 或
/usr/pgsql-16/bin/pg_ctl -D /var/lib/pgsql/16/data start
```

### 6.2 确认 PostgreSQL 运行状态

```bash
systemctl status postgresql* --no-pager
ss -tlnp | grep 5432
```

### 6.3 设置密码并创建数据库

```bash
# 设置 postgres 用户密码
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD '7758521';\""

# 创建项目数据库
su - postgres -c "psql -c \"CREATE DATABASE chemical_park_ai_agent;\""

# 启用 pgvector 扩展
su - postgres -c "psql -d chemical_park_ai_agent -c \"CREATE EXTENSION IF NOT EXISTS vector;\""

# 确认扩展已启用
su - postgres -c "psql -d chemical_park_ai_agent -c \"SELECT extname, extversion FROM pg_extension WHERE extname='vector';\""
```

### 6.4 执行项目初始化 SQL

```bash
cd /root/agent-deploy/agent-system

# 检查 SQL 文件
cat init_database.sql | head -20

# 执行初始化
PGPASSWORD=7758521 psql -h localhost -U postgres -d chemical_park_ai_agent -f init_database.sql
```

### 6.5 配置 pg_hba.conf（允许密码登录）

```bash
# 找到 pg_hba.conf 位置
su - postgres -c "psql -c 'SHOW hba_file;'"

# 编辑（路径以实际输出为准）
# vi /etc/postgresql/16/main/pg_hba.conf   # Ubuntu
# vi /var/lib/pgsql/16/data/pg_hba.conf    # CentOS

# 确保包含以下行（或类似）：
# local   all             all                                     md5
# host    all             all             127.0.0.1/32            md5

# 修改后重载配置
systemctl reload postgresql-16   # CentOS
# 或
pg_ctlcluster 16 main reload     # Ubuntu
```

### 6.6 验证数据库连接

```bash
PGPASSWORD=7758521 psql -h localhost -U postgres -d chemical_park_ai_agent -c "SELECT current_database(), current_user, version();"
```

---

## Task 7: 项目配置与编译

### 7.1 查看现有配置

```bash
cd /root/agent-deploy/agent-system
cat Agent1/appsettings.json
```

### 7.2 关键配置项说明

项目的 `Agent1/appsettings.json` 中需要确认以下配置：

| 配置路径 | 说明 | 默认值 | 是否需要修改 |
|---------|------|--------|------------|
| `Llm.Endpoint` | LLM 推理服务地址 | `http://localhost:8080/v1` | 默认正确 |
| `VectorSearch.EmbeddingEndpoint` | 嵌入服务地址 | `http://localhost:8081/v1` | 默认正确 |
| `ConnectionStrings.DefaultConnection` | 数据库连接串 | 含 `localhost:5432` | 确认用户名密码 |
| `GpuEmbedding.Enabled` | GPU 嵌入加速 | `true` | A800 建议开启 |
| `Reranker.Enabled` | Cross-Encoder 精排 | 视需求 | 可选 |

### 7.3 配置数据库连接（appsettings.json）

在 `Agent1/appsettings.json` 中确认/修改以下内容：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=chemical_park_ai_agent;Username=postgres;Password=7758521"
  }
}
```

### 7.4 配置环境变量

```bash
# 设置 JWT 签名密钥（≥32字符）
export JWT_KEY="qazwsxedcrfvtgbyhnujmikolpqazwsx"

# 数据库密码
export DB_PASSWORD="7758521"

# 账号配置（JSON 数组）
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"Admin@123456","Role":"admin"},{"Username":"auditor","Password":"Audit@123456","Role":"auditor"}]'

# 运行环境
export ASPNETCORE_ENVIRONMENT="Production"
export DOTNET_ENVIRONMENT="Production"

# 知识库路径（如需要）
export KNOWLEDGE_BASE_PATH="/root/agent-deploy/agent-system/knowledgebase"

# 如果 GPU 服务已在运行，确认端点：
export LLM_ENDPOINT="http://localhost:8080/v1"
```

> **生产环境强制要求**：`ASPNETCORE_ENVIRONMENT=Production` 时必须配置 `AUTH_ACCOUNTS_JSON`，否则系统拒绝启动。

### 7.5 编译项目

```bash
cd /root/agent-deploy/agent-system

# 还原依赖
dotnet restore Agent1.sln

# 编译（Release 模式）
dotnet build Agent1.sln -c Release

# 预期结果：0 Error(s)
```

### 7.6 可选：运行单元测试

```bash
# 如果安装了测试框架
dotnet test Agent1.sln -c Release --no-build

# 预期：152 通过，0 失败
```

---

## Task 8: 服务启动

### 8.1 启动顺序概览

```
第1步：PostgreSQL           (端口 5432)
第2步：llama-server LLM     (端口 8080，GPU 推理)
第3步：llama-server Embed   (端口 8081，GPU 嵌入)
第4步：Agent1.Api           (端口 5000，Web API)
第5步：Agent1 控制台        (CLI，功能测试)
```

### 8.2 创建日志目录

```bash
mkdir -p /root/agent-deploy/logs
```

### 8.3 启动 llama-server — LLM 推理服务

```bash
# 先确认 GPU 显存可用
nvidia-smi

# 启动 LLM 服务（后台运行）
nohup /root/agent-deploy/llama.cpp/build/bin/llama-server \
  -m /root/agent-deploy/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 \
  --port 8080 \
  -ngl 99 \
  -c 32768 \
  --cache-type-k q8_0 \
  --cache-type-v q8_0 \
  -fa \
  -sps 0.0 \
  > /root/agent-deploy/logs/llama-server-8080.log 2>&1 &

# 记录进程 ID
echo "LLM PID: $!"
```

**参数说明**：

| 参数 | 值 | 说明 |
|------|-----|------|
| `-m` | 模型路径 | Qwen3-8B Q4_K_M GGUF |
| `--port` | 8080 | LLM 推理端口 |
| `-ngl` | 99 | GPU 层数（全部放入显存） |
| `-c` | 32768 | 上下文窗口大小 |
| `--cache-type-k/v` | q8_0 | KV Cache 8-bit 量化 |
| `-fa` | — | Flash Attention 加速 |
| `-sps` | 0.0 | 禁用 slot 复用（无状态） |

### 8.4 启动 llama-server — 嵌入服务

```bash
nohup /root/agent-deploy/llama.cpp/build/bin/llama-server \
  -m /root/agent-deploy/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 \
  --port 8081 \
  --embeddings \
  -ngl 99 \
  -c 2048 \
  --batch-size 1024 \
  > /root/agent-deploy/logs/llama-embed-8081.log 2>&1 &

echo "Embedding PID: $!"
```

### 8.5 等待服务就绪 + 健康检查

```bash
# 等待模型加载（约 10-30 秒）
sleep 15

# LLM 健康检查
curl -s http://localhost:8080/health
# 预期：{"status":"ok"} 或类似响应

# 嵌入服务健康检查
curl -s http://localhost:8081/health
# 预期：{"status":"ok"}

# 确认 GPU 显存占用
nvidia-smi
```

### 8.6 启动 Agent1 控制台（前台测试用）

```bash
cd /root/agent-deploy/agent-system

export JWT_KEY="qazwsxedcrfvtgbyhnujmikolpqazwsx"
export DB_PASSWORD="7758521"
export DOTNET_ENVIRONMENT="Production"

dotnet run --project Agent1 -c Release
```

启动后应看到控制台菜单界面，选择对应的菜单编号进行测试。

### 8.7 启动 Agent1.Api Web 服务（后台运行）

```bash
cd /root/agent-deploy/agent-system

export JWT_KEY="qazwsxedcrfvtgbyhnujmikolpqazwsx"
export DB_PASSWORD="7758521"
export AUTH_ACCOUNTS_JSON='[{"Username":"admin","Password":"Admin@123456","Role":"admin"}]'
export ASPNETCORE_ENVIRONMENT="Production"

nohup dotnet run --project Agent1.Api -c Release \
  > /root/agent-deploy/logs/agent1-api-5000.log 2>&1 &

echo "API PID: $!"

# 等待启动
sleep 5

# API 健康检查
curl -s http://localhost:5000/health/live
curl -s http://localhost:5000/health/ready
```

---

## Task 9: 功能测试

### 9.1 基础设施健康检查

```bash
# 全量健康检查
curl -s http://localhost:5000/health | python3 -m json.tool 2>/dev/null || curl -s http://localhost:5000/health

# Prometheus 指标
curl -s http://localhost:5000/metrics | head -20
```

### 9.2 控制台菜单功能验证

在 Agent1 控制台启动后，依次执行以下菜单项：

| 菜单编号 | 功能 | 说明 |
|---------|------|------|
| **10** | 数据库连接验证 | 验证 PostgreSQL + pgvector 连接正常 |
| **12** | 工具调用诊断验证 | 验证 8 个 KernelFunction 工具可用 |
| **8** | 化工合规自查 | **核心功能**，提交化学品名称进行合规审查 |
| **13** | 合规评测集 | GPU 加速核心验证（64 条评测） |

### 9.3 API 端点测试

#### 9.3.1 登录获取 Token

```bash
# 使用之前配置的 admin 账号
curl -s -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"Admin@123456"}' | python3 -m json.tool
```

保存返回的 `token` 值用于后续请求：

```bash
TOKEN="<粘贴返回的 token>"
```

#### 9.3.2 危化品类别查询

```bash
curl -s -X POST http://localhost:5000/api/compliance/hazard/query \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"substanceName":"苯"}' | python3 -m json.tool
```

#### 9.3.3 储存兼容性检查

```bash
curl -s -X POST http://localhost:5000/api/compliance/storage/check \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"substanceName":"苯"}' | python3 -m json.tool
```

#### 9.3.4 合规综合检查

```bash
curl -s -X POST http://localhost:5000/api/compliance/check \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"substanceName":"苯","queryType":"storage"}' | python3 -m json.tool
```

### 9.4 llama.cpp 推理验证

```bash
# 直接测试 LLM 推理（OpenAI 兼容 API）
curl -s http://localhost:8080/v1/chat/completions \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "gpt-3.5-turbo",
    "messages": [{"role": "user", "content": "你好，请用一句话介绍你自己"}],
    "max_tokens": 100
  }' | python3 -m json.tool

# 测试嵌入服务
curl -s http://localhost:8081/v1/embeddings \
  -H 'Content-Type: application/json' \
  -d '{
    "model": "gpt-3.5-turbo",
    "input": "化工安全合规审查"
  }' | python3 -m json.tool
```

### 9.5 集成测试脚本（可选）

```bash
cd /root/agent-deploy/agent-system

# 自动化测试（25条 CLI 全功能）
bash scripts/auto_test.sh

# Task 11 集成测试（592行 bash，逐用例 PASS/FAIL）
bash scripts/int-test-task11.sh
```

### 9.6 测试通过标准

| 检查项 | 通过标准 |
|--------|---------|
| 数据库连接 | 菜单10 输出 "连接成功" |
| 工具调用 | 菜单12 输出工具诊断结果 |
| 合规审查 | 菜单8 返回合规判断 + 法规引用 |
| API 登录 | 返回 JWT Token |
| API 查询 | 返回化学品结构化数据 |
| LLM 推理 | 返回流式文本响应 |
| 嵌入服务 | 返回向量数组 |
| GPU 加速 | nvidia-smi 显示进程占用显存 |

---

## Task 10: 常见问题与排障指南

### 10.1 .NET SDK 安装失败

**症状**：`dotnet: command not found` 或 `dotnet --version` 无输出

**排查**：
```bash
# 确认安装路径
which dotnet || find / -name dotnet -type f 2>/dev/null
ls /usr/share/dotnet/dotnet
```

**解决**：
```bash
# 手动安装（dotnet-install 脚本，无需包管理器）
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
export PATH="$PATH:/usr/share/dotnet"
echo 'export PATH="$PATH:/usr/share/dotnet"' >> /etc/profile
```

**离线安装**：在本地下载 `.tar.gz` 包 → SCP 上传 → 解压到 `/usr/share/dotnet`：
- 下载地址：https://dotnet.microsoft.com/en-us/download/dotnet/8.0
- 选择 `Linux x64 Binaries`

### 10.2 PostgreSQL pgvector 扩展缺失

**症状**：`CREATE EXTENSION vector` 报错 `extension "vector" is not available`

**排查**：
```bash
# 检查 pgvector 包是否安装
dpkg -l | grep pgvector   # Ubuntu
rpm -qa | grep pgvector   # CentOS
```

**解决**（手动编译 pgvector）：
```bash
cd /tmp
git clone --branch v0.7.0 https://github.com/pgvector/pgvector.git
cd pgvector
make
make install   # 需要 PostgreSQL 开发头文件 (postgresql16-devel)
```

然后重新执行 `CREATE EXTENSION vector;`

### 10.3 PostgreSQL 连接被拒

**症状**：`psql: error: connection to server at "localhost" (127.0.0.1), port 5432 failed: Connection refused`

**排查**：
```bash
# 确认 PostgreSQL 正在运行
systemctl status postgresql* --no-pager
ss -tlnp | grep 5432

# 查看日志
tail -50 /var/log/postgresql/postgresql-16-main.log   # Ubuntu
tail -50 /var/lib/pgsql/16/data/log/postgresql-*.log   # CentOS
```

**解决**：
```bash
# 手动启动
pg_ctlcluster 16 main start      # Ubuntu
systemctl start postgresql-16    # CentOS
```

### 10.4 llama.cpp CUDA 编译报错

#### 10.4.1 nvcc 路径问题

**症状**：`CMAKE_CUDA_COMPILER not found` 或 `nvcc not found`

```bash
# 查找 nvcc 真实路径
find / -name "nvcc" 2>/dev/null
# 可能的路径：/usr/local/cuda/bin/nvcc、/usr/local/cuda-11.6/bin/nvcc

# 编译时指定正确路径
cmake -B build -DGGML_CUDA=ON \
  -DCMAKE_CUDA_COMPILER=/usr/local/cuda-11.6/bin/nvcc \
  -DCMAKE_CUDA_ARCHITECTURES="80"
```

#### 10.4.2 GCC 版本不兼容

**症状**：`error: unsupported GNU version! gcc versions later than X are not supported!`

```bash
# 检查兼容版本
/usr/local/cuda/bin/nvcc --check-gcc

# 如果系统 GCC 过高（如 GCC 12 而 CUDA 11.6 最高支持 GCC 10）：
# 安装兼容的 GCC 版本
dnf install -y gcc-toolset-10
source /opt/rh/gcc-toolset-10/enable

# 然后在编译时指定
cmake -B build -DGGML_CUDA=ON \
  -DCMAKE_C_COMPILER=/opt/rh/gcc-toolset-10/root/usr/bin/gcc \
  -DCMAKE_CXX_COMPILER=/opt/rh/gcc-toolset-10/root/usr/bin/g++ \
  -DCMAKE_CUDA_HOST_COMPILER=/opt/rh/gcc-toolset-10/root/usr/bin/g++ \
  -DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc \
  -DCMAKE_CUDA_ARCHITECTURES="80"
```

#### 10.4.3 CUDA 架构不匹配

**症状**：`nvcc fatal: Unsupported GPU architecture 'compute_XX'`

```bash
# A800 = sm_80，对应的 CMAKE_CUDA_ARCHITECTURES 必须是 "80"
# 不是 "80-real"、"80-virtual"、"86"（那是 RTX 3090）
cmake -B build -DGGML_CUDA=ON -DCMAKE_CUDA_ARCHITECTURES="80"
```

### 10.5 模型文件下载失败

**症状**：`wget` 下载超时或连接拒绝

**解决方案优先级**：
1. **换镜像源**：`hf-mirror.com` → 尝试 `modelscope.cn`
2. **换下载工具**：`wget` → `curl -L -O` → `aria2c`
3. **SCP 上传**：在本地 Windows 下载后，通过 `scp -P 32024` 上传
4. **断点续传**：`wget -c` 支持断点续传

### 10.6 端口冲突

**症状**：`Address already in use` 或服务无法绑定端口

```bash
# 查看端口占用
ss -tlnp | grep -E ":(8080|8081|5000|5432) "

# 杀死占用进程
kill -9 <PID>

# 或修改端口（以 8080 为例）
# llama-server 改为 --port 8090
# appsettings.json 中 Llm.Endpoint 改为 http://localhost:8090/v1
# 环境变量 LLM_ENDPOINT 同步修改
```

### 10.7 GPU 显存不足

**症状**：llama-server 启动失败，日志显示 `CUDA error: out of memory`

```bash
# 查看当前显存占用
nvidia-smi

# 如果显存不足（已有其他模型占用）：
# 1. 释放其他 GPU 进程
# 2. 减少 -ngl 值（少加载几层到 GPU）
# 3. 使用更小的量化模型（Q3_K_M 或 Q2_K）

# 检查是否已有 llama-server 在运行
ps aux | grep llama-server
```

### 10.8 dotnet run 编译失败

**症状**：`error CSxxxx` 编译错误

```bash
# 清理并重新编译
dotnet clean Agent1.sln
dotnet restore Agent1.sln
dotnet build Agent1.sln -c Release

# 检查 .NET 版本
dotnet --version   # 必须 ≥ 8.0.100

# 查看详细错误
dotnet build Agent1.sln -c Release -v detailed 2>&1 | tail -50
```

### 10.9 中文乱码问题

**症状**：控制台输出乱码

```bash
# 设置 UTF-8 编码
export LANG=en_US.UTF-8
export LC_ALL=en_US.UTF-8

# 如果系统不支持 en_US.UTF-8：
localedef -i en_US -f UTF-8 en_US.UTF-8
```

### 10.10 日志查看命令速查

```bash
# llama-server LLM 日志
tail -f /root/agent-deploy/logs/llama-server-8080.log

# llama-server Embed 日志
tail -f /root/agent-deploy/logs/llama-embed-8081.log

# Agent1 API 日志
tail -f /root/agent-deploy/logs/agent1-api-5000.log

# PostgreSQL 日志
tail -f /var/log/postgresql/postgresql-16-main.log     # Ubuntu
tail -f /var/lib/pgsql/16/data/log/postgresql-*.log     # CentOS

# GPU 实时监控
watch -n 2 nvidia-smi
```

---

## 附录 A: 一键启动脚本

将以下内容保存为 `/root/agent-deploy/start-all.sh`：

```bash
#!/bin/bash
set -e

AGENT_ROOT="/root/agent-deploy"
LOG_DIR="$AGENT_ROOT/logs"
mkdir -p "$LOG_DIR"

echo "=== [1/5] Starting PostgreSQL ==="
pg_ctlcluster 16 main start 2>/dev/null || systemctl start postgresql-16 2>/dev/null || true
sleep 2

echo "=== [2/5] Starting llama-server (LLM, port 8080) ==="
nohup $AGENT_ROOT/llama.cpp/build/bin/llama-server \
  -m $AGENT_ROOT/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 32768 \
  --cache-type-k q8_0 --cache-type-v q8_0 -fa -sps 0.0 \
  > $LOG_DIR/llama-server-8080.log 2>&1 &
echo "  PID: $!"

echo "=== [3/5] Starting llama-server (Embedding, port 8081) ==="
nohup $AGENT_ROOT/llama.cpp/build/bin/llama-server \
  -m $AGENT_ROOT/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings -ngl 99 -c 2048 --batch-size 1024 \
  > $LOG_DIR/llama-embed-8081.log 2>&1 &
echo "  PID: $!"

echo "=== Waiting for model loading (20s) ==="
sleep 20

echo "=== [4/5] Health check ==="
curl -sf http://localhost:8080/health && echo "  LLM: OK" || echo "  LLM: FAIL"
curl -sf http://localhost:8081/health && echo "  Embed: OK" || echo "  Embed: FAIL"

echo "=== [5/5] Starting Agent1.Api (port 5000) ==="
cd $AGENT_ROOT/agent-system
export JWT_KEY="${JWT_KEY:-qazwsxedcrfvtgbyhnujmikolpqazwsx}"
export DB_PASSWORD="${DB_PASSWORD:-7758521}"
export AUTH_ACCOUNTS_JSON="${AUTH_ACCOUNTS_JSON:-[{\"Username\":\"admin\",\"Password\":\"Admin@123456\",\"Role\":\"admin\"}]}"
export ASPNETCORE_ENVIRONMENT="Production"

nohup dotnet run --project Agent1.Api -c Release \
  > $LOG_DIR/agent1-api-5000.log 2>&1 &
echo "  API PID: $!"

sleep 5
curl -sf http://localhost:5000/health/live && echo "  API: OK" || echo "  API: FAIL"

echo ""
echo "=== All services started ==="
echo "  LLM:       http://localhost:8080"
echo "  Embedding: http://localhost:8081"
echo "  API:       http://localhost:5000"
echo "  Swagger:   http://localhost:5000/swagger"
echo "  Logs:      $LOG_DIR/"
```

赋予执行权限：`chmod +x /root/agent-deploy/start-all.sh`

---

## 附录 B: 服务停止脚本

将以下内容保存为 `/root/agent-deploy/stop-all.sh`：

```bash
#!/bin/bash
echo "=== Stopping all AgentSystem services ==="

# 停止 .NET API
pkill -f "Agent1.Api" && echo "  API: stopped" || echo "  API: not running"

# 停止 llama-server
pkill -f "llama-server" && echo "  llama-server: stopped" || echo "  llama-server: not running"

# 停止 PostgreSQL（可选，谨慎使用）
# systemctl stop postgresql-16

echo "=== Done ==="
nvidia-smi | grep -E "(MiB|Processes)" -A 2
```

赋予执行权限：`chmod +x /root/agent-deploy/stop-all.sh`

---

## 附录 C: 部署检查清单

在完成部署后，逐项核对：

- [ ] SSH 可连接 a800-prod
- [ ] `nvidia-smi` 正常输出 GPU 信息
- [ ] `dotnet --version` 返回 8.0.xxx
- [ ] PostgreSQL 运行中（端口 5432）
- [ ] `chemical_park_ai_agent` 数据库已创建
- [ ] `vector` 扩展已启用
- [ ] llama.cpp 编译成功（`build/bin/llama-server` 存在）
- [ ] Qwen3-8B GGUF 模型文件就位（~4.7GB）
- [ ] nomic-embed GGUF 模型文件就位（~274MB）
- [ ] llama-server LLM 服务响应健康检查（8080）
- [ ] llama-server Embed 服务响应健康检查（8081）
- [ ] `dotnet build` 0 错误
- [ ] Agent1.Api 健康检查通过（5000）
- [ ] API 登录接口正常返回 Token
- [ ] 危化品查询接口正常返回数据
- [ ] nvidia-smi 显示推理服务占用显存

---

> **文档版本**：v1.0 | **编制日期**：2026-07-06 | **适用分支**：`linux原生编译模型llama.cpp`
