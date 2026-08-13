using System.Text.Json.Serialization;
using DbUp;
using Game.Shared.Jwt;
using Npgsql;

var builder = WebApplication.CreateSlimBuilder(args);

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

var app = builder.Build();

{
    var upgrader = DeployChanges.To
        .PostgresqlDatabase(connStr)
        .WithScriptsEmbeddedInAssembly(typeof(Program).Assembly)
        .WithTransaction()
        .LogToConsole()
        .Build();
    var result = upgrader.PerformUpgrade();
    if (!result.Successful)
        throw new Exception("DB migration failed: " + result.Error);
}

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", "game-leaderboard")));

// 提交 / 更新分数（同一 game+board 取最高分）
app.MapPost("/api/v1/leaderboard/score", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, SubmitScoreRequest body) =>
{
    if (!TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(body.GameId))
        return Results.BadRequest(new { error = "game_id required" });

    var boardId = string.IsNullOrWhiteSpace(body.BoardId) ? "default" : body.BoardId;

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        INSERT INTO leaderboard_scores (mp_account_id, game_id, board_id, score, nickname, extra_json, updated_at)
        VALUES (@mp, @gid, @bid, @score, @nick, @extra::jsonb, NOW())
        ON CONFLICT (mp_account_id, game_id, board_id) DO UPDATE SET
            score = GREATEST(leaderboard_scores.score, EXCLUDED.score),
            nickname = COALESCE(NULLIF(EXCLUDED.nickname, ''), leaderboard_scores.nickname),
            extra_json = COALESCE(EXCLUDED.extra_json, leaderboard_scores.extra_json),
            updated_at = NOW()
        RETURNING id, mp_account_id, game_id, board_id, score, nickname, extra_json, updated_at
        """, conn);
    cmd.Parameters.AddWithValue("mp", Guid.Parse(claims!.Sub));
    cmd.Parameters.AddWithValue("gid", body.GameId);
    cmd.Parameters.AddWithValue("bid", boardId);
    cmd.Parameters.AddWithValue("score", body.Score);
    cmd.Parameters.AddWithValue("nick", body.Nickname ?? "");
    cmd.Parameters.AddWithValue("extra", body.ExtraJson ?? "{}");

    await using var reader = await cmd.ExecuteReaderAsync();
    await reader.ReadAsync();
    return Results.Ok(ReadScore(reader));
});

// 查询排行榜（按分数降序）
app.MapGet("/api/v1/leaderboard/top", async (NpgsqlDataSource ds, string game_id, string? board_id, int limit = 50) =>
{
    if (string.IsNullOrWhiteSpace(game_id))
        return Results.BadRequest(new { error = "game_id required" });

    var bid = string.IsNullOrWhiteSpace(board_id) ? "default" : board_id;
    if (limit < 1) limit = 1;
    if (limit > 100) limit = 100;

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        SELECT id, mp_account_id, game_id, board_id, score, nickname, extra_json, updated_at
        FROM leaderboard_scores
        WHERE game_id = @gid AND board_id = @bid
        ORDER BY score DESC, updated_at ASC
        LIMIT @lim
        """, conn);
    cmd.Parameters.AddWithValue("gid", game_id);
    cmd.Parameters.AddWithValue("bid", bid);
    cmd.Parameters.AddWithValue("lim", limit);

    var list = new List<ScoreResponse>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        list.Add(ReadScore(reader));

    return Results.Ok(new TopResponse(game_id, bid, list));
});

// 查询自己的排名与分数
app.MapGet("/api/v1/leaderboard/me", async (HttpContext ctx, SimpleJwt jwt, NpgsqlDataSource ds, string game_id, string? board_id) =>
{
    if (!TryGetClaims(ctx, jwt, out var claims))
        return Results.Unauthorized();

    var bid = string.IsNullOrWhiteSpace(board_id) ? "default" : board_id;

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        WITH ranked AS (
            SELECT mp_account_id, score, nickname,
                   RANK() OVER (ORDER BY score DESC, updated_at ASC) AS rank
            FROM leaderboard_scores
            WHERE game_id = @gid AND board_id = @bid
        )
        SELECT rank, score, nickname FROM ranked WHERE mp_account_id = @mp
        """, conn);
    cmd.Parameters.AddWithValue("gid", game_id);
    cmd.Parameters.AddWithValue("bid", bid);
    cmd.Parameters.AddWithValue("mp", Guid.Parse(claims!.Sub));

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Ok(new MyRankResponse(null, 0, null));

    return Results.Ok(new MyRankResponse(
        reader.GetInt64(0),
        reader.GetInt64(1),
        reader.IsDBNull(2) ? null : reader.GetString(2)
    ));
});

app.Run("http://0.0.0.0:8080");

static bool TryGetClaims(HttpContext ctx, SimpleJwt jwt, out JwtClaims? claims)
{
    claims = null;
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
    return jwt.TryValidate(auth["Bearer ".Length..].Trim(), out claims) && claims is not null;
}

static ScoreResponse ReadScore(NpgsqlDataReader r) => new(
    r.GetGuid(0),
    r.GetGuid(1).ToString(),
    r.GetString(2),
    r.GetString(3),
    r.GetInt64(4),
    r.GetString(5),
    r.GetString(6),
    r.GetDateTime(7)
);

public sealed record HealthResponse(string Status, string Service);
public sealed record SubmitScoreRequest(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("board_id")] string? BoardId,
    [property: JsonPropertyName("score")] long Score,
    [property: JsonPropertyName("nickname")] string? Nickname,
    [property: JsonPropertyName("extra_json")] string? ExtraJson);
public sealed record ScoreResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("mp_account_id")] string MpAccountId,
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("board_id")] string BoardId,
    [property: JsonPropertyName("score")] long Score,
    [property: JsonPropertyName("nickname")] string Nickname,
    [property: JsonPropertyName("extra_json")] string ExtraJson,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt);
public sealed record TopResponse(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("board_id")] string BoardId,
    [property: JsonPropertyName("items")] List<ScoreResponse> Items);
public sealed record MyRankResponse(
    [property: JsonPropertyName("rank")] long? Rank,
    [property: JsonPropertyName("score")] long Score,
    [property: JsonPropertyName("nickname")] string? Nickname);

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(SubmitScoreRequest))]
[JsonSerializable(typeof(ScoreResponse))]
[JsonSerializable(typeof(TopResponse))]
[JsonSerializable(typeof(MyRankResponse))]
[JsonSerializable(typeof(List<ScoreResponse>))]
internal partial class AppJsonContext : JsonSerializerContext;
