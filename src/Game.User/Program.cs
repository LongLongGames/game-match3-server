using System.Text.Json.Serialization;
using DbUp;
using Game.Shared.Jwt;
using Game.Shared.Config;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);


// =================== 【迁移入口：--migrate 或 RUN_MIGRATION_ONLY=true】 ===================
var runMigrationOnly = args.Contains("--migrate")
    || string.Equals(Environment.GetEnvironmentVariable("RUN_MIGRATION_ONLY"), "true", StringComparison.OrdinalIgnoreCase);

if (runMigrationOnly)
{
    var migrateConn = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("缺少 ConnectionStrings__Postgres");
    Console.WriteLine("Executing database migrations...");
    var upgrader = DeployChanges.To
        .PostgresqlDatabase(migrateConn)
        .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
        .WithTransaction()
        .LogToConsole()
        .Build();
    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
    {
        Console.Error.WriteLine($"DB migration failed: {result.Error}");
        Environment.Exit(1);
    }
    Console.WriteLine("Migration completed successfully.");
    return; // 只跑迁移，不启动 Web
}
// ========================================================================================

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("缺少 Jwt__Secret（必须与 MP 一致）");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "battle-net-mp";
var connStr = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("缺少 ConnectionStrings__Postgres");

builder.Services.AddSingleton(new SimpleJwt(jwtSecret, jwtIssuer));
builder.Services.AddSingleton(new NpgsqlDataSourceBuilder(connStr).Build());

var configRoot = GameConfigStore.ResolveConfigRoot(
    builder.Configuration["Config:Root"] ?? builder.Configuration["Config__Root"]);
var gameConfig = GameConfigStore.Load(configRoot);
Match3Rules.Apply(gameConfig.Rules);
builder.Services.AddSingleton(gameConfig);

var app = builder.Build();

// 正常启动路径不再执行迁移。由 *-migrate Job 或 --migrate 完成。

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", "game-user")));

// 获取或自动创建当前玩家在某个游戏的资料
app.MapGet("/api/v1/user/profile", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, string game_id) =>
{
    if (!Helpers.TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        SELECT id, mp_account_id, game_id, nickname, level, extra_json, created_at, updated_at
        FROM player_profiles
        WHERE mp_account_id = @mp AND game_id = @gid
        """, conn);
    cmd.Parameters.AddWithValue("mp", Guid.Parse(claims!.Sub));
    cmd.Parameters.AddWithValue("gid", game_id);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (await reader.ReadAsync())
        return Results.Ok(Helpers.ReadProfile(reader));

    await reader.CloseAsync();
    await using var insert = new NpgsqlCommand("""
        INSERT INTO player_profiles (mp_account_id, game_id, nickname)
        VALUES (@mp, @gid, @nick)
        RETURNING id, mp_account_id, game_id, nickname, level, extra_json, created_at, updated_at
        """, conn);
    insert.Parameters.AddWithValue("mp", Guid.Parse(claims.Sub));
    insert.Parameters.AddWithValue("gid", game_id);
    insert.Parameters.AddWithValue("nick", "Player_" + claims.Sub[..Math.Min(8, claims.Sub.Length)]);

    await using var r2 = await insert.ExecuteReaderAsync();
    await r2.ReadAsync();
    return Results.Ok(Helpers.ReadProfile(r2));
});

// 更新昵称 / 等级 / 扩展字段
app.MapPut("/api/v1/user/profile", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, UpdateProfileRequest body) =>
{
    if (!Helpers.TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.GameId))
        return Results.Json(new ErrorResponse("game_id required"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        INSERT INTO player_profiles (mp_account_id, game_id, nickname, level, extra_json, updated_at)
        VALUES (@mp, @gid, @nick, COALESCE(@lv, 1), COALESCE(@extra::jsonb, '{}'::jsonb), NOW())
        ON CONFLICT (mp_account_id, game_id) DO UPDATE SET
            nickname = COALESCE(NULLIF(@nick, ''), player_profiles.nickname),
            level = COALESCE(@lv, player_profiles.level),
            extra_json = COALESCE(@extra::jsonb, player_profiles.extra_json),
            updated_at = NOW()
        RETURNING id, mp_account_id, game_id, nickname, level, extra_json, created_at, updated_at
        """, conn);
    cmd.Parameters.AddWithValue("mp", Guid.Parse(claims!.Sub));
    cmd.Parameters.AddWithValue("gid", body.GameId);
    cmd.Parameters.AddWithValue("nick", body.Nickname ?? "");
    cmd.Parameters.AddWithValue("lv", (object?)body.Level ?? DBNull.Value);
    cmd.Parameters.AddWithValue("extra", body.ExtraJson ?? "{}");

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(Helpers.ReadProfile(reader));
});

// ============================================================
// Match3 玩家状态：体力 / 金币 / 地图进度
// 关卡棋盘配置在客户端；此处只同步进度与经济。
// ============================================================

/// GET /api/v1/user/state?game_id=match3&map_id=1
app.MapGet("/api/v1/user/state", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, string game_id, int? map_id) =>
{
    if (!Helpers.TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(game_id))
        return Results.Json(new ErrorResponse("game_id required"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);

    var mp = Guid.Parse(claims!.Sub);
    var mapId = map_id is > 0 ? map_id.Value : 1;

    await using var conn = await ds.OpenConnectionAsync();
    await Helpers.EnsureEconomyAsync(conn, mp, game_id);
    var economy = await Helpers.LoadAndRegenEnergyAsync(conn, mp, game_id, persist: true);

    var levels = new List<LevelProgressItem>();
    await using (var cmd = new NpgsqlCommand("""
        SELECT level_id, stars, best_steps, clear_count, last_score
        FROM player_level_progress
        WHERE mp_account_id = @mp AND game_id = @gid AND map_id = @mid
        ORDER BY level_id
        """, conn))
    {
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.Parameters.AddWithValue("gid", game_id);
        cmd.Parameters.AddWithValue("mid", mapId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            levels.Add(new LevelProgressItem(
                r.GetInt32(0),
                r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetInt32(2),
                r.GetInt32(3),
                r.GetInt64(4)
            ));
        }
    }

    var byLevel = levels.ToDictionary(x => x.LevelId);
    var full = new List<LevelProgressItem>(Match3Rules.LevelsPerMap);
    for (var i = 1; i <= Match3Rules.LevelsPerMap; i++)
    {
        full.Add(byLevel.TryGetValue(i, out var item)
            ? item
            : new LevelProgressItem(i, 0, null, 0, 0));
    }

    var clearedOnMap = full.Count(x => x.Stars > 0);

    return Results.Ok(new PlayerStateResponse(
        game_id,
        economy.Energy,
        economy.EnergyMax,
        Match3Rules.EnergyRegenSeconds,
        economy.SecondsToNextEnergy,
        economy.Gold,
        economy.UnlockedMap,
        mapId,
        Match3Rules.LevelsPerMap,
        clearedOnMap,
        Match3Rules.MapUnlockClearCount,
        full
    ));
});


/// POST /api/v1/user/level/enter —— 进关扣体力（按配置 EnergyCostPerPlay）
app.MapPost("/api/v1/user/level/enter", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, GameConfigStore cfg, EnterLevelRequest body) =>
{
    if (!Helpers.TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.GameId))
        return Results.Json(new ErrorResponse("game_id required"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
    if (body.MapId < 1)
        return Results.Json(new ErrorResponse("map_id must be >= 1"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
    if (body.LevelId < 1 || body.LevelId > Match3Rules.LevelsPerMap)
        return Results.Json(new ErrorResponse($"level_id must be 1..{Match3Rules.LevelsPerMap}"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);

    if (cfg.Rules.ValidateLevelExists)
    {
        if (!cfg.TryGetLevel(body.MapId, body.LevelId, out var levelRow))
        {
            return Results.Json(new LevelNotInConfigError(
                "level not in config", body.MapId, body.LevelId, cfg.LevelCount),
                AppJsonContext.Default.LevelNotInConfigError,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    var mp = Guid.Parse(claims!.Sub);
    await using var conn = await ds.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        await Helpers.EnsureEconomyAsync(conn, mp, body.GameId);
        var economy = await Helpers.LoadAndRegenEnergyAsync(conn, mp, body.GameId, persist: true);

        if (body.MapId > economy.UnlockedMap)
        {
            await tx.RollbackAsync();
            return Results.Json(new MapLockedError("map locked", economy.UnlockedMap),
                AppJsonContext.Default.MapLockedError,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (body.LevelId > 1)
        {
            await using var prevCmd = new NpgsqlCommand("""
                SELECT stars FROM player_level_progress
                WHERE mp_account_id = @mp AND game_id = @gid AND map_id = @mid AND level_id = @lid
                """, conn);
            prevCmd.Parameters.AddWithValue("mp", mp);
            prevCmd.Parameters.AddWithValue("gid", body.GameId);
            prevCmd.Parameters.AddWithValue("mid", body.MapId);
            prevCmd.Parameters.AddWithValue("lid", body.LevelId - 1);
            var prevStars = await prevCmd.ExecuteScalarAsync();
            if (prevStars is null || (int)prevStars < 1)
            {
                await tx.RollbackAsync();
                return Results.Json(new ErrorResponse("previous level not cleared"),
                    AppJsonContext.Default.ErrorResponse,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var cost = Match3Rules.EnergyCostPerPlay;
        if (economy.Energy < cost)
        {
            await tx.RollbackAsync();
            return Results.Json(new NotEnoughEnergyError(
                "not enough energy", economy.Energy, economy.EnergyMax, economy.SecondsToNextEnergy),
                AppJsonContext.Default.NotEnoughEnergyError,
                statusCode: StatusCodes.Status400BadRequest);
        }

        var newEnergy = economy.Energy - cost;

        await using (var updEco = new NpgsqlCommand("""
            UPDATE player_economy SET
                energy = @en,
                energy_updated_at = NOW(),
                updated_at = NOW()
            WHERE mp_account_id = @mp AND game_id = @gid
            """, conn))
        {
            updEco.Parameters.AddWithValue("en", newEnergy);
            updEco.Parameters.AddWithValue("mp", mp);
            updEco.Parameters.AddWithValue("gid", body.GameId);
            await updEco.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();

        var after = await Helpers.LoadAndRegenEnergyAsync(conn, mp, body.GameId, persist: false);

        return Results.Ok(new EnterLevelResponse(
            body.GameId,
            body.MapId,
            body.LevelId,
            after.Energy,
            after.EnergyMax,
            Match3Rules.EnergyRegenSeconds,
            after.SecondsToNextEnergy,
            cost,
            after.Gold,
            after.UnlockedMap
        ));
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
});

/// POST /api/v1/user/level/clear
app.MapPost("/api/v1/user/level/clear", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, GameConfigStore cfg, ClearLevelRequest body) =>
{
    if (!Helpers.TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.GameId))
        return Results.Json(new ErrorResponse("game_id required"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
    if (body.MapId < 1)
        return Results.Json(new ErrorResponse("map_id must be >= 1"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
    if (body.LevelId < 1 || body.LevelId > Match3Rules.LevelsPerMap)
        return Results.Json(new ErrorResponse($"level_id must be 1..{Match3Rules.LevelsPerMap}"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
    if (body.Stars < 1 || body.Stars > 3)
        return Results.Json(new ErrorResponse("stars must be 1..3 for a clear"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
    if (body.Steps < 0)
        return Results.Json(new ErrorResponse("steps must be >= 0"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);

    // 配置表校验（客户端导表产物）
    if (cfg.Rules.ValidateLevelExists)
    {
        if (!cfg.TryGetLevel(body.MapId, body.LevelId, out var levelRow))
        {
            return Results.Json(new LevelNotInConfigError(
                "level not in config", body.MapId, body.LevelId, cfg.LevelCount),
                AppJsonContext.Default.LevelNotInConfigError,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (cfg.Rules.ValidateStepsAgainstConfig && levelRow.MaxSteps > 0 && body.Steps > levelRow.MaxSteps)
        {
            return Results.Json(new StepsExceedError(
                "steps exceed max_steps", body.Steps, levelRow.MaxSteps),
                AppJsonContext.Default.StepsExceedError,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    var mp = Guid.Parse(claims!.Sub);
    await using var conn = await ds.OpenConnectionAsync();
    await using var tx = await conn.BeginTransactionAsync();

    try
    {
        await Helpers.EnsureEconomyAsync(conn, mp, body.GameId);
        var economy = await Helpers.LoadAndRegenEnergyAsync(conn, mp, body.GameId, persist: true);

        if (body.MapId > economy.UnlockedMap)
        {
            await tx.RollbackAsync();
            return Results.Json(new MapLockedError("map locked", economy.UnlockedMap),
                AppJsonContext.Default.MapLockedError,
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (body.LevelId > 1)
        {
            await using var prevCmd = new NpgsqlCommand("""
                SELECT stars FROM player_level_progress
                WHERE mp_account_id = @mp AND game_id = @gid AND map_id = @mid AND level_id = @lid
                """, conn);
            prevCmd.Parameters.AddWithValue("mp", mp);
            prevCmd.Parameters.AddWithValue("gid", body.GameId);
            prevCmd.Parameters.AddWithValue("mid", body.MapId);
            prevCmd.Parameters.AddWithValue("lid", body.LevelId - 1);
            var prevStars = await prevCmd.ExecuteScalarAsync();
            if (prevStars is null || (int)prevStars < 1)
            {
                await tx.RollbackAsync();
                return Results.Json(new ErrorResponse("previous level not cleared"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        // 通关不再扣体力（进关 POST /level/enter 已扣）
        var newEnergy = economy.Energy;
        var goldGain = Match3Rules.GoldPerStar * body.Stars;
        var newGold = economy.Gold + goldGain;

        await using (var updEco = new NpgsqlCommand("""
            UPDATE player_economy SET
                gold = @gold,
                updated_at = NOW()
            WHERE mp_account_id = @mp AND game_id = @gid
            """, conn))
        {
            updEco.Parameters.AddWithValue("gold", newGold);
            updEco.Parameters.AddWithValue("mp", mp);
            updEco.Parameters.AddWithValue("gid", body.GameId);
            await updEco.ExecuteNonQueryAsync();
        }

        int finalStars;
        int? finalBestSteps;
        int clearCount;
        await using (var upsert = new NpgsqlCommand("""
            INSERT INTO player_level_progress
                (mp_account_id, game_id, map_id, level_id, stars, best_steps, clear_count, last_score, updated_at)
            VALUES (@mp, @gid, @mid, @lid, @stars, @steps, 1, @score, NOW())
            ON CONFLICT (mp_account_id, game_id, map_id, level_id) DO UPDATE SET
                stars = GREATEST(player_level_progress.stars, EXCLUDED.stars),
                best_steps = CASE
                    WHEN player_level_progress.best_steps IS NULL THEN EXCLUDED.best_steps
                    ELSE LEAST(player_level_progress.best_steps, EXCLUDED.best_steps)
                END,
                clear_count = player_level_progress.clear_count + 1,
                last_score = EXCLUDED.last_score,
                updated_at = NOW()
            RETURNING stars, best_steps, clear_count
            """, conn))
        {
            upsert.Parameters.AddWithValue("mp", mp);
            upsert.Parameters.AddWithValue("gid", body.GameId);
            upsert.Parameters.AddWithValue("mid", body.MapId);
            upsert.Parameters.AddWithValue("lid", body.LevelId);
            upsert.Parameters.AddWithValue("stars", body.Stars);
            upsert.Parameters.AddWithValue("steps", body.Steps);
            upsert.Parameters.AddWithValue("score", body.Score);
            await using var ur = await upsert.ExecuteReaderAsync();
            await ur.ReadAsync();
            finalStars = ur.GetInt32(0);
            finalBestSteps = ur.IsDBNull(1) ? null : ur.GetInt32(1);
            clearCount = ur.GetInt32(2);
        }

        int unlockedMap = economy.UnlockedMap;
        await using (var cntCmd = new NpgsqlCommand("""
            SELECT COUNT(*) FROM player_level_progress
            WHERE mp_account_id = @mp AND game_id = @gid AND map_id = @mid AND stars > 0
            """, conn))
        {
            cntCmd.Parameters.AddWithValue("mp", mp);
            cntCmd.Parameters.AddWithValue("gid", body.GameId);
            cntCmd.Parameters.AddWithValue("mid", body.MapId);
            var cleared = Convert.ToInt32(await cntCmd.ExecuteScalarAsync());
            if (cleared >= Match3Rules.MapUnlockClearCount && body.MapId >= unlockedMap)
            {
                unlockedMap = body.MapId + 1;
                await using var unlock = new NpgsqlCommand("""
                    UPDATE player_economy SET unlocked_map = GREATEST(unlocked_map, @um), updated_at = NOW()
                    WHERE mp_account_id = @mp AND game_id = @gid
                    """, conn);
                unlock.Parameters.AddWithValue("um", unlockedMap);
                unlock.Parameters.AddWithValue("mp", mp);
                unlock.Parameters.AddWithValue("gid", body.GameId);
                await unlock.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();

        return Results.Ok(new ClearLevelResponse(
            body.GameId,
            body.MapId,
            body.LevelId,
            finalStars,
            finalBestSteps,
            clearCount,
            newEnergy,
            Match3Rules.EnergyMaxDefault,
            newGold,
            goldGain,
            unlockedMap
        ));
    }
    catch
    {
        await tx.RollbackAsync();
        throw;
    }
});

/// POST /api/v1/user/energy/cheat-refill （开发测试）
app.MapPost("/api/v1/user/energy/cheat-refill", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, CheatRefillRequest body) =>
{
    if (!Helpers.TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(body.GameId))
        return Results.Json(new ErrorResponse("game_id required"), AppJsonContext.Default.ErrorResponse, statusCode: StatusCodes.Status400BadRequest);

    var mp = Guid.Parse(claims!.Sub);
    await using var conn = await ds.OpenConnectionAsync();
    await Helpers.EnsureEconomyAsync(conn, mp, body.GameId);

    await using var cmd = new NpgsqlCommand("""
        UPDATE player_economy SET
            energy = energy_max,
            energy_updated_at = NOW(),
            updated_at = NOW()
        WHERE mp_account_id = @mp AND game_id = @gid
        RETURNING energy, energy_max, gold, unlocked_map
        """, conn);
    cmd.Parameters.AddWithValue("mp", mp);
    cmd.Parameters.AddWithValue("gid", body.GameId);
    await using var r = await cmd.ExecuteReaderAsync();
    await r.ReadAsync();
    return Results.Ok(new CheatRefillResponse(
        r.GetInt32(0),
        r.GetInt32(1),
        r.GetInt64(2),
        r.GetInt32(3)
    ));
});

app.Run("http://0.0.0.0:8080");

// ===================== types & helpers (after top-level statements) =====================

/// <summary>运行期规则：启动时从 GameConfigStore 同步，Helpers 仍可读静态字段。</summary>
static class Match3Rules
{
    public static int EnergyMaxDefault = 30;
    public static int EnergyRegenSeconds = 300;
    public static int LevelsPerMap = 10;
    public static int MapUnlockClearCount = 5;
    public static int EnergyCostPerPlay = 1;
    public static long GoldPerStar = 50;

    public static void Apply(GameRules r)
    {
        // 导表缺失时字段为 0，保留类内默认值
        if (r.EnergyMax > 0) EnergyMaxDefault = r.EnergyMax;
        if (r.EnergyRegenSeconds > 0) EnergyRegenSeconds = r.EnergyRegenSeconds;
        if (r.LevelsPerMap > 0) LevelsPerMap = r.LevelsPerMap;
        if (r.MapUnlockClearCount > 0) MapUnlockClearCount = r.MapUnlockClearCount;
        if (r.EnergyCostPerPlay > 0) EnergyCostPerPlay = r.EnergyCostPerPlay;
        if (r.GoldPerStar > 0) GoldPerStar = r.GoldPerStar;
    }
}

static class Helpers
{
    public static bool TryGetClaims(HttpContext ctx, SimpleJwt jwt, out JwtClaims? claims)
    {
        claims = null;
        var auth = ctx.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        return jwt.TryValidate(auth["Bearer ".Length..].Trim(), out claims) && claims is not null;
    }

    public static ProfileResponse ReadProfile(NpgsqlDataReader r) => new(
        r.GetGuid(0),
        r.GetGuid(1).ToString(),
        r.GetString(2),
        r.GetString(3),
        r.GetInt32(4),
        r.GetString(5),
        r.GetDateTime(6),
        r.GetDateTime(7)
    );

    public static async Task EnsureEconomyAsync(NpgsqlConnection conn, Guid mp, string gameId)
    {
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO player_economy (mp_account_id, game_id, energy, energy_max, gold, energy_updated_at)
            VALUES (@mp, @gid, 30, 30, 0, NOW())
            ON CONFLICT (mp_account_id, game_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.Parameters.AddWithValue("gid", gameId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<EconomySnapshot> LoadAndRegenEnergyAsync(
        NpgsqlConnection conn, Guid mp, string gameId, bool persist)
    {
        await using var cmd = new NpgsqlCommand("""
            SELECT energy, energy_max, gold, energy_updated_at, unlocked_map
            FROM player_economy
            WHERE mp_account_id = @mp AND game_id = @gid
            """, conn);
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.Parameters.AddWithValue("gid", gameId);

        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
            throw new InvalidOperationException("economy row missing");

        var energy = r.GetInt32(0);
        var energyMax = r.GetInt32(1);
        var gold = r.GetInt64(2);
        var updatedAt = r.GetDateTime(3);
        var unlockedMap = r.GetInt32(4);
        await r.CloseAsync();

        var secondsToNext = 0;
        if (energy < energyMax)
        {
            var elapsed = (int)Math.Max(0, (DateTime.UtcNow - updatedAt.ToUniversalTime()).TotalSeconds);
            var gained = elapsed / Match3Rules.EnergyRegenSeconds;
            if (gained > 0)
            {
                var newEnergy = Math.Min(energyMax, energy + gained);
                var consumedSeconds = gained * Match3Rules.EnergyRegenSeconds;
                var newUpdatedAt = updatedAt.ToUniversalTime().AddSeconds(consumedSeconds);
                if (newEnergy >= energyMax)
                    newUpdatedAt = DateTime.UtcNow;

                if (persist && newEnergy != energy)
                {
                    await using var upd = new NpgsqlCommand("""
                        UPDATE player_economy SET energy = @en, energy_updated_at = @uat, updated_at = NOW()
                        WHERE mp_account_id = @mp AND game_id = @gid
                        """, conn);
                    upd.Parameters.AddWithValue("en", newEnergy);
                    upd.Parameters.AddWithValue("uat", newUpdatedAt);
                    upd.Parameters.AddWithValue("mp", mp);
                    upd.Parameters.AddWithValue("gid", gameId);
                    await upd.ExecuteNonQueryAsync();
                }

                energy = newEnergy;
                updatedAt = newUpdatedAt;
            }

            if (energy < energyMax)
            {
                var since = (int)Math.Max(0, (DateTime.UtcNow - updatedAt.ToUniversalTime()).TotalSeconds);
                secondsToNext = Math.Max(1, Match3Rules.EnergyRegenSeconds - (since % Match3Rules.EnergyRegenSeconds));
            }
        }

        return new EconomySnapshot(energy, energyMax, gold, unlockedMap, secondsToNext);
    }
}

sealed record EconomySnapshot(int Energy, int EnergyMax, long Gold, int UnlockedMap, int SecondsToNextEnergy);


public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error);

public sealed record LevelNotInConfigError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("level_id")] int LevelId,
    [property: JsonPropertyName("config_levels")] int ConfigLevels);

public sealed record StepsExceedError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("steps")] int Steps,
    [property: JsonPropertyName("max_steps")] int MaxSteps);

public sealed record MapLockedError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("unlocked_map")] int UnlockedMap);

public sealed record NotEnoughEnergyError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("energy")] int Energy,
    [property: JsonPropertyName("energy_max")] int EnergyMax,
    [property: JsonPropertyName("seconds_to_next")] int SecondsToNext);

public sealed record HealthResponse(string Status, string Service);

public sealed record ProfileResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("mp_account_id")] string MpAccountId,
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("nickname")] string Nickname,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("extra_json")] string ExtraJson,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt);

public sealed record UpdateProfileRequest(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("nickname")] string? Nickname,
    [property: JsonPropertyName("level")] int? Level,
    [property: JsonPropertyName("extra_json")] string? ExtraJson);

public sealed record LevelProgressItem(
    [property: JsonPropertyName("level_id")] int LevelId,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("best_steps")] int? BestSteps,
    [property: JsonPropertyName("clear_count")] int ClearCount,
    [property: JsonPropertyName("last_score")] long LastScore);

public sealed record PlayerStateResponse(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("energy")] int Energy,
    [property: JsonPropertyName("energy_max")] int EnergyMax,
    [property: JsonPropertyName("energy_regen_seconds")] int EnergyRegenSeconds,
    [property: JsonPropertyName("seconds_to_next_energy")] int SecondsToNextEnergy,
    [property: JsonPropertyName("gold")] long Gold,
    [property: JsonPropertyName("unlocked_map")] int UnlockedMap,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("levels_per_map")] int LevelsPerMap,
    [property: JsonPropertyName("cleared_on_map")] int ClearedOnMap,
    [property: JsonPropertyName("map_unlock_clear_count")] int MapUnlockClearCount,
    [property: JsonPropertyName("levels")] List<LevelProgressItem> Levels);

public sealed record EnterLevelRequest(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("level_id")] int LevelId);

public sealed record EnterLevelResponse(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("level_id")] int LevelId,
    [property: JsonPropertyName("energy")] int Energy,
    [property: JsonPropertyName("energy_max")] int EnergyMax,
    [property: JsonPropertyName("energy_regen_seconds")] int EnergyRegenSeconds,
    [property: JsonPropertyName("seconds_to_next")] int SecondsToNext,
    [property: JsonPropertyName("energy_cost")] int EnergyCost,
    [property: JsonPropertyName("gold")] long Gold,
    [property: JsonPropertyName("unlocked_map")] int UnlockedMap);

record ClearLevelRequest(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("level_id")] int LevelId,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("steps")] int Steps,
    [property: JsonPropertyName("score")] long Score);

public sealed record ClearLevelResponse(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("map_id")] int MapId,
    [property: JsonPropertyName("level_id")] int LevelId,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("best_steps")] int? BestSteps,
    [property: JsonPropertyName("clear_count")] int ClearCount,
    [property: JsonPropertyName("energy")] int Energy,
    [property: JsonPropertyName("energy_max")] int EnergyMax,
    [property: JsonPropertyName("gold")] long Gold,
    [property: JsonPropertyName("gold_gained")] long GoldGained,
    [property: JsonPropertyName("unlocked_map")] int UnlockedMap);

public sealed record CheatRefillRequest(
    [property: JsonPropertyName("game_id")] string GameId);

public sealed record CheatRefillResponse(
    [property: JsonPropertyName("energy")] int Energy,
    [property: JsonPropertyName("energy_max")] int EnergyMax,
    [property: JsonPropertyName("gold")] long Gold,
    [property: JsonPropertyName("unlocked_map")] int UnlockedMap);

[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(LevelNotInConfigError))]
[JsonSerializable(typeof(StepsExceedError))]
[JsonSerializable(typeof(MapLockedError))]
[JsonSerializable(typeof(NotEnoughEnergyError))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(UpdateProfileRequest))]
[JsonSerializable(typeof(LevelProgressItem))]
[JsonSerializable(typeof(List<LevelProgressItem>))]
[JsonSerializable(typeof(PlayerStateResponse))]
[JsonSerializable(typeof(EnterLevelRequest))]
[JsonSerializable(typeof(EnterLevelResponse))]
[JsonSerializable(typeof(ClearLevelRequest))]
[JsonSerializable(typeof(ClearLevelResponse))]
[JsonSerializable(typeof(CheatRefillRequest))]
[JsonSerializable(typeof(CheatRefillResponse))]
internal partial class AppJsonContext : JsonSerializerContext;
