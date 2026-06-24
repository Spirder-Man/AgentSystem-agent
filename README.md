

# Agent1 — 化工园区危化品合规审查 AI Agent

基于 .NET 8 + Semantic Kernel + **llama.cpp 原生编译**构建的企业级化工园区危化品合规审查 AI Agent。

> 整体完成度：~95% | 编译：0 错误 | C# 文件：~105 个 / ~15,500 行 | 测试：148 通过

## ✨ 核心功能

- **AI 推理引擎**：SK Auto FC + 断路器 + GPU 嵌入
- **化工合规工具**：8 个 KernelFunction + 三层降级
- **知识库**：BM25+Vector+RRF+增量更新
- **化工业务模块**：合规自查/工单/监管/应急/图谱
- **基础设施**：SHA256 审计链/安全双防线/健康检查
- **可观测性**：PipelineMetrics/TraceId/事件溯源
- **API 服务**：JWT 认证/限流/OTel/优雅关闭
- **12 ModuleType + 20 菜单** — 全部实现

## 🛠️ 技术栈

| 层级 | 技术 | 版本 |
|------|------|------|
| 语言/框架 | C# / .NET | 12.0 / 8.0 |
| AI 框架 | Semantic Kernel | 1.74.0 |
| 推理引擎 | llama.cpp (llama-server) | b4857 |
| 推理模型 | Qwen3-8B (Q4_K_M GGUF) | 8B |
| 嵌入模型 | nomic-embed-text-v1.5 (F16 GGUF) | latest |
| 精排模型 | bge-reranker-v2-m3 | Python sidecar |
| 数据库 | PostgreSQL + pgvector | 16.x |
| 认证 | JWT Bearer + BCrypt | 8.0+ |
| 可观测性 | Prometheus + Grafana + OpenTelemetry | latest |

## 🚀 快速开始

### Linux 生产环境（RTX 3080 Ti / 3090）

```bash
# 1. 安装 .NET 8 SDK
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
apt update && apt install -y dotnet-sdk-8.0

# 2. 安装 PostgreSQL 16 + pgvector
curl -fsSL https://www.postgresql.org/media/keys/ACCC4CF8.asc | gpg --dearmor -o /usr/share/keyrings/postgresql.gpg
echo "deb [signed-by=/usr/share/keyrings/postgresql.gpg] http://apt.postgresql.org/pub/repos/apt jammy-pgdg main" | tee /etc/apt/sources.list.d/pgdg.list
apt update && apt install -y postgresql-16 postgresql-16-pgvector
pg_ctlcluster 16 main start
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD 'your_password';\""
su - postgres -c "psql -c \"CREATE DATABASE chemical_park_ai_agent;\""

# 3. 编译 llama.cpp (CUDA GPU 版)
git clone https://gitclone.com/github.com/ggerganov/llama.cpp.git
cd llama.cpp
cmake -B build -DGGML_CUDA=ON -DCMAKE_CUDA_COMPILER=/usr/local/cuda/bin/nvcc
cmake --build build --config Release -j$(nproc)

# 4. 下载 GGUF 模型
mkdir -p /models
# LLM: Qwen_Qwen3-8B-Q4_K_M.gguf (~4.7GB)
# Embed: nomic-embed-text-v1.5.f16.gguf (~274MB)

# 5. 初始化数据库
psql -U postgres -d chemical_park_ai_agent -f init_database.sql

# 6. 启动 llama-server
nohup /path/to/llama.cpp/build/bin/llama-server \
  -m /models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /logs/llama-server.log 2>&1 &

# 7. 启动 API
cd /path/to/agent-system
DOTNET_ENVIRONMENT=Production JWT_KEY=your_32char_key DB_PASSWORD=your_password \
dotnet run --project Agent1.Api
```

### Docker 部署

```powershell
docker compose up -d
# 访问 http://localhost:5000
```

## 📡 API 端点

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| POST | /api/auth/login | 登录获取 Token | 否 |
| POST | /api/auth/refresh | 刷新 Token | Bearer |
| POST | /api/compliance/hazard/query | 危化品危险类别查询 | Bearer |
| POST | /api/compliance/storage/check | 储存兼容性检查 | Bearer |
| POST | /api/compliance/check | 合规综合检查 | Bearer |
| GET | /health | 全量健康检查 | 否 |
| GET | /metrics | Prometheus 指标 | 否 |

调用示例：
```bash
# 登录
TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"your_password"}' | jq -r '.token')

# 查询危化品
curl -X POST http://localhost:5000/api/compliance/hazard/query \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"substanceName":"苯"}'
```

## 🔐 生产环境安全配置

环境变量（必须设置）：

| 变量 | 说明 |
|------|------|
| `DB_PASSWORD` | PostgreSQL 密码 |
| `JWT_KEY` | JWT 签名密钥（≥32字符） |
| `AUTH_ACCOUNTS_JSON` | 账号列表 JSON |

## 📊 可观测性

```
http://localhost:5000/metrics     # Prometheus 指标
http://localhost:5000/health      # 健康检查
http://localhost:3000            # Grafana
```

## 📝 许可证

MIT License

---

**版本**：v4.5  
**最后更新**：2026年6月24日