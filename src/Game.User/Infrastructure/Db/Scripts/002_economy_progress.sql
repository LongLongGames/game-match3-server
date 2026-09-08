-- Match3 体力 / 金币 / 地图关卡进度
-- 关卡静态配置在客户端；服务器只存玩家进度与经济，便于弱联网与后续校验。

CREATE TABLE IF NOT EXISTS player_economy (
    mp_account_id   UUID NOT NULL,
    game_id         TEXT NOT NULL,
    energy          INT  NOT NULL DEFAULT 30,
    energy_max      INT  NOT NULL DEFAULT 30,
    gold            BIGINT NOT NULL DEFAULT 0,
    -- 上次能量变更时间（用于自然恢复）
    energy_updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    -- 当前已解锁到的地图编号（从 1 开始）
    unlocked_map    INT  NOT NULL DEFAULT 1,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (mp_account_id, game_id),
    CONSTRAINT chk_energy_nonneg CHECK (energy >= 0),
    CONSTRAINT chk_gold_nonneg CHECK (gold >= 0),
    CONSTRAINT chk_unlocked_map CHECK (unlocked_map >= 1)
);

-- 每关最佳成绩：map_id + level_id（1..20）
CREATE TABLE IF NOT EXISTS player_level_progress (
    mp_account_id   UUID NOT NULL,
    game_id         TEXT NOT NULL,
    map_id          INT  NOT NULL,
    level_id        INT  NOT NULL,
    stars           INT  NOT NULL DEFAULT 0,
    best_steps      INT,                 -- 通关所用最少步数（越少越好，可空表示未通关）
    clear_count     INT  NOT NULL DEFAULT 0,
    last_score      BIGINT NOT NULL DEFAULT 0,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (mp_account_id, game_id, map_id, level_id),
    CONSTRAINT chk_map_id CHECK (map_id >= 1),
    CONSTRAINT chk_level_id CHECK (level_id >= 1 AND level_id <= 20),
    CONSTRAINT chk_stars CHECK (stars >= 0 AND stars <= 3)
);

CREATE INDEX IF NOT EXISTS idx_level_progress_player
    ON player_level_progress (mp_account_id, game_id, map_id);
