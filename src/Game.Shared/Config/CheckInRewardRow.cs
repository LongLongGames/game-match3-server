namespace Game.Shared.Config;

/// <summary>签到奖励行（CheckInReward.bytes）。</summary>
public sealed class CheckInRewardRow
{
    public int Id { get; set; }
    public int Day { get; set; }
    public int Energy { get; set; }
    public int ItemId1 { get; set; }
    public int ItemCount1 { get; set; }
    public int ItemId2 { get; set; }
    public int ItemCount2 { get; set; }
    public int ItemId3 { get; set; }
    public int ItemCount3 { get; set; }
    public string? Desc { get; set; }
}
