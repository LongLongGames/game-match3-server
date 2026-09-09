using System.Text.Json.Serialization;

namespace Game.Shared.Config;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<LevelRow>))]
[JsonSerializable(typeof(GameRulesConfig))]
public partial class ConfigJsonContext : JsonSerializerContext;
