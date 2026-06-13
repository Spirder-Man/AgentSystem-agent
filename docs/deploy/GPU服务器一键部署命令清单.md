# GPU 服务器一键部署命令清单

> 适用环境：Ubuntu 22.04 + NVIDIA GPU（AutoDL / 阿里云 / 腾讯云 等）
> 部署目标：Agent1 化工合规 AI Agent 全栈服务

---

## 部署流程（共 9 步）

### 第 1 步：安装 Docker

```bash
rm -f /etc/apt/sources.list.d/docker.list
echo "deb [trusted=yes] https://mirrors.tuna.tsinghua.edu.cn/docker-ce/linux/ubuntu jammy stable" | tee /etc/apt/sources.list.d/docker.list
apt update && apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

> **说明：** 国内网络直连 Docker 官方源会失败，用清华镜像 + `trusted=yes` 绕过 GPG 验证。

---

### 第 2 步：导入 NVIDIA GPG 密钥

```bash
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | gpg --dearmor -o /usr/share/keyrings/nvidia-docker.gpg --yes
```

> **说明：** 下载 NVIDIA 官方签名密钥，用于验证后续安装包的合法性。

---

### 第 3 步：添加 NVIDIA APT 源

```bash
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | sed -e 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-docker.gpg] https://#' -e 's/\$(ARCH)/amd64/g' | tee /etc/apt/sources.list.d/nvidia-container-toolkit.list
```

> **说明：** 从 NVIDIA 官方下载源地址模板，`sed` 同时完成两件事：① 注入 GPG 签名密钥路径 ② 将 `$(ARCH)` 变量替换为 `amd64`（避免 shell 空展开导致 404）。

---

### 第 4 步：安装 NVIDIA Container Toolkit

```bash
apt update && apt install -y nvidia-container-toolkit
```

> **说明：** 安装 GPU 容器运行时插件，Docker 容器才能访问 RTX 3080。

---

### 第 5 步：配置 Docker 使用 NVIDIA 运行时

```bash
nvidia-ctk runtime configure --runtime=docker
```

> **说明：** 修改 Docker 配置，让 `--gpus all` 参数生效。**注意：此命令会覆盖 daemon.json，下一步需重新写入完整配置。**

---

### 第 6 步：配置 daemon.json（镜像加速 + vfs 驱动 + GPU 运行时）

```bash
cat > /etc/docker/daemon.json << 'EOF'
{
    "registry-mirrors": [
        "https://registry.cn-hangzhou.aliyuncs.com",
        "https://docker.m.daocloud.io"
    ],
    "storage-driver": "vfs",
    "runtimes": {
        "nvidia": {
            "args": [],
            "path": "nvidia-container-runtime"
        }
    }
}
EOF
```

> **说明：** 三项配置合一：① 阿里云镜像加速（Docker Hub 直连超时） ② `vfs` 驱动（AutoDL 容器内无挂载权限，不能用 overlayfs） ③ NVIDIA 运行时（上一步被覆盖，重新写入）。

---

### 第 7 步：启动 Docker（绕过容器网络/存储限制）

```bash
pkill dockerd; sleep 2
rm -f /var/run/docker.sock /var/run/docker.pid
rm -rf /var/lib/docker
nohup dockerd --bridge=none --iptables=false --ip6tables=false &>/var/log/dockerd.log &
sleep 4
docker info 2>/dev/null | grep -E "Storage Driver|Registry Mirrors" -A 3
```

> **说明：** `--bridge=none` 跳过创建 docker0（无权限）、`--iptables=false` 跳过 NAT 规则（无 iptables 权限）、清空旧镜像缓存。确认输出显示 `vfs` + `registry.cn-hangzhou.aliyuncs.com` 即成功。

---

### 第 8 步：拉取项目代码

```bash
cd /root/autodl-tmp
git clone https://gitee.com/liuchao_yue/agent-system.git
cd agent-system
```

> **说明：** 放在数据盘 `/root/autodl-tmp`，关机不丢数据。

---

### 第 9 步：启用 GPU + 一键部署

```bash
# 取消注释 docker-compose.yml 中的 GPU 配置
sed -i '53,60s/^# //' docker-compose.yml

# 创建环境变量文件
cp .env.example .env

# 启动全栈服务（PostgreSQL + Ollama + API）
docker compose up -d
```

> **说明：** `sed` 命令自动取消 GPU 段的注释；首次启动会自动拉取 qwen3:8b 和 nomic-embed-text 两个模型。新版 Docker 用 `docker compose`（有空格）而非 `docker-compose`。

---

## 完整一键脚本

把以下内容保存为 `deploy.sh`，一条命令完成部署：

```bash
#!/bin/bash
set -e
echo "=== [1/9] 安装 Docker ==="
rm -f /etc/apt/sources.list.d/docker.list
echo "deb [trusted=yes] https://mirrors.tuna.tsinghua.edu.cn/docker-ce/linux/ubuntu jammy stable" | tee /etc/apt/sources.list.d/docker.list
apt update && apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

echo "=== [2/9] NVIDIA GPG 密钥 ==="
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | gpg --dearmor -o /usr/share/keyrings/nvidia-docker.gpg --yes

echo "=== [3/9] NVIDIA APT 源 ==="
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | sed -e 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-docker.gpg] https://#' -e 's/\$(ARCH)/amd64/g' | tee /etc/apt/sources.list.d/nvidia-container-toolkit.list

echo "=== [4/9] 安装 NVIDIA Container Toolkit ==="
apt update && apt install -y nvidia-container-toolkit

echo "=== [5/9] 配置 Docker GPU 运行时 ==="
nvidia-ctk runtime configure --runtime=docker

echo "=== [6/9] 配置 daemon.json（镜像加速 + vfs + GPU） ==="
cat > /etc/docker/daemon.json << 'DOCKEREOF'
{
    "registry-mirrors": [
        "https://registry.cn-hangzhou.aliyuncs.com",
        "https://docker.m.daocloud.io"
    ],
    "storage-driver": "vfs",
    "runtimes": {
        "nvidia": {
            "args": [],
            "path": "nvidia-container-runtime"
        }
    }
}
DOCKEREOF

echo "=== [7/9] 启动 Docker（绕过容器限制） ==="
pkill dockerd 2>/dev/null; sleep 2
rm -f /var/run/docker.sock /var/run/docker.pid
rm -rf /var/lib/docker
nohup dockerd --bridge=none --iptables=false --ip6tables=false &>/var/log/dockerd.log &
sleep 4

echo "=== [8/9] 拉取代码 ==="
cd /root/autodl-tmp
git clone https://gitee.com/liuchao_yue/agent-system.git || true
cd agent-system
git pull origin master

echo "=== [9/9] 启用 GPU + 部署 ==="
sed -i '53,60s/^# //' docker-compose.yml
cp .env.example .env
docker compose up -d

echo "=== 部署完成！==="
docker compose ps
```

使用方法：
```bash
chmod +x deploy.sh && ./deploy.sh
```

---

## 验证部署

```bash
# 健康检查
curl http://localhost:8080/health

# 登录获取 Token
curl -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"7758521"}'

# 查询化学品合规
curl -X POST http://localhost:8080/api/compliance/hazard/query \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"substanceName":"苯"}'
```

---

## 常见问题

| 问题 | 解决 |
|------|------|
| Docker 源 GPG 验证失败 | 用 `trusted=yes` 跳过 |
| `lsb_release` 不存在 | 写死 `jammy`（Ubuntu 22.04 代号） |
| NVIDIA APT 源路径错误 | 用标准路径 `stable/deb/`，不要拼接 `ubuntu22.04/amd64` |
| `$(ARCH)` 未展开 | `sed` 替换为 `amd64` |
| Docker Daemon iptables 权限不足 | dockerd 加 `--iptables=false --ip6tables=false` |
| bridge 创建失败 `operation not permitted` | dockerd 加 `--bridge=none` |
| overlayfs mount 失败 | `storage-driver` 切 `vfs`，并 `rm -rf /var/lib/docker` 清缓存 |
| nvidia-ctk 覆盖 daemon.json | 重新写入完整配置（含镜像加速 + vfs + GPU 运行时） |
| Docker Hub 拉取超时 | 用阿里云 `registry.cn-hangzhou.aliyuncs.com` 镜像加速 |
| `docker-compose` 命令不存在 | 新版 Docker 用 `docker compose`（有空格） |
| 模型拉取慢 | 已通过 `ollama-pull` 容器自动拉取 |

---

> 更新时间：2026-06-11  
> 目标环境：AutoDL RTX 3080 / 10GB + Ubuntu 22.04
