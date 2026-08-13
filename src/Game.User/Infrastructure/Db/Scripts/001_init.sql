CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================
-- 玩家在本游戏下的资料
-- 本仓库是「单游戏模板」：clone 成 game-xxx 后只服务一个游戏。
-- game_id 建议与环境变量 GAME_ID 一致；也可由调用方传入。
-- 全局游戏注册表已迁至 MP Catalog，此处不再维护 games 表。
-- ============================================================
CREATE TABLE IF NOT EXISTS player_profiles (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    mp_account_id   UUID NOT NULL,
    game_id         TEXT NOT NULL,
    nickname        TEXT NOT NULL DEFAULT '',
    level           INT  NOT NULL DEFAULT 1,
    extra_json      JSONB NOT NULL DEFAULT '{}',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (mp_account_id, game_id)
);

CREATE INDEX IF NOT EXISTS idx_player_profiles_mp ON player_profiles (mp_account_id);
CREATE INDEX IF NOT EXISTS idx_player_profiles_game ON player_profiles (game_id);
