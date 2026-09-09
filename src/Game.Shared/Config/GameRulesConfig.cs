using System.Text.Json.Serialization;

namespace Game.Shared.Config;

/// <summary>
/// 玩法/经济规则。可由 config/GameRules.json 覆盖；缺省与历史硬编码一致。
/// </summary>
public sealed class GameRulesConfig
{
    [JsonPropertyName("EnergyMaxDefault")]
    public int EnergyMaxDefault { get; set; } = 30;

    [JsonPropertyName("EnergyRegenSeconds")]
    public int EnergyRegenSeconds { get; set; } = 300;

    [JsonPropertyName("LevelsPerMap")]
    public int LevelsPerMap { get; set; } = 20;

    [JsonPropertyName("MapUnlockClearCount")]
    public int MapUnlockClearCount { get; set; } = 10;

    [JsonPropertyName("EnergyCostPerPlay")]
    public int EnergyCostPerPlay { get; set; } = 1;

    [JsonPropertyName("GoldPerStar")]
    public long GoldPerStar { get; set; } = 50;

    /// <summary>通关时要求 Level 表中存在该 map/level。</summary>
    [JsonPropertyName("ValidateLevelExists")]
    public bool ValidateLevelExists { get; set; } = true;

    /// <summary>通关时校验 steps 不超过 MaxSteps（防作弊模板开关）。</summary>
    [JsonPropertyName("ValidateStepsAgainstConfig")]
    public bool ValidateStepsAgainstConfig { get; set; } = true;
}
