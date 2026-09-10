using System.Text.Json.Serialization;

namespace Game.Shared.Config;

/// <summary>
/// 与客户端 ExcelConfigCompiler Level 表对齐。
/// 优先从 Level.bytes 加载；兼容旧 Level.json（Id 可能为字符串）。
/// </summary>
public sealed class LevelRow
{
    /// <summary>表主键（ECC：int；旧 JSON 可能是 "1_1" 字符串，反序列化时忽略即可）。</summary>
    [JsonPropertyName("Id")]
    public int Id { get; set; }

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
