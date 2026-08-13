CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- 本仓库是「单游戏模板」。game_id 建议与 GAME_ID 环境变量一致。
-- 全局游戏注册在 MP Catalog，此处不加跨库外键。
CREATE TABLE IF NOT EXISTS leaderboard_scores (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    mp_account_id   UUID NOT NULL,
    game_id         TEXT NOT NULL,
    board_id        TEXT NOT NULL DEFAULT 'default',
    score           BIGINT NOT NULL,
    nickname        TEXT NOT NULL DEFAULT '',
    extra_json      JSONB NOT NULL DEFAULT '{}',
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (mp_account_id, game_id, board_id)
);

CREATE INDEX IF NOT EXISTS idx_lb_game_board_score
    ON leaderboard_scores (game_id, board_id, score DESC);
