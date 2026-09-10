# 服务端配置目录

## ExcelConfigCompiler 产物（与客户端同表）

由客户端 **Tools → Excel Config Compiler** 导出后拷贝到本目录（或 CI 同步）。

| 文件 | 说明 |
|------|------|
| `Level.bytes` | 关卡表（**优先**加载） |
| `Item.bytes` | 道具表（可选） |
| `CheckInReward.bytes` | 签到奖励（可选） |
| `Level.json` | 仅兼容旧 BakingSheet；有 `.bytes` 时不会读 |

字段顺序必须与客户端生成的 `Level.cs` / `Item.cs` / `CheckInReward.cs` 一致。

## 服务端自有

| 文件 | 说明 |
|------|------|
| `GameRules.json` | 体力、每图关卡数、校验开关等（**不**经 ECC） |

当前约定：`LevelsPerMap = 10`。

Docker：`./config` → `/app/config`（只读挂载）。环境变量：`Config__Root`。
