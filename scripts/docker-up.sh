#!/bin/bash
# ═══════════════════════════════════════════════════════════
# Agent1 一键容器化部署脚本
# 启动: PostgreSQL + Ollama(LLM+Embedding) + API + Prometheus + Grafana
# ═══════════════════════════════════════════════════════════
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

cd "$PROJECT_DIR"

echo "════════════════════════════════════════"
echo "  Agent1 容器化部署"
echo "  $(date '+%Y-%m-%d %H:%M:%S')"
echo "════════════════════════════════════════"

# 1. 检查 .env 文件
if [ ! -f ".env" ]; then
    echo ""
    echo "⚠️  未找到 .env 文件，从 .env.example 复制..."
    cp .env.example .env
    echo "✅ .env 已创建，请编辑填入生产密码后重新执行"
    echo "   必填项: DB_PASSWORD / JWT_KEY / ALERT_EMAIL_PASSWORD"
    exit 1
fi

# 2. 加载环境变量
echo ""
echo "📋 加载环境变量..."
set -a; source .env; set +a

# 3. 检查 Docker
echo "🐳 检查 Docker..."
if ! docker info > /dev/null 2>&1; then
    echo "❌ Docker 未运行，请先启动 Docker"
    exit 1
fi
echo "   Docker ✅"

# 4. 检查 GPU（可选）
if command -v nvidia-smi > /dev/null 2>&1; then
    GPU_COUNT=$(nvidia-smi --query-gpu=name --format=csv,noheader 2>/dev/null | wc -l)
    if [ "$GPU_COUNT" -gt 0 ]; then
        echo "   GPU: $GPU_COUNT 卡检测到 → 可启用 GPU 加速"
        echo "   提示: 取消 docker-compose.yml 中 ollama 的 deploy 段注释以启用 GPU"
    fi
fi

# 5. 拉取镜像 + 构建
echo ""
echo "📦 拉取基础镜像..."
docker compose pull postgres 2>/dev/null || true

echo "🔨 构建 llama.cpp CUDA 镜像 (首次约10分钟)..."
docker compose build llama-server llama-embed

echo "🔨 构建 API 镜像..."
docker compose build api

# 6. 启动全部服务
echo ""
echo "🚀 启动全部服务..."
docker compose up -d

# 7. 等待健康检查通过
echo ""
echo "⏳ 等待服务就绪..."
echo "   PostgreSQL..."
until docker compose exec -T postgres pg_isready -U postgres 2>/dev/null; do sleep 2; done

echo "   llama-server (LLM, 等待模型加载)..."
echo "   llama-embed (Embedding, 等待模型加载)..."

echo "   API (等待 health check)..."
for i in $(seq 1 30); do
    if curl -s -o /dev/null http://localhost:${API_PORT:-8080}/health/live 2>/dev/null; then
        echo "   API ✅"
        break
    fi
    sleep 3
done

# 8. 打印状态
echo ""
echo "════════════════════════════════════════"
echo "  Agent1 容器化部署完成 ✅"
echo "════════════════════════════════════════"
echo ""
echo "  服务入口:"
echo "  ┌─────────────────────────────────────"
echo "  │ API (Swagger):  http://localhost:${API_PORT:-8080}/swagger"
echo "  │ API (Health):   http://localhost:${API_PORT:-8080}/health"
echo "  │ API (Metrics):  http://localhost:${API_PORT:-8080}/metrics"
echo "  │ Grafana:        http://localhost:${GRAFANA_PORT:-3000}"
echo "  │   用户: ${GRAFANA_USER:-admin}"
echo "  │   密码: ${GRAFANA_PASSWORD:-agent1-admin}"
echo "  │ Prometheus:     http://localhost:${PROMETHEUS_PORT:-9090}"
echo "  │ PostgreSQL:     localhost:${DB_PORT:-5432}"
echo "  │ llama.cpp LLM:  http://localhost:${LLAMA_PORT:-8080}"
echo "  │ llama.cpp Embed: http://localhost:${LLAMA_EMBED_PORT:-8081}"
echo "  └─────────────────────────────────────"
echo ""
echo "  常用命令:"
echo "  docker compose logs -f api      查看 API 实时日志"
echo "  docker compose restart api      重启 API"
echo "  docker compose down             停止全部服务"
echo "  docker compose down -v          停止+清除所有数据卷"
echo ""
echo "  查看日志溯源:"
echo "  docker compose exec api cat /app/logs/agent1-api-\$(date +%Y%m%d).log"
echo ""
echo "  状态检查:"
docker compose ps --format "table {{.Name}}\t{{.Status}}\t{{.Ports}}" 2>/dev/null || docker compose ps
