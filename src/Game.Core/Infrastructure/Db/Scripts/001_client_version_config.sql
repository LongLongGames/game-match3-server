-- ============================================================
-- 客户端版本 + 资源热更配置（YooAsset / 自研 AB 通用）
-- 不含 Addressables。启动时一次性完整下载模式。
-- ============================================================

CREATE TABLE IF NOT EXISTS client_version_config (
    id                        BIGSERIAL PRIMARY KEY,
    game_id                   VARCHAR(32)  NOT NULL,           -- match3
    channel                   VARCHAR(32)  NOT NULL,           -- official / google / apple / steam ...
    platform                  VARCHAR(16)  NOT NULL,           -- android / ios / windows / macos
    region                    VARCHAR(16)  NOT NULL DEFAULT 'cn', -- cn / global / jp / kr

    -- 客户端整包
    client_version            VARCHAR(32)  NOT NULL,           -- 展示用 "1.2.0"
    client_version_code       INT          NOT NULL,           -- 比较用 10200
    min_client_version_code   INT          NOT NULL,           -- 强制更新门槛
    force_update              BOOLEAN      NOT NULL DEFAULT false,
    app_store_url             TEXT,

    -- 资源（YooAsset / 自研 AB）
    resource_version          VARCHAR(32)  NOT NULL,           -- 2026.08.15.1
    package_name              VARCHAR(64)  NOT NULL DEFAULT 'DefaultPackage',
    cdn_main_url              TEXT         NOT NULL,           -- 主 CDN 目录
    cdn_fallback_url          TEXT,                            -- 备用 CDN
    resource_manifest_url     TEXT,                            -- 自研 AB 可用；YooAsset 可空

    -- 灰度与状态
    status                    VARCHAR(16)  NOT NULL DEFAULT 'active', -- active / gray / maintenance
    gray_rate                 INT          NOT NULL DEFAULT 100,      -- 0-100

    extra_json                JSONB        NOT NULL DEFAULT '{}',     -- 渠道特殊配置等
    created_at                TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_at                TIMESTAMPTZ  NOT NULL DEFAULT NOW(),

    UNIQUE (game_id, channel, platform, region)
);

CREATE INDEX IF NOT EXISTS idx_cvc_lookup
    ON client_version_config (game_id, channel, platform, region);

-- 示例数据（可按需修改或删除）
INSERT INTO client_version_config (
    game_id, channel, platform, region,
    client_version, client_version_code, min_client_version_code, force_update, app_store_url,
    resource_version, package_name, cdn_main_url, cdn_fallback_url, resource_manifest_url,
    status, gray_rate
) VALUES
(
    'match3', 'official', 'android', 'cn',
    '1.0.0', 10000, 10000, false, NULL,
    '2026.08.15.1', 'DefaultPackage',
    'https://cdn1.example.com/match3/android/cn/v2026.08.15.1/',
    'https://cdn2.example.com/match3/android/cn/v2026.08.15.1/',
    NULL,
    'active', 100
),
(
    'match3', 'official', 'android', 'global',
    '1.0.0', 10000, 10000, false, NULL,
    '2026.08.15.1', 'DefaultPackage',
    'https://cdn1.example.com/match3/android/global/v2026.08.15.1/',
    'https://cdn2.example.com/match3/android/global/v2026.08.15.1/',
    NULL,
    'active', 100
)
ON CONFLICT (game_id, channel, platform, region) DO NOTHING;
