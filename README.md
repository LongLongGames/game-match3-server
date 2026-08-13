# GameTemplate — 游戏后端模板 v0.2

> 原仓库名 `MS`。现定位为 **可复制的游戏后端骨架**，不是平台共享运行时。

## 定位

```
MP（平台，唯一）
  └── Catalog / Auth

GameTemplate（本仓库，模板）
  └── clone → game-match3 / game-flyingchess / …

每个 game-xxx：独立 Repo + 独立 Image + 独立 DB + 独立发布
```

**用法**：

```bash
# 复制本仓库作为新游戏起点
git clone <本仓库> game-match3
cd game-match3
# 改 GAME_ID、玩法逻辑、表结构……
docker compose up -d --build
```

不要把本仓库当成「所有游戏共用的一份在线服务」长期运行。

## 服务一览

| 服务 | 说明 |
|------|------|
| **game-gateway** | Nginx 反代（8081） |
| **game-user** | 本游戏玩家资料 |
| **game-leaderboard** | 本游戏排行榜 |
| **game-core** | 玩法占位（clone 后在此写真实逻辑） |

基础设施：PostgreSQL 16 + Redis 7（与 MP 独立，端口 5433 / 6380）

## 快速启动

```bash
cp .env.example .env
# JWT_SECRET 必须与 MP 完全一致！

docker compose up -d --build
```

- 网关：http://localhost:8081
- 健康检查：`GET http://localhost:8081/health`
- Postgres：localhost:5433
- Redis：localhost:6380

## 与 MP 联调

1. MP 在 http://localhost:8080 运行，两边 `JWT_SECRET` 相同。
2. 登录拿 Token：

```bash
curl -s -X POST http://localhost:8080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{
    "provider": "official",
    "app_id": "test_app",
    "device_id": "gt-test",
    "auth_payload": { "username": "tester1", "password": "test1234" }
  }' | jq
```

3. 带 `Authorization: Bearer <access_token>` 调本模板接口。

或直接跑：

```bash
chmod +x scripts/smoke-test.sh
./scripts/smoke-test.sh
```

## API

### game-user

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/v1/user/profile?game_id=match3` | 获取（不存在则自动创建）资料 |
| PUT | `/api/v1/user/profile` | 更新昵称 / 等级 / 扩展字段 |

### game-leaderboard

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/v1/leaderboard/score` | 提交分数（同 game+board 保留最高分） |
| GET | `/api/v1/leaderboard/top?game_id=match3&board_id=default&limit=50` | 排行榜 |
| GET | `/api/v1/leaderboard/me?game_id=match3&board_id=default` | 自己的排名 |

### game-core（占位）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/v1/game/status` | JWT 验签示例 |

## 游戏接入规范

1. **先在 MP Catalog 注册** `game_id`（平台侧插 `games` 表）。
2. **身份**：客户端先调 MP 拿 JWT；本服务本地验签（复制 `Game.Shared/Jwt/SimpleJwt.cs`），禁止回调 MP。
3. **资料 / 排行榜**：使用上表 API；`game_id` 与 Catalog 一致。
4. **玩法**：在 `Game.Core`（或你拆出的新项目）里写，独立发版。
5. **健康检查**：`GET /health` → `{"status":"ok","service":"game-xxx"}`

## 命名对照（原 MS → 现）

| 原 | 现 |
|----|----|
| MS | GameTemplate |
| MS.Shared | Game.Shared |
| MS.User | Game.User |
| MS.Leaderboard | Game.Leaderboard |
| MS.Game | Game.Core |
| ms-user / ms-leaderboard / ms-game / ms-gateway | game-user / game-leaderboard / game-core / game-gateway |
| 平台 games 表 | 已迁至 **MP Catalog**，本模板不再维护 |

## 技术说明

- .NET 10 Native AOT
- Npgsql + DbUp
- 与 MP 一致的 SimpleJwt（HS256）
- 本版全部 HTTP

## 目录

```
GameTemplate/
├── .github/workflows/deploy.yml
├── nginx/nginx.conf
├── scripts/smoke-test.sh
├── src/
│   ├── Game.Shared/          # JWT 公共库
│   ├── Game.User/
│   ├── Game.Leaderboard/
│   └── Game.Core/            # 玩法占位
├── docker-compose.yml
├── .env.example
└── README.md
```

## Roadmap（按需）

- [ ] Mail / BugReport 模块（模板内置，数据仍随本游戏 DB）
- [ ] 环境变量 `GAME_ID` 强制校验
- [ ] RabbitMQ 结算事件
- [ ] Prometheus
