# ==========================================
# AgentSystem - 化工合规 AI Agent API Dockerfile
# 多阶段构建：SDK 编译 → Runtime 运行
# ==========================================
# 使用方式：
#   docker build -t agent1-api .
#   docker run -p 8080:8080 --env DB_PASSWORD=xxx agent1-api
# ==========================================

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 还原 NuGet 包（分层缓存）
# 注意：不 COPY global.json，Docker 镜像自带 SDK，版本约束会导致不兼容
COPY nuget.config ./
COPY Agent1/Agent1.csproj Agent1/
COPY Agent1.Api/Agent1.Api.csproj Agent1.Api/
RUN dotnet restore Agent1.Api/Agent1.Api.csproj

# 复制源码并编译
COPY Agent1/ Agent1/
COPY Agent1.Api/ Agent1.Api/
COPY Data/ Data/
RUN dotnet publish Agent1.Api/Agent1.Api.csproj -c Release -o /app/publish

# ════════════════════════════════════════
# Runtime 镜像（非 root 运行）
# ════════════════════════════════════════
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# 安装 wget（用于健康检查）+ 创建非 root 用户
# 注意：ARM64 镜像 GPG 密钥可能缺失，使用 --allow-unauthenticated
RUN apt-get update --allow-insecure-repositories && \
    apt-get install -y --no-install-recommends --allow-unauthenticated wget && \
    rm -rf /var/lib/apt/lists/* && \
    adduser --disabled-password --gecos "" appuser

COPY --from=build /app/publish .

# COPY 后 chown，确保发布文件属主为 appuser
RUN mkdir -p /app/logs && \
    chown -R appuser:appuser /app

USER appuser

# ASP.NET Core 监听端口
ENV ASPNETCORE_URLS=http://+:8080

# ════════════════════════════════════════
# 环境变量默认值（可在 docker-compose 中覆盖）
# ════════════════════════════════════════
ENV LLM_ENDPOINT=http://localhost:8080/v1
ENV KNOWLEDGE_BASE_PATH=/app/knowledgebase
ENV CORS_ORIGINS=http://localhost:3000,http://localhost:5173
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# 健康检查
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD wget --no-verbose --tries=1 --spider http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Agent1.Api.dll"]
