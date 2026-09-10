> 组织总览与进度：[LongLongGames](https://github.com/LongLongGames) · [Platform Roadmap](https://github.com/orgs/LongLongGames/projects/1)

# game-match3-server

Match3 游戏后端。由 [GameTemplate](https://github.com/LongLongGames/GameTemplate) 复制落地。

## 服务

| 服务 | 说明 |
|------|------|
| game-gateway | Nginx（8081） |
| game-user | 玩家资料 |
| game-leaderboard | 排行榜 |
| game-core | 版本/资源检查（弱联网服不做玩法） |

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
| GET | /api/v1/user/state?game_id=match3&map_id=1 | 体力/金币/地图进度（10 关） |
| POST | /api/v1/user/level/clear | 通关上报（扣体力、发金币、更新星级） |
| POST | /api/v1/user/energy/cheat-refill | 开发用：体力回满 |
| POST | /api/v1/leaderboard/score | 提交分数 |
| GET | /api/v1/leaderboard/top | 排行榜 |
| GET | /api/v1/leaderboard/me | 自己的排名 |
| GET | /api/v1/game/status | JWT 验签示例 |
| GET | /api/v1/game/version-check | 版本/资源检查 |

### Match3 进度约定

- 关卡棋盘配置在**客户端**；服务器只存玩家经济与进度。
- 体力上限 30，每 300 秒自然恢复 1 点；每局消耗 1 点。
- 每张地图 10 关；同图需上一关至少 1 星才能打下一关。
- 当前图累计通关 ≥ 5 关时解锁下一张地图。
- 通关星级 1–3，金币奖励 = 50 × 星数；星级取历史最高，步数取历史最少。



## 配置表（ExcelConfigCompiler → Server）

客户端 **Tools → Excel Config Compiler** 导出 `.bytes`，同步到本仓库 `config/`（与 client 同级时由导表输出或 CI 拷贝）。

| 文件 | 说明 |
|------|------|
| `config/Level.bytes` | 关卡表（**优先**；通关校验存在性 / MaxSteps） |
| `config/Item.bytes` | 道具表（可选加载） |
| `config/CheckInReward.bytes` | 签到表（可选加载） |
| `config/GameRules.json` | 服务端规则：体力、每图关卡数、校验开关（非 ECC） |
| `config/Level.json` | 仅兼容旧 BakingSheet；有 `.bytes` 时忽略 |

`game-user` 启动时读入内存。`POST /api/v1/user/level/clear` 按表校验关卡与步数（可用 `GameRules.json` 关闭）。

约定：`LevelsPerMap = 10`（与客户端一致）。

Docker：镜像内 `/app/config`，compose 挂载 `./config`。环境变量：`Config__Root`（默认 `/app/config`）。

二进制格式：Magic `EXCF` + version + rows（与 [ExcelConfigCompiler](https://github.com/setsuodu/ExcelConfigCompiler) 一致）。

## 发布

Tag `v*` 触发构建：

- `ghcr.io/longlonggames/game-match3-server/game-user`
- `ghcr.io/longlonggames/game-match3-server/game-leaderboard`
- `ghcr.io/longlonggames/game-match3-server/game-core`

## 技术

.NET 10 Native AOT · Npgsql · DbUp · JWT（与 MP 同 Secret）
