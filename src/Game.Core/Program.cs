using System.Text.Json.Serialization;
using Game.Shared.Jwt;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("缺少 Jwt__Secret（必须与 MP 一致）");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "battle-net-mp";

builder.Services.AddSingleton(new SimpleJwt(jwtSecret, jwtIssuer));

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", "game-core")));

// 接入示例：任何真实游戏仓库都应实现类似的健康检查 + JWT 验签
app.MapGet("/api/v1/game/status", (HttpContext ctx, SimpleJwt jwt) =>
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var token = auth["Bearer ".Length..].Trim();
    if (!jwt.TryValidate(token, out var claims) || claims is null)
        return Results.Unauthorized();

    return Results.Ok(new GameStatusResponse(
        claims.Sub,
        "game-core",
        "online",
        "Game.Core 验签成功（Game.Core 验签成功（clone 后在此写真实玩法））"
    ));
});

app.Run("http://0.0.0.0:8080");

public sealed record HealthResponse(string Status, string Service);
public sealed record GameStatusResponse(
    [property: JsonPropertyName("mp_account_id")] string MpAccountId,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message);

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(GameStatusResponse))]
internal partial class AppJsonContext : JsonSerializerContext;
