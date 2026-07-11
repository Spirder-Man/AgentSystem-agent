# Docker 容器化一键部署

---

### 准备工作

```powershell
# ① 进入项目目录
cd d:\桌面\agent\项目\Agent1

# ② 确认 Docker 在运行（如果没反应，手动打开 Docker Desktop 再执行）
docker version

# ③ 确认 .env 存在
ls .env

# ④ 如果不存在，创建一个
copy .env.example .env
```

### 第一步：拉取基础镜像（不需要 GPU 编译的）

```powershell
docker compose pull postgres
docker compose pull prometheus
docker compose pull grafana
```

### 第二步：构建 llama.cpp CUDA 编译镜像（首次约 10~30 分钟）

```powershell
docker compose build llama-server llama-embed
```

> 这一步会克隆 llama.cpp 源码 + CUDA 编译，完整日志可见。Windows 上 GPU 不可用但镜像兼容 Linux。

### 第三步：构建 API 镜像

```powershell
docker compose build api
```

> 这一步会 `dotnet restore` → `dotnet publish`，约 2~3 分钟。

### 第四步：启动全部 6 个服务

```powershell
docker compose up -d
```

### 第五步：实时看日志

```powershell
docker compose logs -f
```

`Ctrl+C` 退出日志。

### 第六步：验证

```powershell
# 服务状态
docker compose ps

# API 健康检查
curl http://localhost:5000/health

# Swagger 文档
start http://localhost:5000/swagger

# Prometheus
start http://localhost:9090

# Grafana（admin / agent1-admin）
start http://localhost:3000
```

---

### 常用后续命令

```powershell
docker compose logs -f api            # 只看 API 日志
docker compose restart api            # 重启 API
docker compose down                   # 停止全部（保留数据卷）
docker compose down -v                # 停止 + 清除所有数据
```

---

### Windows 注意事项

| 问题                | 说明                                                         |
| ------------------- | ------------------------------------------------------------ |
| llama-server 启动慢 | 首次需要编译 CUDA 镜像，CPU 推理模式（Windows 无 GPU 直通），启动后日志会显示 `llama_model_load: loaded meta data` |
| 模型需要手动放      | `models/` 目录下放 `qwen3-8b-q4_k_m.gguf` 和 `nomic-embed-text-v1.5.Q8_0.gguf`，否则 llama-server 启动失败 |
| 没有模型文件        | 可以先跳过 llama-server/llama-embed，只启动 `docker compose up -d postgres api prometheus grafana`，API Mock 测试仍可用 |

从第二步开始跑，有问题随时贴日志。
