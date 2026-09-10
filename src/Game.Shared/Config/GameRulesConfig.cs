using System.Text.Json.Serialization;

namespace Game.Shared.Config;

/// <summary>
/// 服务端运营规则（手写 JSON，非 ECC 导表）。
/// </summary>
public sealed class GameRulesConfig
{
    [JsonPropertyName("EnergyMaxDefault")]
    public int EnergyMaxDefault { get; set; } = 30;

    [JsonPropertyName("EnergyRegenSeconds")]
    public int EnergyRegenSeconds { get; set; } = 300;

    [JsonPropertyName("LevelsPerMap")]
    public int LevelsPerMap { get; set; } = 10;

    [JsonPropertyName("MapUnlockClearCount")]
    public int MapUnlockClearCount { get; set; } = 5;

    [JsonPropertyName("EnergyCostPerPlay")]
    public int EnergyCostPerPlay { get; set; } = 1;

    [JsonPropertyName("GoldPerStar")]
    public int GoldPerStar { get; set; } = 50;

    [JsonPropertyName("ValidateLevelExists")]
    public bool ValidateLevelExists { get; set; } = true;

    [JsonPropertyName("ValidateStepsAgainstConfig")]
    public bool ValidateStepsAgainstConfig { get; set; } = true;
}
