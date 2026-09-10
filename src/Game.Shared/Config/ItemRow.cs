namespace Game.Shared.Config;

/// <summary>道具表行（Item.bytes）。</summary>
public sealed class ItemRow
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Effect { get; set; }
    public int Param { get; set; }
    public string? Icon { get; set; }
    public string? Desc { get; set; }
}
