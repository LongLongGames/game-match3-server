using System.Reflection;
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

// 启动时自动迁移
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

var appVersion = typeof(Program).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion
    ?? typeof(Program).Assembly.GetName().Version?.ToString()
    ?? "unknown";

app.MapGet("/health", () => Results.Ok(new HealthResponse("ok", "game-core", appVersion)));

// 版本检查（公开接口，启动时调用，无需 JWT）
// GET /api/v1/game/version-check?game_id=match3&channel=official&platform=android&region=cn&client_version_code=10000&resource_version=2026.08.10.1
app.MapGet("/api/v1/game/version-check", async (
    NpgsqlDataSource ds,
    string game_id,
    string channel,
    string platform,
    string? region,
    int? client_version_code,
    string? resource_version) =>
{
    if (string.IsNullOrWhiteSpace(game_id) ||
        string.IsNullOrWhiteSpace(channel) ||
        string.IsNullOrWhiteSpace(platform))
    {
        return Results.BadRequest(new { error = "game_id, channel, platform are required" });
    }

    var reg = string.IsNullOrWhiteSpace(region) ? "cn" : region.Trim().ToLowerInvariant();

    await using var conn = await ds.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        SELECT
            client_version,
            client_version_code,
            min_client_version_code,
            force_update,
            app_store_url,
            resource_version,
            package_name,
            cdn_main_url,
            cdn_fallback_url,
            resource_manifest_url,
            status,
            gray_rate,
            extra_json
        FROM client_version_config
        WHERE game_id = @gid
          AND channel = @ch
          AND platform = @plat
          AND region = @reg
          AND status IN ('active', 'gray')
        LIMIT 1
        """, conn);

    cmd.Parameters.AddWithValue("gid", game_id);
    cmd.Parameters.AddWithValue("ch", channel);
    cmd.Parameters.AddWithValue("plat", platform);
    cmd.Parameters.AddWithValue("reg", reg);

    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.NotFound(new { error = "no version config for this channel/platform/region" });
    }

    var cfgClientVer = reader.GetString(0);
    var cfgClientCode = reader.GetInt32(1);
    var minCode = reader.GetInt32(2);
    var forceUpdate = reader.GetBoolean(3);
    var appStoreUrl = reader.IsDBNull(4) ? null : reader.GetString(4);
    var cfgResVer = reader.GetString(5);
    var packageName = reader.GetString(6);
    var cdnMain = reader.GetString(7);
    var cdnFallback = reader.IsDBNull(8) ? null : reader.GetString(8);
    var manifestUrl = reader.IsDBNull(9) ? null : reader.GetString(9);
    var status = reader.GetString(10);
    var grayRate = reader.GetInt32(11);
    var extraJson = reader.GetString(12);

    // 灰度：status=gray 时按 gray_rate 决定是否命中；未命中则仍返回配置但可让客户端降级（此处简单返回）
    // 生产可按设备 id / 账号做稳定哈希，当前用随机简化
    var inGray = status != "gray" || grayRate >= 100 || Random.Shared.Next(100) < grayRate;

    var localCode = client_version_code ?? 0;
    var needForceAppUpdate = forceUpdate || localCode < minCode;
    var needResourceUpdate = !string.Equals(resource_version, cfgResVer, StringComparison.Ordinal);

    var resp = new VersionCheckResponse(
        Code: 0,
        ForceUpdateApp: needForceAppUpdate,
        AppStoreUrl: appStoreUrl,
        ClientVersion: cfgClientVer,
        ClientVersionCode: cfgClientCode,
        MinClientVersionCode: minCode,
        Resource: new ResourceInfo(
            ResourceVersion: cfgResVer,
            PackageName: packageName,
            CdnMainUrl: cdnMain,
            CdnFallbackUrl: cdnFallback,
            ManifestUrl: manifestUrl
        ),
        GrayRate: grayRate,
        InGray: inGray,
        Status: status,
        NeedResourceUpdate: needResourceUpdate,
        ServerTime: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ExtraJson: extraJson
    );

    return Results.Ok(resp);
});

// 保留原有 JWT 验签示例
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
        appVersion
    ));
});

app.Run("http://0.0.0.0:8080");

// ---------- records ----------
public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("version")] string Version);

public sealed record GameStatusResponse(
    [property: JsonPropertyName("mp_account_id")] string MpAccountId,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] string Version);

public sealed record ResourceInfo(
    [property: JsonPropertyName("resource_version")] string ResourceVersion,
    [property: JsonPropertyName("package_name")] string PackageName,
    [property: JsonPropertyName("cdn_main_url")] string CdnMainUrl,
    [property: JsonPropertyName("cdn_fallback_url")] string? CdnFallbackUrl,
    [property: JsonPropertyName("manifest_url")] string? ManifestUrl);

public sealed record VersionCheckResponse(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("force_update_app")] bool ForceUpdateApp,
    [property: JsonPropertyName("app_store_url")] string? AppStoreUrl,
    [property: JsonPropertyName("client_version")] string ClientVersion,
    [property: JsonPropertyName("client_version_code")] int ClientVersionCode,
    [property: JsonPropertyName("min_client_version_code")] int MinClientVersionCode,
    [property: JsonPropertyName("resource")] ResourceInfo Resource,
    [property: JsonPropertyName("gray_rate")] int GrayRate,
    [property: JsonPropertyName("in_gray")] bool InGray,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("need_resource_update")] bool NeedResourceUpdate,
    [property: JsonPropertyName("server_time")] long ServerTime,
    [property: JsonPropertyName("extra_json")] string ExtraJson);

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(GameStatusResponse))]
[JsonSerializable(typeof(ResourceInfo))]
[JsonSerializable(typeof(VersionCheckResponse))]
internal partial class AppJsonContext : JsonSerializerContext;
