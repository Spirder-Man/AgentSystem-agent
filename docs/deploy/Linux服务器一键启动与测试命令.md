# Agent1 Linux 服务器一键启动与测试命令

> **适用环境**：RTX 3090 24GB Linux（AutoDL 容器 / 裸金属）  
> **分支**：`linux原生编译模型llama.cpp`  
> **最后更新**：2026-06-13

---

## 一、环境概览

| 组件 | 端口 | 模型 | 显存 |
|------|------|------|------|
| LLM 推理 | 8080 | Qwen3-8B Q4_K_M, -ngl 99, -c 8192 | ~5.0 GB |
| Embedding | 8081 | nomic-embed-text-v1.5 F16, -ngl 99, -c 2048, --batch-size 512 | ~1.0 GB |
| Reranker（可选） | 8082 | bge-reranker-v2-m3 Python sidecar | ~1.5 GB |
| PostgreSQL | 5432 | pgvector | 内存 |
| 项目路径 | — | `/root/autodl-tmp/agent-system` | — |
| 日志路径 | — | `/root/autodl-tmp/logs/` | — |

---

## 二、数据库启动与管理

### 2.1 启动 PostgreSQL

```bash
service postgresql start
```

### 2.2 验证数据库状态

```bash
# 检查端口是否监听
pg_isready

# socket 免密连接验证
su - postgres -c "psql -d chemical_park_ai_agent -c 'SELECT 1'"
```

### 2.3 密码认证问题修复

如果 .NET 程序报 `password authentication failed`：

```bash
# 重置 postgres 密码
su - postgres -c "psql -c \"ALTER USER postgres PASSWORD '7758521'\""

# 或者改 pg_hba.conf 为 trust 模式（开发环境）
# 找到 host all all 127.0.0.1/32 那行，把 scram-sha-256 改为 trust
# 然后重启
service postgresql restart
```

### 2.4 首次初始化数据库

```bash
su - postgres -c "psql -f /root/autodl-tmp/agent-system/init_database.sql"
```

---

## 三、AI 推理服务启动

### 3.1 完整一键启动

```bash
# 创建日志目录
mkdir -p /root/autodl-tmp/logs

# 1. LLM 推理服务（端口 8080）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf \
  --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 \
  > /root/autodl-tmp/logs/llama-server.log 2>&1 &

# 2. Embedding 嵌入服务（端口 8081，GPU 加速）
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server \
  -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf \
  --host 0.0.0.0 --port 8081 --embeddings \
  -ngl 99 -c 2048 --batch-size 512 \
  > /root/autodl-tmp/logs/llama-embed.log 2>&1 &

# 3. 等待模型加载
sleep 5

# 4. 健康检查
echo "=== LLM 推理服务 ===" && curl -s http://localhost:8080/health
echo "=== Embedding 服务 ===" && curl -s http://localhost:8081/health
```

### 3.2 参数说明

| 参数 | LLM 服务 | Embedding 服务 | 说明 |
|------|---------|---------------|------|
| `-ngl` | 99 | 99 | GPU 卸载层数（99 = 全部 GPU） |
| `-c` | 8192 | 2048 | 上下文长度（tokens） |
| `--batch-size` | — | 512 | 批处理大小（物理限制） |
| `--embeddings` | — | ✅ | 启用嵌入模式 |
| `--host` | 0.0.0.0 | 0.0.0.0 | 监听所有网卡 |

---

## 四、服务状态检查

### 4.1 所有服务健康检查

```bash
# 一行检查全部服务
echo "=== PostgreSQL ===" && pg_isready; \
echo "=== LLM(8080) ===" && curl -s -o /dev/null -w "%{http_code}" http://localhost:8080/health && echo ""; \
echo "=== Embedding(8081) ===" && curl -s -o /dev/null -w "%{http_code}" http://localhost:8081/health && echo ""
```

期望输出：
```
=== PostgreSQL ===
/var/run/postgresql:5432 - accepting connections
=== LLM(8080) ===
200
=== Embedding(8081) ===
200
```

### 4.2 检查服务是否存活

```bash
# 检查进程
ps aux | grep llama-server

# 查看最新日志
tail -20 /root/autodl-tmp/logs/llama-server.log
tail -20 /root/autodl-tmp/logs/llama-embed.log
```

### 4.3 验证 GPU 嵌入是否正常

```bash
# 发一条嵌入请求，应返回 768 维向量
curl -s http://localhost:8081/v1/embeddings \
  -H "Content-Type: application/json" \
  -d '{"input":"苯的储存安全距离","model":"nomic-embed-text"}' \
  | python3 -c "import sys,json; d=json.load(sys.stdin); print(f'维度: {len(d[\"data\"][0][\"embedding\"])}')"
```

期望输出：`维度: 768`

### 4.4 验证 LLM 推理是否正常

```bash
curl -s http://localhost:8080/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{"model":"qwen","messages":[{"role":"user","content":"回复OK即可"}],"max_tokens":5}' \
  | python3 -c "import sys,json; print(json.load(sys.stdin)['choices'][0]['message']['content'])"
```

---

## 五、拉取代码与编译

### 5.1 拉取最新 GPU 加速代码

```bash
cd /root/autodl-tmp/agent-system

# 如果有本地改动，先 stash
git stash

# 切换分支并拉取
git checkout linux原生编译模型llama.cpp
git pull origin linux原生编译模型llama.cpp

# 恢复本地配置改动
git stash pop 2>/dev/null || true
```

### 5.2 编译

```bash
dotnet build Agent1/Agent1.csproj -c Release
```

期望输出：`Build succeeded. 0 Warning(s) 0 Error(s)`

---

## 六、功能测试（控制台菜单）

### 6.1 启动控制台程序

```bash
cd /root/autodl-tmp/agent-system
DOTNET_ENVIRONMENT=Production \
JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx \
DB_PASSWORD=7758521 \
dotnet run --project Agent1
```

> ⚠️ `DB_PASSWORD` 必须匹配 PostgreSQL 实际密码。`JWT_KEY` 至少 32 字符。

### 6.2 菜单选项说明与测试

启动后输入对应数字回车。以下为推荐测试顺序：

#### 选项 10 — 数据库连接验证

```
请输入选项: 10
```

**验证**：输出数据库表列表、pgvector 扩展状态。确保 PostgreSQL 连接正常。

#### 选项 12 — 工具调用诊断验证

```
请输入选项: 12
```

**验证**：输入化学品名称（如 `苯`），确认 7 个 KernelFunction 工具能正确调用 LLM 驱动执行。

#### 选项 8 — 化工合规自查（核心功能）

```
请输入选项: 8
```

**测试输入**：
- `苯和丙酮可以同库储存吗？`
- `液氯储罐距离居民区的安全距离是多少？`
- `过氧化氢的危险类别是什么？`

**验证**：
- LLM 是否调用了 `CheckStorageCompatibility` / `GetSafetyDistance` / `CheckHazardCategory`
- 结论是否附带 `[REGULATIONS: ...]` 标签
- 回答末尾有 `[判定:is_compliant=...]` 标签

#### 选项 7 — 智能对话系统

```
请输入选项: 7
```

**测试输入**：`查询苯的CAS号和闪点`

**验证**：确认调用了 `LookupChemicalProperties` 工具，返回结构化属性数据。

#### 选项 9 — 化工合规 RAG 测试

```
请输入选项: 9
```

**验证**：输入法规相关查询（如 `GB 50160 规定的甲类仓库防火间距`），确认 RAG 检索返回知识库原文，且 GPU 嵌入延迟约 30ms。

#### 选项 6 — RAG 检索增强生成

```
请输入选项: 6
```

**验证**：CoT + RAG 联合推理，输入 `氰化钠的重大危险源临界量是多少？`，确认引用了 GB 18218。

#### 选项 5 — Reflection 自我反思

```
请输入选项: 5
```

**验证**：输入合规问题，程序先 CoT 推理，然后 `ReflectionVerifier` 对法规编号做事实核查，最后 LLM 基于核查报告修正结论。

#### 选项 1-4 — 思维链 / ReAct 推理

```
请输入选项: 1  (CoT 标准)
请输入选项: 2  (CoT 流式)
请输入选项: 3  (ReAct 标准)
请输入选项: 4  (ReAct 流式)
```

**验证**：各推理范式走完整 RAG 检索 + 工具调用链。流式模式可观察 token 逐步输出。

#### 选项 11 — 切换检索模式

```
请输入选项: 11
```

可选：`BM25`（纯关键词）/ `Vector`（纯向量）/ `Hybrid`（混合）。切换后验证对应检索方式是否生效。

#### 选项 13 — 合规评测集（GPU 加速核心验证）⭐

```
请输入选项: 13
```

**这是验证 GPU 加速效果的关键步骤。** 跑完 64 条评测后自动输出报告，重点关注：

```
📊 GPU 加速指标
─────────────────────────────
GPU 嵌入延迟(均值):     ~30ms     ← 目标 <50ms
GPU 检索延迟(均值):     ~8ms      ← 目标 <10ms
Reranker 延迟(均值):    ~25ms     ← 目标 <30ms
VRAM 使用量:            ~9.8 GB   ← 目标 <14GB
查询缓存命中率:         XX%

📊 检索质量指标
─────────────────────────────
Precision@K:    XX%              ← 目标 55%+
Recall@K:       XX%              ← 目标 60%+
MRR:            XX               ← 目标 0.60+
```

> ⏱️ 评测预计耗时：GPU 模式下约 3-5 分钟 / CPU 模式下约 10-15 分钟

---

## 七、API 服务测试（可选）

如果需要通过 Web API 测试：

```bash
# 启动 API 服务（另开终端或 nohup）
nohup dotnet run --project Agent1.Api \
  --environment Production \
  > /root/autodl-tmp/logs/agent1-api.log 2>&1 &

# 健康检查
curl http://localhost:5000/health/live

# 登录获取 Token
curl -s -X POST http://localhost:5000/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"7758521"}' | python3 -m json.tool

# 储存兼容性检查（替换 <token>）
curl -s -X POST http://localhost:5000/api/compliance/storage/check \
  -H 'Authorization: Bearer <token>' \
  -H 'Content-Type: application/json' \
  -d '{"substance1":"苯","substance2":"丙酮"}' | python3 -m json.tool
```

---

## 八、故障排查速查表

| 症状 | 诊断命令 | 常见原因与修复 |
|------|---------|--------------|
| LLM 服务 404 | `curl http://localhost:8080/health` | 服务没启动或端口冲突，`kill` 旧进程后重启 |
| Embedding 404 | `curl http://localhost:8081/health` | 同上 |
| 数据库连不上 | `pg_isready` | `service postgresql start`；密码问题改 `pg_hba.conf` 为 `trust` |
| 编译失败 | `dotnet build Agent1/Agent1.csproj` | 代码未更新：`git stash && git pull` |
| GPU 加速未生效 | 看评测报告的嵌入延迟 | 检查 `-ngl 99` 参数是否带上，`nvidia-smi` 看显存占用 |
| Reranker 报错 | 评测报告显示 fallback | 正常行为—Reranker 服务(8082)未启动时自动降级，不影响评测 |
| 评测超时 | 单条 >5min | 检查 LLM 服务 `-c 8192` 是否太小，或嵌入服务 `--batch-size 512` 是否需要调大 |

### 8.1 快速重启全部服务

```bash
# 杀掉旧进程
pkill -f llama-server

# 重新启动（按第三章步骤）
service postgresql restart
# ... 然后重新启动两个 llama-server
```

### 8.2 查看 GPU 显存使用

```bash
nvidia-smi --query-gpu=memory.used,memory.total --format=csv
```

---

## 九、快速参考卡片

```bash
# ═══════ 完整启动流程 ═══════
service postgresql start                                    # 1. 启动数据库
cd /root/autodl-tmp/agent-system                            # 2. 进入项目
git checkout linux原生编译模型llama.cpp && git pull          # 3. 拉代码

# 4. 启动 AI 服务
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server -m /root/autodl-tmp/models/Qwen_Qwen3-8B-Q4_K_M.gguf --host 0.0.0.0 --port 8080 -ngl 99 -c 8192 > /root/autodl-tmp/logs/llama-server.log 2>&1 &
nohup /root/autodl-tmp/llama.cpp/build/bin/llama-server -m /root/autodl-tmp/models/nomic-embed-text-v1.5.f16.gguf --host 0.0.0.0 --port 8081 --embeddings -ngl 99 -c 2048 --batch-size 512 > /root/autodl-tmp/logs/llama-embed.log 2>&1 &
sleep 5

# 5. 编译运行
dotnet build Agent1/Agent1.csproj -c Release && \
DOTNET_ENVIRONMENT=Production JWT_KEY=qazwsxedcrfvtgbyhnujmikolpqazwsx DB_PASSWORD=7758521 dotnet run --project Agent1
```
