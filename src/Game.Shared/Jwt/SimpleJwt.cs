using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.Shared.Jwt;

/// <summary>
/// 与 MP 完全一致的极简 HS256 JWT 实现。
/// 游戏后端只需同一 Secret 即可本地验签，无需回调 MP。
/// </summary>
public sealed record JwtClaims(
    [property: JsonPropertyName("iss")] string Iss,
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("app_id")] string? AppId,
    [property: JsonPropertyName("iat")] long Iat,
    [property: JsonPropertyName("exp")] long Exp);

[JsonSerializable(typeof(JwtClaims))]
internal partial class JwtJsonContext : JsonSerializerContext
{
}

public sealed class SimpleJwt(string secret, string issuer)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(secret);
    private readonly string _issuer = issuer;

    public bool TryValidate(string token, out JwtClaims? claims)
    {
        claims = null;
        var parts = token.Split('.');
        if (parts.Length != 3) return false;

        var signingInput = $"{parts[0]}.{parts[1]}";
        var expectedSig = Base64UrlEncode(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(signingInput)));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSig), Encoding.UTF8.GetBytes(parts[2])))
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize(Base64UrlDecode(parts[1]), JwtJsonContext.Default.JwtClaims);
            if (parsed is null) return false;
            if (parsed.Exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            // 可选：严格校验 Issuer
            if (!string.IsNullOrEmpty(_issuer) && parsed.Iss != _issuer) return false;
            claims = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
