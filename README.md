> 组织总览与进度：[LongLongGames](https://github.com/LongLongGames) · [Platform Roadmap](https://github.com/orgs/LongLongGames/projects/1)

# game-match3-server

Match3 游戏后端。由 [GameTemplate](https://github.com/LongLongGames/GameTemplate) 复制落地。

## 服务

| 服务 | 说明 |
|------|------|
| game-gateway | Nginx（8081） |
| game-user | 玩家资料 |
| game-leaderboard | 排行榜 |
| game-core | 玩法 |

Postgres `5433` · Redis `6380`（与 MP 隔离）

## 启动

```bash
cp .env.example .env
# JWT_SECRET 必须与 MP 一致
docker compose up -d --build
```

- 网关：http://localhost:8081
- 健康检查：`GET /health`

## 与 MP 联调

1. MP 在 http://localhost:8080 运行
2. 登录取 JWT，请求本服务时带 `Authorization: Bearer <token>`
3. 或：`./scripts/smoke-test.sh`

## API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | /api/v1/user/profile?game_id=match3 | 资料（无则创建） |
| PUT | /api/v1/user/profile | 更新资料 |
| POST | /api/v1/leaderboard/score | 提交分数 |
| GET | /api/v1/leaderboard/top | 排行榜 |
| GET | /api/v1/leaderboard/me | 自己的排名 |
| GET | /api/v1/game/status | JWT 验签示例 |

## 发布

Tag `v*` 触发构建：

- `ghcr.io/longlonggames/game-match3-server/game-user`
- `ghcr.io/longlonggames/game-match3-server/game-leaderboard`
- `ghcr.io/longlonggames/game-match3-server/game-core`

## 技术

.NET 10 Native AOT · Npgsql · DbUp · JWT（与 MP 同 Secret）
