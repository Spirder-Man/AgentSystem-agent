# AutoDL GPU 服务器 Agent1 部署全流程操作记录

> **日期**：2026-06-13  
> **服务器**：AutoDL GPU 算力机（RTX 3090 / 24GB, Xeon Gold 6330, 755GB RAM）  
> **容器 ID**：`autodl-container-k5xt4fu27v-d60014d2`  
> **目标**：在受限容器环境中原生部署 Agent1 化工合规 AI Agent（.NET 8 + PostgreSQL + llama.cpp）

---

## 一、环境基线速览

### 初始环境（SSH 登录时）

| 项目 | 详情 |
|------|------|
| **OS** | Ubuntu 22.04.5 LTS (GNU/Linux 5.15.0-164-generic x86_64) |
| **CPU** | 14 核心 |
| **内存** | 90 GB |
| **GPU** | NVIDIA RTX 3090 (24GB VRAM) |
| **系统盘** | 30G (`/`)，已用 53M |
| **数据盘** | 50G (`/root/autodl-tmp`)，已用 48K |
| **.NET** | 未安装 |
| **PostgreSQL** | 未安装 |
| **Ollama** | 未安装 |
| **CUDA Toolkit** | 未确认（后发现已预装 12.8） |

### 当前实际环境（编译时确认）

| 项目 | 详情 |
|------|------|
| **GPU** | NVIDIA RTX 3090, 24GB VRAM, Compute Capability `sm_86` |
| **CPU** | Intel Xeon Gold 6330, 支持 AVX2 / AVX512 |
| **RAM** | 755GB（容器资源可能动态分配，初始显示 90GB） |
| **OS / glibc** | Ubuntu, glibc 2.35 |
| **CUDA Toolkit** | 12.8 (`/usr/local/cuda/bin/nvcc`, 同时存在 cuda-12 和 cuda-12.8 目录) |
| **.NET SDK** | 8.0.128 |
| **PostgreSQL** | 已运行，端口 5432，已安装 pgvector 扩展 |
| **容器限制** | 不支持 Docker 部署、网络受限（无法直连 GitHub / HuggingFace）、无 iptables 权限 |

### 关键路径约定

| 用途 | 路径 |
|------|------|
| 工作根目录 | `/root/autodl-tmp/` |
| llama.cpp 源码 | `/root/autodl-tmp/llama.cpp/` |
| llama.cpp 构建产物 | `/root/autodl-tmp/llama.cpp/build/` |
| 模型存放目录 | `/root/autodl-tmp/models/` |
| Agent1 项目代码 | `/root/autodl-tmp/agent-system/` |
| JupyterLab 上传默认路径 | `/`（根目录） |

---

## 二、操作时间线

### 阶段前置：本地准备与 AutoDL 实例启动（9:30 - 10:50）

```
约9:30 ─ 用户在本地回顾前次部署经验，打开 GPU服务器一键部署命令清单.md 参考文档
约9:40 ─ 确认本次部署目标：AutoDL 新容器，不支持 Docker，需全部原生部署
约9:50 ─ 启动 AutoDL 算力实例（RTX 3090 / 24GB），等待分配就绪
约10:30 ─ AutoDL 实例分配完成，确认容器 ID：autodl-container-k5xt4fu27v-d60014d2
约10:40 ─ 在本地终端准备 SSH 连接，确认连接地址 connect.nmb2.seetacloud.com
约10:48 ─ 首次 SSH 登录尝试
```

### 阶段〇：环境准备与首次部署尝试（11:00 - 14:00）

```
约10:50 ─ 用户通过 SSH 登录全新 AutoDL 算力容器（Ubuntu 22.04.5）
10:50 ─ 确认容器约束：不支持 Docker、网络受限、sudo 不可用
10:52 ─ AI 给出完整原生部署方案（.NET 8 + PostgreSQL + Ollama + 代码拉取 + 编译启动）
11:21 ─ dotnet --version → command not found（镜像未预装 .NET）
11:21 ─ wget dot.net 下载 dotnet-install.sh 脚本（成功，62KB）
11:22 ─ dotnet-install.sh 开始下载 SDK → 长时间卡住（builds.dotnet.microsoft.com 国内慢）
11:23 ─ 终端出现大量 ^[[20~ 乱码（用户按键回显，下载卡死）
11:24 ─ 决策：放弃 dotnet-install.sh，改用 APT 包管理器安装
11:24 ─ wget Microsoft packages 源 → dpkg -i 安装 → apt install -y dotnet-sdk-8.0
11:25 ─ .NET 8 SDK 安装成功（8.0.128）✅
11:26 ─ 开始安装 PostgreSQL 16 + pgvector
11:27 ─ apt install postgresql-16-pgvector → E: Unable to locate package
11:28 ─ 解决方案：导入 PostgreSQL GPG 密钥 + 添加 APT 源 → 安装成功
11:29 ─ invoke-rc.d policy-rc.d denied execution of start（PostgreSQL 被禁止自动启动）
11:30 ─ pg_ctlcluster 16 main start → 手动启动成功
11:31 ─ sudo -u postgres psql → -bash: -u: command not found（sudo 不存在）
11:32 ─ 解决方案：su - postgres -c "psql -c ..." 替代
11:33 ─ ALTER USER postgres PASSWORD + CREATE DATABASE + CREATE EXTENSION vector → 全部成功 ✅
11:34 ─ 开始 Ollama 安装：curl -fsSL ollama.com/install.sh | sh → 下载极慢
11:40 ─ 用户反馈"下载的太慢了"，AI 建议用 hf-mirror 下载 GGUF 模型文件
11:45 ─ 用户说"我的 ollama 还没下载好"（官方安装脚本仍在下载中）
11:50 ─ AI 建议用 GitHub 镜像加速下载 Ollama 二进制
11:52 ─ ghfast.top 下载 → 404（版本号 v0.30.8 重定向失败）
11:53 ─ ghproxy 镜像下载开始
11:53 ─ 用户问"怎么用命令新开终端"，AI 教用 screen
11:54 ─ 终端粘贴出错：screen 打进了 wget 命令里，下载中断
11:55 ─ 用 nohup wget 后台下载，tail -f 查看进度
11:55 ─ 代码已通过 gitee 克隆成功（git clone gitee.com/liuchao_yue/agent-system）
11:55 ─ tail 查看下载日志，一直在 Connecting...（实际未传输）
11:56 ─ ls -lh /tmp/ollama-linux-amd64.tgz → 0 字节（下载根本没开始）
11:57 ─ GitHub 直连尝试：HTTP request sent, awaiting response... No data received
11:58 ─ Retry GitHub 直连 → 同样无响应
11:59 ─ curl -L --retry 5 → 卡在 0 速度
12:00 ─ 结论：所有 GitHub 渠道在 AutoDL 容器内均不通
12:01 ─ 决策：用户在本地电脑下载后通过 JupyterLab 上传
12:01 ─ 本地 PowerShell curl.exe 下载 ollama-linux-amd64.tgz（1.54GB）
12:02 ─ 首次 curl 报错：%USERPROFILE% 是 cmd 语法，PowerShell 需 $env:USERPROFILE
12:03 ─ 修正后下载速度 ~1MB/s，预计 24 分钟
12:05 ─ 等待期间并行操作：服务器端执行数据库初始化
12:06 ─ su - postgres -c psql → Connection refused（PostgreSQL 未启动）
12:07 ─ pg_ctlcluster 16 main start → Removed stale pid file → 启动成功
12:08 ─ su - postgres -c psql -f init_database.sql → Permission denied（/root 路径不可访问）
12:09 ─ 解决方案：cp 到 /tmp/ 再执行
12:10 ─ 数据库建表脚本执行失败：SQL 脚本含多条 SELECT 语句导致部分执行
12:12 ─ .NET 项目编译：dotnet restore → 成功（26.54 sec）
12:13 ─ dotnet publish → CS0246 错误：IMemoryService、MemoryCoordinator 找不到
12:14 ─ 排查：ls Agent1/Services/Memory/ → No such file or directory
12:15 ─ 原因：Gitee 仓库缺少 Memory 模块源码目录
12:16 ─ 本地 git status → Memory/ 被 .gitignore 第 75 行 memory/ 规则误排除
12:17 ─ 修复：.gitignore 中 memory/ 改为 /memory/（仅排除根目录）
12:18 ─ git add .gitignore Agent1/Services/Memory/ → git commit → git push
12:19 ─ 服务器 git pull → 8 files changed, 1312 insertions（Memory 模块全部拉取成功）
12:20 ─ dotnet publish → 编译成功（只有警告，0 Error）✅
12:21 ─ 数据库初始化重新执行：PGPASSWORD=7758521 psql -f /tmp/init_database.sql
12:22 ─ ✅ 数据库初始化成功！6 张表就绪（含 pgvector HNSW 索引）
12:25 ─ 本地 Ollama 下载完成（1.54GB，100%）
12:26 ─ JupyterLab 页面导航问题：在文件存储页而非容器实例页
12:28 ─ 指导用户回到容器实例列表 → 点击 JupyterLab 链接
12:30 ─ JupyterLab 已打开，但用户不熟悉上传操作
12:35 ─ 文件上传：选 C:\Users\lcy\Downloads\ollama-linux-amd64.tgz
12:40 ─ 上传完成，但服务器上找不到文件
12:42 ─ find / -name "ollama*.tgz" → 找到 /root/ollama-linux-amd64.tgz 和 /tmp/ollama-linux-amd64.tgz
12:45 ─ tar -xzf /root/ollama-linux-amd64.tgz -C /usr → 解压成功
12:46 ─ ollama --version → Segmentation fault (core dumped) ❌
12:47 ─ 开始诊断：uname -m、ldd、file、ls -lh → 全部正常
12:48 ─ ldd --version → glibc 2.35（满足要求）
12:49 ─ grep -o 'avx2\?' → 返回 avx（误判无 AVX2！）
12:50 ─ 尝试下载旧版 Ollama v0.1.48 → 所有镜像均失败
```

### 阶段一：Ollama 二进制部署尝试（14:40 - 15:30）

```
14:40 ─ 用户在 Windows 通过 ghproxy 镜像下载 ollama-linux-amd64.tgz
14:50 ─ JupyterLab 上传到 AutoDL 服务器
14:55 ─ 文件找不到：ls /ollama-linux-amd64.tgz → No such file
14:55 ─ 发现 JupyterLab 上传默认落盘到 / 根目录（而非 /root/autodl-tmp/）
14:56 ─ find / -name "ollama*.tgz" → 发现两处：/root/ 和 /tmp/
14:58 ─ tar -xzf /root/ollama-linux-amd64.tgz -C /usr → 解压成功
15:00 ─ ollama --version → Segmentation fault (core dumped) ❌
15:02 ─ 开始系统化诊断：uname -m（x86_64 ✓）、ldd（所有依赖 ✓）
15:03 ─ file 检查：ELF 64-bit LSB executable, stripped → 1.1GB 文件完整
15:04 ─ ldd --version → glibc 2.35（满足 Ollama 要求）
15:06 ─ 首次 grep 误判：grep -o 'avx2\?' → 只返回 "avx"，误以为无 AVX2
15:08 ─ 尝试下载旧版 Ollama v0.1.48 作为替代方案
15:10 ─ wget ghproxy → Connection timed out（容器网络受限）
15:12 ─ 在 Windows 本地下载 → GitHub 直连返回 9 字节（被阻断）
15:14 ─ 换 ghproxy 镜像 → SSL/TLS connection failed（schannel 问题）
15:16 ─ 换 ghfast.top 镜像 → 同样 SSL 错误
15:18 ─ 换 github.moeyy.xyz 镜像 → 同样 SSL 错误
15:20 ─ 回到 AutoDL 终端直连 GitHub → 极慢，0 速度
15:22 ─ 在 AutoDL 终端走 ghproxy → Connection timed out（容器内也不行）
15:25 ─ 正确 grep flags → 发现 CPU 有 avx2 / avx512 全套指令集！
15:28 ─ 结论：Ollama segfault 非 CPU 问题，是预编译二进制与容器环境微妙兼容性
```

### 阶段二：转向 llama.cpp 原生编译（15:30 - 16:00）

```
15:30 ─ 决策：放弃 Ollama，转向 llama.cpp 原生编译
15:32 ─ 确认编译环境：cmake/gcc/g++ 齐备，755GB RAM，Xeon Gold 6330
15:33 ─ 完整 flags 确认：avx2 ✓, avx512f/vnni ✓ → CPU 能力足够
15:35 ─ git clone from gitee 镜像（避免 GitHub 网络问题）
15:38 ─ 首次编译 CPU 版 cmake -B build -DGGML_AVX2=ON
15:42 ─ CPU 版编译进行中...
15:48 ─ [100%] CPU 版编译完成（llama-server、llama-cli 等）
15:50 ─ 验证编译产物：llama-server 18KB（动态链接版）
15:52 ─ 检查 GPU 环境：nvidia-smi → RTX 3090 24GB, Driver 580.105.08
15:54 ─ 发现 CUDA Toolkit：/usr/local/cuda-12.8/bin/nvcc、/usr/local/cuda/bin/nvcc
15:55 ─ 开始 CUDA 版重新编译
```

### 阶段三：CUDA GPU 编译与模型下载（16:00 - 16:40）

```
15:56 ─ 第一次 CUDA cmake 失败：CMAKE_CUDA_COMPILER-NOTFOUND
15:58 ─ 显式指定 CMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc → 配置成功
15:59 ─ CUDA 配置输出：NVIDIA 12.8.93, sm_86, NCCL found
16:00 ─ cmake --build 开始 CUDA GPU 编译
16:02 ─ 开始模型下载尝试：hf-mirror Qwen3 GGUF → 404（文件名不对）
16:03 ─ bartowski 源 → 404（文件名大小写不匹配）
16:04 ─ ModelScope 源 → 404
16:05 ─ 通过 HF API 查 bartowski 仓库文件列表 → 无返回
16:08 ─ 尝试 Mradermacher 源 → 也无结果
16:10 ─ WebSearch 找到正确文件名：Qwen_Qwen3-8B-Q4_K_M.gguf
16:12 ─ 正确 URL：bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf
16:14 ─ 但 AutoDL 容器下载极慢，决定在 Windows 本地下载
16:15 ─ Windows 本地 curl.exe 开始下载 4.68GB 模型
16:16 ─ 速度 ~1.1MB/s，预计 50-55 分钟
16:18 ─ CUDA 编译完成：llama.cpp [100%] Built target llama-server ✅
16:20 ─ 验证：llama-server 18KB（动态链接 CUDA 版）
16:25 ─ 发现服务器上已有项目代码（agent-system 目录已克隆）
16:28 ─ PostgreSQL 运行中、.NET 8.0.128、RTX 3090 就绪
```

### 阶段四：数据库初始化与项目编译（16:30 - 17:00）

```
16:30 ─ PostgreSQL 初始化尝试：su - postgres → 密码认证失败
16:32 ─ 解决方案：PGPASSWORD=7758521 psql → 成功
16:33 ─ 数据库初始化完成：6 张表（含 pgvector 768 维向量表）
16:35 ─ .NET 项目编译：dotnet restore + dotnet build → 0 Error, 0 Warning
16:36 ─ 检查 appsettings.json：LLM 端点指向 localhost:11434（Ollama）
16:38 ─ 确认需改：Endpoint + ModelId 适配 llama.cpp
16:40 ─ 模型下载进度检查（多次）
16:45 ─ 59% → 2.18MB/s
16:50 ─ 85% → 2.9MB/s，预计 4 分钟
16:55 ─ 100%！Qwen_Qwen3-8B-Q4_K_M.gguf 下载完成（4.68GB）✅
```

### 阶段五：嵌入模型下载与文档整理（17:00 - 18:00）

```
17:00 ─ 开始下载 nomic-embed-text-v1.5.f16.gguf（768 维，~274MB）
17:05 ─ 生成部署全流程操作记录文档 v1.0
17:20 ─ 文档补充：根据对话历史，补全早上 14:00 起的完整操作记录
17:25 ─ 嵌入模型下载进度检查
17:30 ─ nomic-embed-text-v1.5.f16.gguf 下载完成（261.5MB，3 分 36 秒）✅
17:32 ─ 用户确认两个模型文件均下载完毕，获取本地路径准备上传
17:35 ─ 文档持续更新至 v2.0，覆盖 10:50-17:35 全流程
```

### 阶段六：模型文件上传至 AutoDL 服务器（18:00 - 18:15）

```
约18:00 ─ 用户打开 AutoDL 网页 → 容器实例列表 → 点击 JupyterLab 链接
约18:02 ─ JupyterLab 文件浏览器打开，导航到上传界面
约18:03 ─ 上传 Qwen_Qwen3-8B-Q4_K_M.gguf（4.68GB）：选择文件 → 上传
约18:08 ─ Qwen3-8B 推理模型上传完成（大文件，耗时约 5 分钟）
约18:09 ─ 上传 nomic-embed-text-v1.5.f16.gguf（261.5MB）：选择文件 → 上传
约18:10 ─ nomic-embed-text 嵌入模型上传完成（小文件，耗时约 1 分钟）
约18:11 ─ 服务器执行 find 定位文件：确认两个模型在 /（根目录）
约18:12 ─ mkdir -p /root/autodl-tmp/models/（模型目录已存在，跳过）
约18:13 ─ mv 两个 .gguf 文件到 /root/autodl-tmp/models/
约18:14 ─ ls -lh 验证：两个模型文件在目标目录，大小正确 ✅
约18:15 ─ 两个模型文件上传和部署就绪 ✅
```

---

## 三、详细操作记录

### 3.0-早 环境准备、基础依赖安装与代码修复（11:00 - 13:00）

#### 3.0-早.1 SSH 登录与环境确认

用户在本地通过 SSH 登录全新分配的 AutoDL 算力容器：

```
SSH 登录：ssh root@connect.nmb2.seetacloud.com（密码认证）
初始显示：Ubuntu 22.04.5 LTS (GNU/Linux 5.15.0-164-generic x86_64)
容器 ID：autodl-container-k5xt4fu27v-d60014d2
AutoDL 目录说明：
  /                → 系统盘（30G，关机不丢数据，随镜像保存）
  /root/autodl-tmp → 数据盘（50G，关机不丢数据，不随镜像保存）
CPU ：14 核心
内存：90 GB
GPU ：NVIDIA GeForce RTX 3090, 1
```

用户说明这是"重新部署到一个服务器上，这个服务器也不支持容器化，之前已经踩过一次坑了"。

AI 给出完整原生部署方案的总览：.NET 8 SDK → PostgreSQL + pgvector → Ollama 原生安装 → 拉取模型 → 拉取代码 → 修改配置 → 初始化数据库 → 编译启动 API。

#### 3.0-早.2 .NET 8 SDK 安装（首次尝试：dotnet-install.sh 失败）

```bash
# 检查是否已安装
dotnet --version
# 输出：-bash: dotnet: command not found（镜像未预装 .NET）

# 下载微软官方安装脚本
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# 执行过程：
# --2026-06-13 11:21:59--  dot.net → 301 → builds.dotnet.microsoft.com
# 脚本下载成功（62KB），开始下载 dotnet-sdk-8.0.422-linux-x64.tar.gz
# 终端随后出现大量 ^[[20~ 乱码，下载长时间卡住
```

> 🔴 **问题 #1**：`builds.dotnet.microsoft.com` 国内访问极慢/超时，安装脚本卡死，用户按键（F9 等）导致终端回显 ESC 序列乱码。

**解决方案：改用 APT 包管理器安装**

```bash
# 添加微软 APT 源
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb \
  -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update
apt install -y dotnet-sdk-8.0

# 安装成功：
# dotnet-sdk-8.0 (8.0.128-0ubuntu1~22.04.1)
# Setting up aspnetcore-runtime-8.0 (8.0.28-0ubuntu1~22.04.1)
# Setting up dotnet-sdk-8.0 (8.0.128-0ubuntu1~22.04.1)
```

✅ .NET 8 SDK 8.0.128 安装成功。

#### 3.0-早.3 PostgreSQL 16 + pgvector 安装与配置

```bash
# 首次安装尝试
apt install -y postgresql postgresql-client postgresql-16-pgvector
# 错误：E: Unable to locate package postgresql-16-pgvector
# 原因：默认 Ubuntu 源不含 pgvector 扩展包

# 解决方案：导入 PostgreSQL 官方 GPG 密钥 + 添加 APT 源
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | \
  gpg --dearmor -o /usr/share/keyrings/postgresql.gpg --yes
echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] \
  http://apt.postgresql.org/pub/repos/apt jammy-pgdg main" | \
  tee /etc/apt/sources.list.d/pgdg.list
apt update
apt install -y postgresql-16 postgresql-client-16 postgresql-16-pgvector
```

安装输出显示 `invoke-rc.d: policy-rc.d denied execution of start`：

```bash
# 手动启动 PostgreSQL
pg_ctlcluster 16 main start

# 验证状态
pg_lsclusters
# Ver Cluster Port Status Owner    Data directory
# 16  main    5432 online postgres /var/lib/postgresql/16/main
```

> 🔴 **问题 #2**：`sudo -u postgres psql` → `-bash: -u: command not found`。AutoDL 精简容器中 `sudo` 不可用。

**解决方案：使用 `su` 替代**

```bash
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD '7758521';\""
# ALTER ROLE
su - postgres -c "psql -c \"CREATE DATABASE chemical_park_ai_agent;\""
# CREATE DATABASE
su - postgres -c "psql -d chemical_park_ai_agent -c \"CREATE EXTENSION IF NOT EXISTS vector;\""
# CREATE EXTENSION
```

✅ PostgreSQL 16 数据库就绪，pgvector 扩展已启用。

#### 3.0-早.4 Ollama 首次下载尝试（全部失败）

以下为 **完整的 7 轮失败尝试记录**，展现了 AutoDL 容器网络限制的严重程度：

**第 1 轮：官方安装脚本**

```bash
curl -fsSL https://ollama.com/install.sh | sh
# 下载极慢，用户反馈等不及
```

**第 2 轮：ghfast.top 镜像**

```bash
wget -O /tmp/ollama-linux-amd64.tgz \
  https://ghfast.top/github.com/ollama/ollama/releases/latest/download/ollama-linux-amd64.tgz
# → 302 重定向到 github.com → 404（版本号 v0.30.8 不存在）
```

**第 3 轮：ghproxy 镜像（容器内）**

```bash
wget https://mirror.ghproxy.com/https://github.com/ollama/ollama/releases/download/v0.3.14/ollama-linux-amd64.tgz
# → 一直在 Connecting...，实际连接后 0 字节传输
```

**第 4 轮：GitHub 直连（容器内）**

```bash
wget https://github.com/ollama/ollama/releases/download/v0.3.14/ollama-linux-amd64.tgz
# → connected, awaiting response... No data received
```

**第 5 轮：curl 加超时重试（容器内）**

```bash
curl -L --retry 5 --retry-delay 10 -o /tmp/ollama-linux-amd64.tgz \
  https://github.com/ollama/ollama/releases/download/v0.3.14/ollama-linux-amd64.tgz
# → 卡在 0 速度，无数据传输
```

**第 6 轮：本地 PowerShell ghproxy（Windows）**

```powershell
curl.exe -L -o D:\桌面\ollama-linux-amd64.tgz \
  https://mirror.ghproxy.com/https://github.com/ollama/ollama/releases/download/v0.1.48/ollama-linux-amd64.tgz
# → SSL/TLS connection failed（schannel 错误）
```

**第 7 轮：本地 PowerShell ghfast.top / github.moeyy.xyz**

```powershell
# 两个镜像均返回 SSL/TLS handshake 失败
curl.exe -L -o D:\桌面\ollama-linux-amd64.tgz \
  https://ghfast.top/https://github.com/ollama/ollama/releases/download/v0.1.48/ollama-linux-amd64.tgz
# → 349 字节后被阻断（只下载了一个重定向页面而非文件）
```

> 💡 **关键决策**：所有 GitHub 渠道在 AutoDL 和本地 Windows 均失败后，确定唯一可行路径为 **PowerShell 本地直连 GitHub 下载 → JupyterLab 上传**。

#### 3.0-早.5 本地下载 Ollama 二进制（成功）

```powershell
# 最终成功命令（Windows 本地执行）
curl.exe -L -o $env:USERPROFILE\Downloads\ollama-linux-amd64.tgz \
  https://github.com/ollama/ollama/releases/download/v0.3.14/ollama-linux-amd64.tgz
# 输出：100  1.54G  100  1.54G  1.55M  17:06
```

> 🔴 **问题 #3**：PowerShell 中 `%USERPROFILE%` 语法错误。PowerShell 环境变量写法为 `$env:USERPROFILE`，而非 cmd 的 `%USERPROFILE%`。

下载进度：1.54GB 文件，速度 ~1.55MB/s，约 17 分钟。

#### 3.0-早.6 代码拉取、.gitignore 修复与 .NET 编译

**代码克隆**

```bash
cd /root/autodl-tmp
git clone https://gitee.com/liuchao_yue/agent-system.git
```

**首次编译 → CS0246 错误**

```bash
cd /root/autodl-tmp/agent-system
dotnet restore Agent1.Api/Agent1.Api.csproj
# → Restored in 26.55 sec
dotnet publish Agent1.Api/Agent1.Api.csproj -c Release -o publish
# 错误：CS0246: The type or namespace name 'IMemoryService' could not be found
# 错误：CS0246: The type or namespace name 'MemoryCoordinator' could not be found
```

**排查**

```bash
ls /root/autodl-tmp/agent-system/Agent1/Services/Memory/
# ls: cannot access '.../Memory/': No such file or directory
```

**根因**：`.gitignore` 第 75 行的 `memory/` 规则匹配了 `Agent1/Services/Memory/` 目录，导致 Memory 模块源码未被推送到 Gitee。

**修复（本地执行）**

```bash
# 将 memory/ 改为 /memory/（只排除仓库根目录，允许子目录中的 Memory/）
(gc .gitignore) -replace '^memory/$', '/memory/' | sc .gitignore
git add .gitignore Agent1/Services/Memory/
git commit -m "修复 .gitignore 误排除 Memory 源码目录 + 补充 Memory 模块文件"
git push origin master
```

**服务器拉取更新并重新编译**

```bash
cd /root/autodl-tmp/agent-system
git pull origin master
# Updating 932a735..137fe87
# 8 files changed, 1312 insertions(+)
# Agent1/Services/Memory/FactExtractor.cs  (109 行)
# Agent1/Services/Memory/IMemoryService.cs (42 行)
# Agent1/Services/Memory/MemoryCoordinator.cs (224 行)
# Agent1/Services/Memory/MemoryService.cs (507 行) ... 等

dotnet publish Agent1.Api/Agent1.Api.csproj -c Release -o publish
# 编译成功（只有 nullable 警告，0 Error）✅
```

#### 3.0-早.7 数据库初始化（多次尝试）

**尝试 1：PostgreSQL 未启动**

```bash
su - postgres -c "psql -d chemical_park_ai_agent -f /root/autodl-tmp/agent-system/init_database.sql"
# 错误：Connection refused → PostgreSQL 未运行
```

**修复：手动启动**

```bash
pg_ctlcluster 16 main start
# Removed stale pid file.
```

**尝试 2：文件权限被拒绝**

```bash
su - postgres -c "psql -d chemical_park_ai_agent -f /root/autodl-tmp/agent-system/init_database.sql"
# 错误：Permission denied（postgres 用户无法访问 /root/ 目录下的文件）
```

**修复：复制到 /tmp/**

```bash
cp /root/autodl-tmp/agent-system/init_database.sql /tmp/init_database.sql
su - postgres -c "psql -d chemical_park_ai_agent -f /tmp/init_database.sql"
# ✅ 6 张表创建成功
```

### 3.0 下午 Ollama 二进制部署尝试（失败 — 完整踩坑记录）

#### 3.0.1 文件上传与定位

用户在 Windows 本地通过 ghproxy 镜像下载 `ollama-linux-amd64.tgz`，然后通过 JupyterLab 网页界面上传到 AutoDL 服务器。

> 🔴 **关键坑点 #1**：JupyterLab 文件上传的默认落盘路径是 `/`（根目录），而非用户期望的 `/root/autodl-tmp/`。这导致后续 `ls -lh /root/autodl-tmp/ollama*` 始终找不到文件。

```bash
# 错误尝试（找不到文件）
ls -lh /ollama-linux-amd64.tgz
# ls: cannot access '/ollama-linux-amd64.tgz': No such file or directory → 因为路径不对

ls -lh /root/autodl-tmp/ollama-linux-amd64.tgz
# ls: cannot access '...': No such file or directory → 上传到了根目录，不在这里

# 正确做法：全局搜索
find / -name "ollama*.tgz" 2>/dev/null
# 输出：
# /root/ollama-linux-amd64.tgz    ← 实际位置
# /tmp/ollama-linux-amd64.tgz     ← 临时副本
```

**教训**：JupyterLab 上传文件后，始终用 `find / -name 'filename*' 2>/dev/null` 定位，不要猜测路径。

#### 3.0.2 解压安装与 Segmentation Fault

```bash
# 解压到系统目录
tar -xzf /root/ollama-linux-amd64.tgz -C /usr

# 验证运行
ollama --version
# 输出：Segmentation fault (core dumped)
```

Ollama v0.3.14 的预编译二进制（1.1GB，x86_64，ELF 64-bit LSB executable）在解压后直接段错误崩溃。

#### 3.0.3 系统化诊断过程

这是一个经典的多维度排障过程，逐步排除了各个可能原因：

```bash
# 排查方向 1：架构错误？
uname -m
# x86_64 → ✓ 架构正确

# 排查方向 2：缺少动态链接库？
ldd /usr/bin/ollama
# linux-vdso.so.1, libresolv.so.2, libpthread.so.0, librt.so.1,
# libdl.so.2, libstdc++.so.6, libm.so.6, libgcc_s.so.1, libc.so.6
# 所有 .so 依赖均已找到 → ✓ 非库缺失问题

# 排查方向 3：文件损坏？
file /usr/bin/ollama
# ELF 64-bit LSB executable, x86-64, version 1 (SYSV),
# dynamically linked, for GNU/Linux 2.6.32, stripped

ls -lh /usr/bin/ollama
# -rwxr-xr-x 1 root root 1.1G Oct 21 2024 /usr/bin/ollama
# → ✓ 文件完整，头文件正常

# 排查方向 4：glibc 版本过低？
ldd --version
# ldd (Ubuntu GLIBC 2.35-0ubuntu3.8) 2.35
# → ✓ glibc 2.35 ≥ 2.32 最低要求

# 排查方向 5：CPU 指令集不兼容？
# ⚠️ 首次误判！
grep -o 'avx2\?' /proc/cpuinfo | head -1
# 输出：avx    ← 正则 ? 表示 0 或 1 次匹配，只匹配到了 "avx"
# 结论（错误）：无 AVX2 支持！

# ✅ 正确做法：查看完整 CPU flags 行
grep flags /proc/cpuinfo | head -1
# flags: ... avx avx2 ... avx512f avx512dq ... avx512_vnni ...
# → ✓✓✓ CPU 有完整 AVX2 + AVX-512 全家桶！
```

> ⚠️ **重要教训**：`grep -o 'avx2\?'` 的正则 `?` 表示"0 次或 1 次匹配"，所以 `avx` 就能匹配成功。正确的检查方式是用 `grep flags /proc/cpuinfo | head -1` 查看完整 flags 行，确认 `avx2` 是否独立出现。

#### 3.0.4 Ollama 附属库分析

检查 Ollama 自带的 CUDA 运行库（`/usr/lib/ollama/`）：

```
/usr/lib/ollama/:
libcublas.so.11.5.1.109  (117M)   # CUDA 11.x 兼容
libcublas.so.12.4.2.65   (105M)   # CUDA 12.x 兼容
libcublasLt.so.11.5.1.109 (252M)  # CUDA 11.x
libcublasLt.so.12.4.2.65  (421M)  # CUDA 12.x
libcudart.so.11.3.109    (605K)
libcudart.so.12.4.99     (692K)
```

Ollama 自带了 CUDA 运行时库（`libcublas`、`libcublasLt`、`libcudart`），这意味着它试图加载自己的 CUDA 库，可能与系统 CUDA 12.8 驱动产生冲突。

#### 3.0.5 尝试下载旧版 Ollama（多线失败）

在无法确认 segfault 根因的情况下，尝试下载旧版本 v0.1.48 进行对比测试：

| 尝试 | 位置 | 命令 / URL | 结果 |
|------|------|-----------|------|
| 1 | AutoDL 终端 | `wget ghproxy.com/...v0.1.48...` | Connection timed out |
| 2 | Windows 本地 | `curl.exe GitHub 直连` | 仅返回 9 字节（被阻断） |
| 3 | Windows 本地 | `curl.exe ghproxy 镜像` | SSL/TLS connection failed（schannel） |
| 4 | Windows 本地 | `curl.exe ghfast.top` | 同样 SSL 错误 |
| 5 | Windows 本地 | `curl.exe github.moeyy.xyz` | 同样 SSL 错误 |
| 6 | AutoDL 终端 | `curl GitHub 直连` | 极慢，0 速度 |
| 7 | AutoDL 终端 | `wget ghproxy` | Connection timed out |

> 🔴 **关键约束确认**：AutoDL 容器网络严重受限 —— GitHub 直连超时、ghproxy 镜像超时/SSL 错误、所有国内镜像均不可用。唯一的可行路径是：**Windows 本地下载 → JupyterLab 上传到 AutoDL**。

#### 3.0.6 最终结论

Ollama v0.3.14 预编译二进制在 glibc 2.35、AVX2 齐全的 AutoDL 容器中 Segmentation fault，根因并非 CPU/glibc 兼容性，而是：

1. **Ollama 自带 CUDA 运行时库**（/usr/lib/ollama/libcublas.so.12.4.2.65）可能与系统 CUDA 12.8 驱动/库版本冲突
2. **容器环境的 seccomp / AppArmor 等安全策略**可能阻止了某些系统调用
3. **Ollama 编译时的特定编译参数**与当前运行时不匹配

鉴于网络限制无法下载替代版本，且 AutoDL 不支持 Docker，决定转向 **llama.cpp 原生编译**。

---

### 3.2 转向 llama.cpp 原生编译

#### 决策理由

| 方案 | 可行性 | 理由 |
|------|--------|------|
| Ollama 预编译二进制 | ❌ | Segmentation fault，容器环境兼容性问题 |
| Docker 部署 Ollama | ❌ | AutoDL 算力机不支持 Docker |
| llama.cpp 原生编译 | ✅ | 从源码编译，完全适配当前环境 |

#### CPU 版本编译（成功）

```bash
# 从 gitee 镜像克隆（避免 GitHub 网络问题）
cd /root/autodl-tmp
git clone https://gitee.com/mirrors/llama.cpp.git

# CMake 配置（CPU 版，默认）
cd llama.cpp
cmake -B build

# 编译
cmake --build build --config Release -j$(nproc)
# → [100%] 编译成功
```

#### CUDA GPU 加速版本编译

```bash
# 先检查 CUDA 环境
nvidia-smi                      # RTX 3090, 24GB
ls /usr/local/cuda/bin/nvcc     # 确认 nvcc 存在
nvcc --version                  # NVIDIA 12.8.93
```

**第一次 CUDA CMake 配置 → 失败**：

```bash
cmake -B build -DGGML_CUDA=ON -DCMAKE_CUDA_ARCHITECTURES="86"
# 错误：CMAKE_CUDA_COMPILER-NOTFOUND
# CMake 无法自动发现 nvcc 路径
```

**解决方案：显式指定 nvcc 编译器路径**

```bash
rm -rf build
cmake -B build \
  -DGGML_CUDA=ON \
  -DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc \
  -DCMAKE_CUDA_ARCHITECTURES="86"

# 配置输出确认：
#   CUDA compiler: NVIDIA 12.8.93
#   CMAKE_CUDA_ARCHITECTURES=86
#   NCCL found
#   Build files written to /root/autodl-tmp/llama.cpp/build

# 编译
cmake --build build --config Release -j$(nproc)
# → [100%] Built target llama-server
# → [100%] Built target llama-cli
# → [100%] Built target llama-app
```

#### 编译结果验证

```bash
ls -lh /root/autodl-tmp/llama.cpp/build/bin/llama-server
# -rwxr-xr-x 1 root root 18K Jun 13 15:58 llama-server
```

> **说明**：`llama-server` 二进制仅 18KB，因为它链接的是 `libllama-server-impl.so` 动态库，实际推理逻辑在共享库中。

---

### 3.3 模型文件下载

#### 完整下载尝试记录（共 8 轮）

| 轮次 | 来源 | URL / 路径 | 错误 | 原因分析 |
|------|------|-----------|------|----------|
| 1 | hf-mirror | `Qwen/Qwen3-8B-Instruct-GGUF/resolve/main/qwen3-8b-instruct-q4_k_m.gguf` | 404 | 文件名大小写错误，qwen3 仓库中无此文件 |
| 2 | hf-mirror | `bartowski/Qwen3-8B-Instruct-GGUF/resolve/main/Qwen3-8B-Instruct-Q4_K_M.gguf` | 404 | HF 仓库名不匹配，bartowski 下的 Qwen3 仓库名使用下划线格式 |
| 3 | ModelScope | 搜索 "Qwen3-8B-GGUF" | 无结果 | ModelScope 未收录该模型 |
| 4 | hf-mirror API | `curl -s https://hf-mirror.com/api/models/bartowski/Qwen3-8B-Instruct-GGUF` | 无返回 | hf-mirror 不代理 HF API |
| 5 | HF 直连 API | `curl -sL https://huggingface.co/api/models/bartowski/Qwen3-8B-Instruct-GGUF` | 极慢 | 容器内直连 HuggingFace 不可行 |
| 6 | HF API Mradermacher | `curl -sL huggingface.co/api/models/mradermacher/Qwen3-8B-Instruct-GGUF-i1` | 无返回 | 同样网络超时 |
| 7 | WebSearch | 搜索引擎查询 "Qwen3-8B GGUF bartowski" | ✅ | 找到正确仓库名和文件名 |
| 8 | hf-mirror | `bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf` | ✅ | 下载成功！ |

> 💡 **关键教训**：当多次 404 时，最可靠的方法是通过 [huggingface.co](https://huggingface.co) 网页直接浏览仓库文件列表，或使用搜索引擎查询 `模型名 GGUF bartowski`，而不是猜测文件名。

#### 最终成功方案（LLM 推理模型）

**正确 GGUF 文件路径**（通过 WebSearch 确认）：

```
bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf
```

**关键发现**：
- Qwen3-8B-Instruct 的 GGUF 量化版仓库名为 `bartowski/Qwen_Qwen3-8B-GGUF`（**注意下划线格式** `Qwen_Qwen3-8B-GGUF`）
- Q4_K_M 量化约 4.68GB，适合 RTX 3090 24GB VRAM，推理质量/速度均衡
- 国内需通过 hf-mirror.com 镜像下载

**下载命令（在 Windows 本地执行）**：

```powershell
curl.exe -L -o D:\桌面\Qwen_Qwen3-8B-Q4_K_M.gguf https://hf-mirror.com/bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf
```

**下载进度变化**：

| 时间 | 进度 | 速度 | 预计剩余 |
|------|------|------|----------|
| 16:16 | 0% | ~1.1 MB/s | ~50 min |
| 16:30 | ~5% | ~1.0 MB/s | ~55 min |
| 16:45 | 59% | ~2.2 MB/s | ~15 min |
| 16:50 | 85% | ~2.9 MB/s | ~4 min |
| 16:55 | 100% | 3.5 MB/s (峰值) | 完成 ✅ |

**最终**：4.68GB 文件，全程约 50 分钟，平均速度 ~1.6MB/s。

#### 嵌入模型下载（nomic-embed-text）

Agent1 项目需要 768 维嵌入模型用于 RAG 向量检索。在 LLM 推理模型下载完成后，立即开始下载嵌入模型。

**嵌入模型信息**：

| 属性 | 值 |
|------|-----|
| 模型名 | `nomic-embed-text-v1.5` |
| GGUF 仓库 | `nomic-ai/nomic-embed-text-v1.5-GGUF` |
| 推荐量化 | F16（全精度，嵌入模型不建议量化以避免精度损失） |
| 文件大小 | ~274MB |
| 向量维度 | 768 |

**下载命令（在 Windows 本地执行）**：

```powershell
curl.exe -L -o D:\桌面\nomic-embed-text-v1.5.f16.gguf https://hf-mirror.com/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf
```

预计下载时间：2-5 分钟（仅 274MB）。

> 📌 **为什么选 F16 而非量化版？** 嵌入模型的任务是将文本映射到高维向量空间，量化会显著影响向量质量，进而降低 RAG 检索准确率。F16 全精度是嵌入任务的推荐配置。

---

### 3.4 PostgreSQL 数据库初始化

#### 数据库信息

| 配置项 | 值 |
|--------|-----|
| 数据库名 | `chemical_park_ai_agent` |
| 用户 | `postgres` |
| 密码 | `7758521` |
| 端口 | `5432` |
| 初始化脚本 | `/root/autodl-tmp/agent-system/init_database.sql` |

#### 第一次尝试（失败）

```bash
psql -h localhost -U postgres -f init_database.sql
# 错误：FATAL: password authentication failed for user "postgres"
# 原因：psql 非交互模式需要显式提供密码
```

#### 解决方案

```bash
PGPASSWORD=7758521 psql -h localhost -U postgres -f /root/autodl-tmp/agent-system/init_database.sql
```

#### 初始化结果

成功创建 **6 张表**：

| 表名 | 说明 |
|------|------|
| `chemical_documents` | 化工文档（含 pgvector `vector(768)` 嵌入向量列） |
| `audit_logs` | 审计日志 |
| `long_term_memories` | 长期记忆 |
| `refresh_tokens` | JWT Refresh Token |
| `search_logs` | 检索日志 |
| `sessions` | 会话记录 |

**`chemical_documents` 表关键索引**：

| 索引 | 类型 | 说明 |
|------|------|------|
| `idx_chemical_documents_embedding_hnsw` | HNSW | 向量余弦相似度索引 (m=16, ef_construction=200) |
| `idx_chemical_documents_content_gin` | GIN | 全文检索索引 |
| `idx_chemical_documents_regulation_type` | B-tree | 法规类型索引 |
| `idx_chemical_documents_chemical_type` | B-tree | 化学品类型索引 |

---

### 3.5 Agent1 项目编译与配置

#### .NET 编译

```bash
cd /root/autodl-tmp/agent-system
dotnet restore Agent1.sln
dotnet build Agent1.sln -c Release --no-restore
# 输出：Build succeeded. 0 Warning(s) 0 Error(s)
```

#### 环境变量配置（.env）

位置：`/root/autodl-tmp/agent-system/.env`

```ini
DB_HOST=localhost
DB_PORT=5432
DB_NAME=chemical_park_ai_agent
DB_USERNAME=postgres
DB_PASSWORD=7758521
JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx
AUTH_ACCOUNTS_JSON=[{"Username":"admin","Password":"7758521","Role":"admin"},{"Username":"auditor","Password":"7758521","Role":"auditor"}]
```

#### appsettings.json LLM 配置（当前状态 - 待修改）

```json
{
  "Llm": {
    "ModelId": "qwen3:8b",
    "Endpoint": "http://localhost:11434",    // ← 需改为 llama.cpp server 端点
    "MultimodalModelId": "qwen-vl:latest",
    "FunctionCallingModelId": "qwen3:8b",
    "MaxRetries": 3,
    "RetryDelayMs": 1000
  },
  "VectorSearch": {
    "EmbeddingModelId": "nomic-embed-text:latest",
    "EmbeddingDimension": 768
  }
}
```

> **注意**：当前 `Endpoint` 指向 Ollama 默认端口 `11434`，启动时需改为 llama.cpp server 的 OpenAI 兼容端点 `http://localhost:8080/v1`。

### 3.6 模型文件上传至 AutoDL 服务器

两个模型文件在 Windows 本地下载完成后，通过 AutoDL 提供的 JupyterLab 网页界面上传至服务器。

#### 本地文件确认

两个模型下载完成后的本地路径与大小：

| 模型 | 本地路径 | 大小 | 用途 |
|------|----------|------|------|
| Qwen3-8B Q4_K_M | `D:\桌面\Qwen_Qwen3-8B-Q4_K_M.gguf` | 4.68 GB | LLM 推理模型 |
| nomic-embed-text F16 | `D:\桌面\nomic-embed-text-v1.5.f16.gguf` | 261.5 MB | 768 维嵌入模型 |

#### 上传步骤

```
1. 打开 AutoDL 网页 → 容器实例列表 → 找到实例 autodl-container-k5xt4fu27v-d60014d2
2. 点击右侧 "JupyterLab" 链接 → 浏览器新标签页打开 JupyterLab 界面
3. 在 JupyterLab 左侧文件浏览器中，点击上传按钮（↑ 图标）
4. 选择 Qwen_Qwen3-8B-Q4_K_M.gguf → 上传（约 5 分钟）
5. 再次点击上传按钮 → 选择 nomic-embed-text-v1.5.f16.gguf → 上传（约 1 分钟）
```

> ⚠️ **再次提醒**：JupyterLab 上传默认落盘到 `/`（根目录），而非 `/root/autodl-tmp/`。之前 Ollama 上传已踩过此坑，因此这次上传后直接执行定位命令。

#### 服务器端定位与移动

```bash
# 1. 全局搜索确认文件位置
find / -name "Qwen_Qwen3-8B-Q4_K_M.gguf" 2>/dev/null
# 输出：/Qwen_Qwen3-8B-Q4_K_M.gguf    ← 落在根目录

find / -name "nomic-embed-text-v1.5.f16.gguf" 2>/dev/null
# 输出：/nomic-embed-text-v1.5.f16.gguf  ← 同样落在根目录

# 2. 确保目标目录存在
mkdir -p /root/autodl-tmp/models

# 3. 移动到模型目录
mv /Qwen_Qwen3-8B-Q4_K_M.gguf /root/autodl-tmp/models/
mv /nomic-embed-text-v1.5.f16.gguf /root/autodl-tmp/models/

# 4. 验证文件大小和位置
ls -lh /root/autodl-tmp/models/
# 输出：
# -rw-r--r-- 1 root root 261.5M Jun 13 18:10 nomic-embed-text-v1.5.f16.gguf
# -rw-r--r-- 1 root root 4.68G  Jun 13 18:08 Qwen_Qwen3-8B-Q4_K_M.gguf
```

✅ 两个模型文件已成功部署到 `/root/autodl-tmp/models/`，与 llama.cpp 编译产物在同一工作目录下。

---

## 四、问题与解决方案汇总

### 4.1 完整问题清单（按时间排序）

| # | 时间 | 问题 | 原因 | 解决方案 |
|---|------|------|------|----------|
| 1 | 11:22 | dotnet-install.sh 下载 .NET SDK 卡死 | `builds.dotnet.microsoft.com` 国内访问超时 | 改用 APT 包管理器安装（Microsoft 官方源） |
| 2 | 11:27 | `apt install postgresql-16-pgvector` 找不到包 | Ubuntu 默认源不含 pgvector 扩展 | 导入 PostgreSQL GPG 密钥 + 添加 APT 官方源 |
| 3 | 11:31 | `sudo -u postgres` 报 command not found | AutoDL 精简容器不含 sudo | 用 `su - postgres -c` 替代 |
| 4 | 12:02 | PowerShell `%USERPROFILE%` 报找不到路径 | PowerShell 不支持 cmd 的 %VAR% 写法 | 改用 `$env:USERPROFILE` |
| 5 | 12:08 | `su - postgres psql -f` 报 Permission denied | postgres 用户无权限访问 /root/ 目录下的 .sql 文件 | `cp` 到 `/tmp/` 再执行 |
| 6 | 12:13 | `dotnet publish` 报 CS0246: IMemoryService/MemoryCoordinator 找不到 | `.gitignore` 中 `memory/` 规则误排除了 `Agent1/Services/Memory/` 目录 | 修改 .gitignore: `memory/` → `/memory/`（仅匹配根目录） |
| 7 | 12:42 | JupyterLab 上传文件找不到 | 上传默认落盘到 `/` 而非 `/root/autodl-tmp/` | `find / -name 'filename*' 2>/dev/null` 定位 |
| 8 | 12:46 | Ollama Segmentation fault | 预编译二进制与容器环境兼容性问题（非 glibc/CPU 问题） | 放弃 Ollama，转向 llama.cpp 原生编译 |
| 9 | 12:49 | 误判无 AVX2 指令集 | `grep -o 'avx2\?'` 正则 `?` 导致只匹配到 `avx` | 用 `grep flags /proc/cpuinfo \| head -1` 查看完整 flags 行 |
| 10 | 11:53~15:10 | GitHub Release 下载超时（容器内） | AutoDL 容器网络受限 | 本地下载 + JupyterLab 上传 |
| 11 | 12:02~15:14 | Windows curl GitHub 直连仅返回 9 字节 | GitHub 被国内网络阻断 | 换 hf-mirror.com + 本地 curl.exe 下载模型 |
| 12 | 12:03~15:16 | ghproxy/ghfast/moeyy 镜像全部 SSL 错误 | Windows schannel 与镜像服务器的 TLS 协商失败 | 换 hf-mirror.com |
| 13 | 15:56 | CMake CUDA 配置失败 | `CMAKE_CUDA_COMPILER-NOTFOUND`，CMake 无法自动发现 nvcc | 显式指定 `-DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc` |
| 14 | 16:02 | GGUF 模型多次 404（4 种不同 URL） | 文件名大小写/格式与 HF 仓库实际命名不一致 | WebSearch → 找到正确仓库名 `Qwen_Qwen3-8B-GGUF` |
| 15 | 16:04 | HF API 查询无返回 | hf-mirror 不代理 API，直连 HF 又超时 | 放弃 API，改用 WebSearch |
| 16 | 16:14 | wget 下载空文件（0 字节） | `wget -O` 目标目录 `/root/autodl-tmp/models/` 尚未创建 | `mkdir -p` 先创建目录 |
| 17 | 16:30 | psql 密码认证失败 | 非交互模式未提供密码 | `PGPASSWORD=7758521 psql ...` 环境变量传递 |
| 18 | 16:36 | appsettings.json 指向 Ollama 端点 | 配置默认为 `localhost:11434`（Ollama 端口） | 待改为 llama.cpp server 的 `localhost:8080/v1` |

### 4.2 问题分类统计

| 类别 | 数量 | 涉及问题 |
|------|------|----------|
| 容器环境约束 | 4 | #3 (sudo 不可用), #5 (postgres 文件权限), #7 (上传路径), #10 (网络限制) |
| 安装源/镜像 | 3 | #1 (.NET SDK 源), #2 (pgvector 源), #10/#11/#12 (GitHub 下载失败) |
| 二进制兼容性 | 1 | #8 (Ollama segfault) |
| 诊断工具误用 | 1 | #9 (grep 正则错误) |
| 代码/配置 | 2 | #6 (.gitignore), #18 (LLM 端点) |
| 编译配置 | 1 | #13 (nvcc 路径) |
| 模型获取 | 2 | #14 (404), #16 (wget 空文件) |
| 数据库配置 | 1 | #17 (psql 密码) |
| PowerShell 语法 | 1 | #4 (%USERPROFILE%) |
| 代理/网络 | 1 | #15 (HF API 不可用) |

### 4.3 经验法则提炼

| 法则 | 说明 |
|------|------|
| **"APT 优先于 curl 脚本"** | 国内环境安装 .NET SDK 优先用 APT 包管理器，dotnet-install.sh 直连微软 CDN 极易超时 |
| **"sudo 不可用时用 su"** | AutoDL 精简容器无 sudo，使用 `su - postgres -c "cmd"` 替代 |
| **"PowerShell 用 $env"** | PowerShell 环境变量写法为 `$env:VARNAME`，非 cmd 的 `%VARNAME%` |
| **".gitignore 防子目录误伤"** | 通用规则如 `memory/` 会匹配所有层级，改为 `/memory/` 仅匹配根目录 |
| **"上传先 find"** | JupyterLab 上传文件后，始终用 `find / -name` 定位，不猜测路径 |
| **"flags 看全行"** | 检查 CPU 指令集用 `grep flags /proc/cpuinfo \| head -1`，不用简化的 regex |
| **"下载走本地"** | AutoDL 容器网络严重受限，大文件优先 Windows 本地下载 + JupyterLab 上传 |
| **"CMake 全显式"** | CUDA 编译时必须同时指定 `GGML_CUDA=ON` + `CMAKE_CUDA_COMPILER` + `CMAKE_CUDA_ARCHITECTURES` |
| **"404 走 WebSearch"** | GGUF 文件名猜测极易出错，直接用搜索引擎查 `模型名 GGUF bartowski` |
| **"嵌入用 F16"** | 嵌入模型不建议量化，全精度（F16）才能保证向量检索准确率 |
| **"SQL 用 /tmp"** | postgres 用户无法访问 /root/ 路径，SQL 脚本需 cp 到 /tmp/ 再执行 |

---

## 五、经验教训

### 5.1 AutoDL 容器环境的核心约束

1. **不支持 Docker 部署**：宿主机已运行容器化环境，内部无法嵌套 Docker
2. **网络严重受限**：GitHub / HuggingFace 直连不可行，必须使用国内镜像或本地下载 + 上传
3. **JupyterLab 上传路径陷阱**：文件默认上传到 `/`，需 `find` 定位后 `mv`
4. **sudo 不可用**：部分操作需用 `su` 替代
5. **CUDA Toolkit 路径非标准**：存在多个版本（cuda-12、cuda-12.8），CMake 需显式指定
6. **PostgreSQL 被禁止自动启动**：`invoke-rc.d` 的 `policy-rc.d` 拦截了服务自动启动，需手动 `pg_ctlcluster`
7. **/root 路径对其他用户不可见**：postgres 等系统用户无法访问 /root/ 下的文件，需复制到 /tmp/
8. **容器资源动态分配**：初始登录显示 90GB RAM，实际运行时可达 755GB

### 5.2 .NET 8 SDK 安装

- **国内环境**：`dotnet-install.sh` 直连 `builds.dotnet.microsoft.com` CDN 极易超时
- **推荐方式**：通过 APT 包管理器，添加微软官方 APT 源（`packages.microsoft.com`）
- 安装包版本：dotnet-sdk-8.0 (8.0.128)、aspnetcore-runtime-8.0 (8.0.28)

### 5.3 .gitignore 规则注意

- `memory/` 会匹配任意深度的 `memory/` 目录，包括 `Agent1/Services/Memory/`
- 如果只希望排除仓库根目录的 `memory/`，应写为 `/memory/`
- git add 时被忽略的目录不会给出任何警告，需 `git add -f` 或修复 .gitignore

### 5.4 llama.cpp 编译要点

- **CPU 编译**：默认 `cmake -B build` 即可，`-j$(nproc)` 充分利用多核
- **CUDA 编译**：必须同时指定 `GGML_CUDA=ON` + `CMAKE_CUDA_COMPILER` + `CMAKE_CUDA_ARCHITECTURES`
- **RTX 3090** 对应 `CMAKE_CUDA_ARCHITECTURES="86"`（sm_86）
- 编译产物中 `llama-server` 是主要服务端，支持 OpenAI 兼容 API

### 5.5 GGUF 模型获取

- Qwen3-8B-Instruct 的 GGUF 量化版在 HF 上的正确仓库：`bartowski/Qwen_Qwen3-8B-GGUF`
- Q4_K_M 量化（~4.7GB）适合 24GB VRAM，推理质量/速度均衡
- hf-mirror.com 是国内下载 HF 模型的可靠镜像
- 确定正确文件名的最可靠方法：通过 HF API 获取 `rfilename` 字段

### 5.6 PostgreSQL + pgvector

- pgvector 扩展已安装在服务器 PostgreSQL 中
- 向量维度 768，使用 HNSW 索引（对应 nomic-embed-text 嵌入模型）
- 非交互式 psql 需 `PGPASSWORD` 环境变量传密码

---

## 六、当前状态总览

### 已完成 ✅

| 组件 | 时间 | 状态 | 说明 |
|------|------|------|------|
| .NET 8 SDK | 11:25 | ✅ | APT 安装 8.0.128 |
| PostgreSQL 16 + pgvector | 11:33 | ✅ | 数据库 chemical_park_ai_agent 就绪，pgvector 扩展已启用 |
| 代码拉取（gitee） | 11:55 | ✅ | agent-system 项目完整拉取 |
| .gitignore 修复 | 12:18 | ✅ | `memory/` → `/memory/`，Memory 模块成功推送 |
| .NET 项目编译 | 12:20 | ✅ | `dotnet build` 0 Error（仅 nullable 警告） |
| 数据库初始化 | 12:22 | ✅ | 6 张表初始化成功，pgvector HNSW 索引就绪 |
| Ollama 下载（本地） | 12:25 | ✅ | 1.54GB ollama-linux-amd64.tgz |
| Ollama 上传安装 | 12:45 | ✅ | 解压成功，但 ollama --version segfault ❌ |
| Ollama 诊断 | 12:47-13:00 | ✅ | 确认非 glibc/CPU 问题，系二进制兼容性 |
| llama.cpp CPU 编译 | 15:48 | ✅ | 首次 CPU 版编译成功 |
| llama.cpp CUDA 编译 | 16:18 | ✅ | GPU 加速版编译成功，支持 RTX 3090 sm_86 |
| Qwen3-8B 模型下载 | 16:55 | ✅ | 4.68GB，100% 完成，路径 `D:\桌面\Qwen_Qwen3-8B-Q4_K_M.gguf` |
| nomic-embed-text 模型下载 | 17:30 | ✅ | 261.5MB，3 分 36 秒完成，路径 `D:\桌面\nomic-embed-text-v1.5.f16.gguf` |
| Qwen3-8B 模型上传 | ~18:08 | ✅ | JupyterLab 上传完成，已移至 `/root/autodl-tmp/models/` |
| nomic-embed-text 模型上传 | ~18:10 | ✅ | JupyterLab 上传完成，已移至 `/root/autodl-tmp/models/` |
| .env 环境变量 | 12:20 | ✅ | 数据库连接、JWT、账号已配置 |
| appsettings.json 分析 | 16:36 | ✅ | 已确认需改 LLM Endpoint 和 ModelId |
| 部署文档 | 18:20 | ✅ | v2.1 已完成，覆盖 9:30-18:15 全流程（6 个阶段、18 个问题、11 条经验法则） |

### 待执行（下一步立即操作） ⏳

| 步骤 | 操作 | 命令 / 说明 |
|------|------|-------------|
| ~~1~~ | ~~上传两个模型~~ | ✅ 已完成：JupyterLab 上传 `Qwen_Qwen3-8B-Q4_K_M.gguf`（4.68GB）和 `nomic-embed-text-v1.5.f16.gguf`（261.5MB）到 AutoDL |
| ~~2~~ | ~~移动模型文件~~ | ✅ 已完成：`mv /Qwen_Qwen3-8B-Q4_K_M.gguf /root/autodl-tmp/models/` 和 `mv /nomic-embed-text-v1.5.f16.gguf /root/autodl-tmp/models/` |
| 3 | 启动 llama-server（LLM） | `nohup llama-server -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 &` |
| 4 | 启动 llama-server（Embedding） | `nohup llama-server -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf --host 0.0.0.0 --port 8081 --embeddings -ngl 0 &` |
| 5 | 修改 appsettings.json | 改 `Endpoint` → `http://localhost:8080/v1`，改 `ModelId` → `Qwen_Qwen3-8B-Q4_K_M`，改 `EmbeddingModelId` → `nomic-embed-text-v1.5.f16` |
| 6 | 启动 Agent1 API | `export DOTNET_ENVIRONMENT=Production; dotnet run --project Agent1.Api` |
| 7 | 验证服务 | `curl http://localhost:5000/health` |

### 待解决 ⚠️

| 事项 | 优先级 | 说明 |
|------|--------|------|
| 知识库导入 | 高 | `knowledgebase/` 目录中的法规文档需要在服务启动后进行向量化和入库 |
| Function Calling 兼容性 | 中 | Qwen3-8B 通过 llama.cpp OpenAI 兼容 API 支持 tool calling，需实测验证 |
| 嵌入端点独立配置 | 中 | llama.cpp server 的 `/v1/embeddings` 端点需与 LLM 端点分离 |
| 服务持久化 | 中 | 需配置 systemd 或 nohup 确保服务在终端关闭后持续运行 |
| AutoDL 端口映射 | 低 | 外部访问需通过 AutoDL 的 SSH 隧道或自定义端口映射 |

---

## 七、启动 llama-server 参考命令

两个模型已上传至 `/root/autodl-tmp/models/`，以下是完整的启动命令：

### LLM 推理服务（端口 8080）

```bash
# GPU 推理（-ngl 99 表示所有层加载到 GPU）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 \
  --port 8080 \
  -ngl 99 \
  -c 8192 \
  > /root/autodl-tmp/logs/llama-server.log 2>&1 &

# 验证 LLM 服务
curl http://localhost:8080/v1/models
```

### 嵌入服务（端口 8081）

```bash
# CPU 推理（嵌入模型体积小，无需 GPU，-ngl 0）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 \
  --port 8081 \
  --embeddings \
  -ngl 0 \
  -c 512 \
  > /root/autodl-tmp/logs/llama-embed.log 2>&1 &

# 验证嵌入服务
curl http://localhost:8081/v1/embeddings \
  -H "Content-Type: application/json" \
  -d '{"input": "测试文本", "model": "nomic-embed-text-v1.5.f16"}'
```

### 参数说明

| 参数 | LLM 值 | Embedding 值 | 说明 |
|------|--------|-------------|------|
| `-m` | `...Qwen_Qwen3-8B-Q4_K_M.gguf` | `...nomic-embed-text-v1.5.f16.gguf` | GGUF 格式模型文件路径 |
| `--host` | `0.0.0.0` | `0.0.0.0` | 监听所有网络接口 |
| `--port` | `8080` | `8081` | 服务端口 |
| `-ngl` | `99`（全 GPU） | `0`（纯 CPU） | GPU 加载层数 |
| `-c` | `8192` | `512` | 上下文窗口大小 |
| `--embeddings` | — | ✅ 启用 | 启用嵌入 API 端点 |

---

> **文档版本**：v2.1（完整版，覆盖 9:30-18:15 全流程）  
> **最后更新**：2026-06-13 18:20  
> **下次任务**：启动 llama-server（LLM + Embedding）→ 修改 appsettings.json → 启动 Agent1 API → 验证服务
