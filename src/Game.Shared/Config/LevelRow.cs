using System.Text.Json.Serialization;

namespace Game.Shared.Config;

/// <summary>
/// 与客户端 BakingSheet 导出的 Level.json 行结构对齐（字段名 PascalCase）。
/// </summary>
public sealed class LevelRow
{
    [JsonPropertyName("Id")]
    public string? Id { get; set; }

    [JsonPropertyName("MapId")]
    public int MapId { get; set; }

    [JsonPropertyName("LevelId")]
    public int LevelId { get; set; }

    [JsonPropertyName("MaxSteps")]
    public int MaxSteps { get; set; }

    [JsonPropertyName("StepsFor3Stars")]
    public int StepsFor3Stars { get; set; }

    [JsonPropertyName("StepsFor2Stars")]
    public int StepsFor2Stars { get; set; }

    [JsonPropertyName("BoardWidth")]
    public int BoardWidth { get; set; } = 8;

    [JsonPropertyName("BoardHeight")]
    public int BoardHeight { get; set; } = 8;

    [JsonPropertyName("Goal")]
    public string? Goal { get; set; }

    [JsonPropertyName("GoalValue")]
    public int GoalValue { get; set; }
}
