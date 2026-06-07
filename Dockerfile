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

# 创建非 root 用户
RUN adduser --disabled-password --gecos "" appuser && \
    mkdir -p /app/logs && \
    chown -R appuser:appuser /app

USER appuser

COPY --from=build /app/publish .

# ASP.NET Core 监听端口
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# 健康检查
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Agent1.Api.dll"]
