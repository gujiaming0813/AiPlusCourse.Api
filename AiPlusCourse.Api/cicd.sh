#!/bin/bash

# ================= 配置区 =================
# 自动获取脚本所在目录作为项目根目录
PROJECT_DIR=$(cd "$(dirname "$0")"; pwd)
IMAGE_NAME="aipluscourse-api"
CONTAINER_NAME="aipluscourse-api"
PORT=7001
# =========================================

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

log "====== 开始自动化部署 (安全备份模式) ======"
log "📂 工作目录: $PROJECT_DIR"

# 1. 进入目录 & 拉取代码
cd "$PROJECT_DIR" || exit
log "1. 拉取最新代码..."
git pull origin master

# 2. 准备阶段：标记当前运行版本为 '临时备份' (pre-deploy)
if docker image inspect $IMAGE_NAME:latest >/dev/null 2>&1; then
    log "2. 将当前版本标记为临时备份 ($IMAGE_NAME:pre-deploy)..."
    # 强制覆盖可能存在的旧 pre-deploy
    docker tag $IMAGE_NAME:latest $IMAGE_NAME:pre-deploy
else
    log "2. 首次部署或无 latest 镜像，跳过预备份。"
fi

# 3. 构建新镜像
log "3. 开始构建新镜像..."
if docker build -t $IMAGE_NAME:latest .; then
    log "   ✅ 构建成功！"
else
    log "   ❌ 构建失败！取消部署。"
    # 恢复现场：如果有 pre-deploy，把它恢复 tag 为 latest (虽然此时 latest 应该还在，但为了保险)
    exit 1
fi

# 4. 停止并移除旧容器
if [ "$(docker ps -aq -f name=$CONTAINER_NAME)" ]; then
    log "4. 停止旧容器..."
    docker rm -f $CONTAINER_NAME
fi

# 5. 启动新容器
log "5. 启动新容器 (端口: $PORT)..."
# 注意：容器内部端口为 7001 (对应 Dockerfile 配置)
docker run -d -p $PORT:7001 --name $CONTAINER_NAME --restart=always --network app-network $IMAGE_NAME:latest

# 6. 健康检查与决策 (核心逻辑修改) 
log "6. 等待 10 秒进行健康检查..."
sleep 10

if [ "$(docker inspect -f '{{.State.Running}}' $CONTAINER_NAME 2>/dev/null)" == "true" ]; then
    # === 🟢 成功分支 ===
    log "🎉 部署成功！新服务运行正常。"
    log "🔄 正在更新备份镜像..."
    
    # 1. 删除最老的 backup
    if docker image inspect $IMAGE_NAME:backup >/dev/null 2>&1; then
        docker rmi -f $IMAGE_NAME:backup
    fi
    
    # 2. 将 pre-deploy (刚才的旧版) 转正为新的 backup
    if docker image inspect $IMAGE_NAME:pre-deploy >/dev/null 2>&1; then
        docker tag $IMAGE_NAME:pre-deploy $IMAGE_NAME:backup
        docker rmi $IMAGE_NAME:pre-deploy
        log "   ✅ 备份更新完毕：上一个版本已存为 $IMAGE_NAME:backup"
    fi
    
else
    # === 🔴 失败分支 ===
    log "❌ 部署失败！新容器无法启动。"
    log "🔄 执行回滚策略 (保留原备份)..."
    
    # 1. 删除部署失败的镜像
    docker rm -f $CONTAINER_NAME
    log "   删除故障的新镜像..."
    docker rmi -f $IMAGE_NAME:latest
    
    # 2. 恢复上一版
    if docker image inspect $IMAGE_NAME:pre-deploy >/dev/null 2>&1; then
        log "   正在从临时备份 ($IMAGE_NAME:pre-deploy) 恢复服务..."
        
        # 把 pre-deploy 恢复为 latest
        docker tag $IMAGE_NAME:pre-deploy $IMAGE_NAME:latest
        
        # 启动旧版
        docker run -d -p $PORT:7001 --name $CONTAINER_NAME --restart=always --network app-network $IMAGE_NAME:latest
        
        # 清理临时标签
        docker rmi $IMAGE_NAME:pre-deploy
        
        log "   ✅ 已回滚到部署前的版本。"
        log "   ℹ️ 提示：之前的 $IMAGE_NAME:backup 仍保留，未被覆盖。"
    else
        log "❌ 严重错误：没有临时备份可供回滚！"
    fi
fi

log "====== 流程结束 ======"
