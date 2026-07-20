# Linux 新机原生部署一条龙

> 适用场景：全新 AutoDL 容器（或任何 Ubuntu 22.04 + NVIDIA GPU 环境），从零到服务全启动。

## 当前容器情况确认

| 已有                  | 缺少                          |
| --------------------- | ----------------------------- |
| CUDA 12.4 + nvcc ✅    | .NET 8 SDK                    |
| RTX 3080 Ti (sm_86) ✅ | PostgreSQL + pgvector         |
| 45GB 内存 ✅           | llama.cpp（需编译）           |
|                       | Qwen3 GGUF + nomic-embed GGUF |

---

## 按 docs 经验，分步执行

### 第 1 步：安装 .NET 8 SDK

```bash
# 用 APT 装（不要用 dotnet-install.sh，国内太慢）
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update
apt install -y dotnet-sdk-8.0
dotnet --version   # 验证: 应为 8.0.xxx
```

### 第 2 步：安装 PostgreSQL 16 + pgvector

```bash
# 导入密钥 + 添加源
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /usr/share/keyrings/postgresql.gpg --yes
echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] http://apt.postgresql.org/pub/repos/apt jammy-pgdg main" | tee /etc/apt/sources.list.d/pgdg.list
apt update
apt install -y postgresql-16 postgresql-client-16 postgresql-16-pgvector

# 手动启动（容器禁止自动启动）
pg_ctlcluster 16 main start

# 设置密码 + 建库（用 su 替代 sudo）
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD '7758521';\""
su - postgres -c "psql -c \"CREATE DATABASE chemical_park_ai_agent;\""
su - postgres -c "psql -d chemical_park_ai_agent -c \"CREATE EXTENSION IF NOT EXISTS vector;\""
```

### 第 3 步：编译 llama.cpp（CUDA GPU 版）

```bash
cd /root/autodl-tmp
rm -rf llama.cpp
git clone https://gitclone.com/github.com/ggerganov/llama.cpp.git
cd llama.cpp

# CUDA 编译（3080 Ti = sm_86，必须显式指定 nvcc 路径）
cmake -B build \
  -DGGML_CUDA=ON \
  -DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc \
  -DCMAKE_CUDA_ARCHITECTURES="86"

cmake --build build --config Release -j$(nproc)

# 验证
ls -lh build/bin/llama-server
```

### 第 4 步：下载 GGUF 模型文件

**两个途径：**

**A. 直接在容器下载（如果 hf-mirror 可通）**：
```bash
mkdir -p /root/autodl-tmp/models

# LLM 模型（4.68GB）
wget -O /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  https://hf-mirror.com/bartowski/Qwen_Qwen3-8B-GGUF/resolve/main/Qwen_Qwen3-8B-Q4_K_M.gguf

# 嵌入模型（274MB）
wget -O /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  https://hf-mirror.com/nomic-ai/nomic-embed-text-v1.5-GGUF/resolve/main/nomic-embed-text-v1.5.f16.gguf
```

**B. 如果容器网不通，走 JupyterLab 上传**（docs 里的经验）：
- Windows 本地 curl 下载两个文件
- JupyterLab 网页上传（落盘到 `/` 根目录）  
- `find / -name "*.gguf"` 定位 → `mv` 到 `/root/autodl-tmp/models/`

### 第 5 步：克隆代码 + 初始化数据库

```bash
cd /root/autodl-tmp
git clone https://gitee.com/liuchao_yue/agent-system.git
cd agent-system

# 初始化数据库
cp init_database.sql /tmp/
PGPASSWORD=7758521 psql -h localhost -U postgres -f /tmp/init_database.sql
```

### 第 6 步：修改配置

编辑 `Agent1/appsettings.json`，把 LLM 端点从 Ollama 改为 llama-server：

```
"Llm": {
    "Endpoint": "http://localhost:8080/v1"
},
"VectorSearch": {
    "EmbeddingEndpoint": "http://localhost:8081/v1"
}
```

### 第 7 步：启动 AI 推理服务

```bash
mkdir -p /root/autodl-tmp/logs

# LLM 推理服务 (8080)
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /root/autodl-tmp/logs/llama-server.log 2>&1 &

# Embedding 服务 (8081)
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings -ngl 99 -c 2048 --batch-size 512 \
  > /root/autodl-tmp/logs/llama-embed.log 2>&1 &

sleep 5
curl http://localhost:8080/health    # 应返回 200
curl http://localhost:8081/health    # 应返回 200
```

### 第 8 步：编译并启动

```bash
cd /root/autodl-tmp/agent-system
dotnet build Agent1/Agent1.csproj -c Release

DOTNET_ENVIRONMENT=Production \
JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx \
DB_PASSWORD=7758521 \
dotnet run --project Agent1
```

---

先跑第 1-3 步看环境是否就绪，有问题随时说。
