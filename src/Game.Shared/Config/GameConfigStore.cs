using System.Text.Json;

namespace Game.Shared.Config;

/// <summary>
/// 启动时加载 config 目录：
/// - Level.bytes / Item.bytes / CheckInReward.bytes：ExcelConfigCompiler 产物（优先）
/// - Level.json：兼容旧客户端 BakingSheet 导出
/// - GameRules.json：服务端规则（非导表）
/// </summary>
public sealed class GameConfigStore
{
    public GameRulesConfig Rules { get; }
    public IReadOnlyDictionary<(int MapId, int LevelId), LevelRow> Levels { get; }
    public IReadOnlyDictionary<int, ItemRow> Items { get; }
    public IReadOnlyDictionary<int, CheckInRewardRow> CheckInByDay { get; }
    public string ConfigRoot { get; }
    public int LevelCount => Levels.Count;

    GameConfigStore(
        string configRoot,
        GameRulesConfig rules,
        Dictionary<(int, int), LevelRow> levels,
        Dictionary<int, ItemRow> items,
        Dictionary<int, CheckInRewardRow> checkIn)
    {
        ConfigRoot = configRoot;
        Rules = rules;
        Levels = levels;
        Items = items;
        CheckInByDay = checkIn;
    }

    public bool TryGetLevel(int mapId, int levelId, out LevelRow? row)
        => Levels.TryGetValue((mapId, levelId), out row);

    public bool TryGetItem(int id, out ItemRow? row)
        => Items.TryGetValue(id, out row);

    public bool TryGetCheckInDay(int day, out CheckInRewardRow? row)
        => CheckInByDay.TryGetValue(day, out row);

    public static GameConfigStore Load(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot))
            throw new ArgumentException("configRoot required", nameof(configRoot));

        configRoot = Path.GetFullPath(configRoot);
        Directory.CreateDirectory(configRoot);

        var rules = LoadRules(configRoot);
        var levels = LoadLevels(configRoot);
        var items = LoadItems(configRoot);
        var checkIn = LoadCheckIn(configRoot);

        return new GameConfigStore(configRoot, rules, levels, items, checkIn);
    }

    static GameRulesConfig LoadRules(string configRoot)
    {
        var rulesPath = Path.Combine(configRoot, "GameRules.json");
        if (!File.Exists(rulesPath))
        {
            Console.WriteLine($"[GameConfig] GameRules.json missing at {rulesPath}, using defaults");
            return new GameRulesConfig();
        }

        var json = File.ReadAllText(rulesPath);
        var rules = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.GameRulesConfig)
                    ?? new GameRulesConfig();
        Console.WriteLine($"[GameConfig] loaded GameRules.json LevelsPerMap={rules.LevelsPerMap}");
        return rules;
    }

    static Dictionary<(int, int), LevelRow> LoadLevels(string configRoot)
    {
        var bytesPath = Path.Combine(configRoot, "Level.bytes");
        var jsonPath = Path.Combine(configRoot, "Level.json");
        var levels = new Dictionary<(int, int), LevelRow>();

        if (File.Exists(bytesPath))
        {
            var data = File.ReadAllBytes(bytesPath);
            foreach (var row in ReadLevelBytes(data))
            {
                if (row.MapId < 1 || row.LevelId < 1) continue;
                levels[(row.MapId, row.LevelId)] = row;
            }
            Console.WriteLine($"[GameConfig] loaded Level.bytes count={levels.Count} from {bytesPath}");
            return levels;
        }

        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath);
            var list = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ListLevelRow)
                       ?? new List<LevelRow>();
            foreach (var row in list)
            {
                if (row.MapId < 1 || row.LevelId < 1) continue;
                levels[(row.MapId, row.LevelId)] = row;
            }
            Console.WriteLine($"[GameConfig] loaded Level.json count={levels.Count} (legacy) from {jsonPath}");
            return levels;
        }

        Console.WriteLine(
            $"[GameConfig] Level.bytes / Level.json missing under {configRoot}. Export via ExcelConfigCompiler first.");
        return levels;
    }

    static Dictionary<int, ItemRow> LoadItems(string configRoot)
    {
        var path = Path.Combine(configRoot, "Item.bytes");
        var map = new Dictionary<int, ItemRow>();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[GameConfig] Item.bytes missing (optional)");
            return map;
        }

        var data = File.ReadAllBytes(path);
        ExcelConfigBinary.ValidateHeader(data, out _, out int count, out int offset);
        for (int i = 0; i < count; i++)
        {
            var row = new ItemRow
            {
                Id = ExcelConfigBinary.ReadInt32(data, ref offset),
                Name = ExcelConfigBinary.ReadString(data, ref offset),
                Effect = ExcelConfigBinary.ReadString(data, ref offset),
                Param = ExcelConfigBinary.ReadInt32(data, ref offset),
                Icon = ExcelConfigBinary.ReadString(data, ref offset),
                Desc = ExcelConfigBinary.ReadString(data, ref offset),
            };
            if (row.Id > 0) map[row.Id] = row;
        }
        Console.WriteLine($"[GameConfig] loaded Item.bytes count={map.Count}");
        return map;
    }

    static Dictionary<int, CheckInRewardRow> LoadCheckIn(string configRoot)
    {
        var path = Path.Combine(configRoot, "CheckInReward.bytes");
        var map = new Dictionary<int, CheckInRewardRow>();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[GameConfig] CheckInReward.bytes missing (optional)");
            return map;
        }

        var data = File.ReadAllBytes(path);
        ExcelConfigBinary.ValidateHeader(data, out _, out int count, out int offset);
        for (int i = 0; i < count; i++)
        {
            var row = new CheckInRewardRow
            {
                Id = ExcelConfigBinary.ReadInt32(data, ref offset),
                Day = ExcelConfigBinary.ReadInt32(data, ref offset),
                Energy = ExcelConfigBinary.ReadInt32(data, ref offset),
                ItemId1 = ExcelConfigBinary.ReadInt32(data, ref offset),
                ItemCount1 = ExcelConfigBinary.ReadInt32(data, ref offset),
                ItemId2 = ExcelConfigBinary.ReadInt32(data, ref offset),
                ItemCount2 = ExcelConfigBinary.ReadInt32(data, ref offset),
                ItemId3 = ExcelConfigBinary.ReadInt32(data, ref offset),
                ItemCount3 = ExcelConfigBinary.ReadInt32(data, ref offset),
                Desc = ExcelConfigBinary.ReadString(data, ref offset),
            };
            if (row.Day > 0) map[row.Day] = row;
        }
        Console.WriteLine($"[GameConfig] loaded CheckInReward.bytes days={map.Count}");
        return map;
    }

    /// <summary>Level 字段顺序与客户端 Generated/Level.cs 一致。</summary>
    public static List<LevelRow> ReadLevelBytes(ReadOnlySpan<byte> data)
    {
        ExcelConfigBinary.ValidateHeader(data, out _, out int count, out int offset);
        var list = new List<LevelRow>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(new LevelRow
            {
                Id = ExcelConfigBinary.ReadInt32(data, ref offset),
                MapId = ExcelConfigBinary.ReadInt32(data, ref offset),
                LevelId = ExcelConfigBinary.ReadInt32(data, ref offset),
                MaxSteps = ExcelConfigBinary.ReadInt32(data, ref offset),
                StepsFor3Stars = ExcelConfigBinary.ReadInt32(data, ref offset),
                StepsFor2Stars = ExcelConfigBinary.ReadInt32(data, ref offset),
                BoardWidth = ExcelConfigBinary.ReadInt32(data, ref offset),
                BoardHeight = ExcelConfigBinary.ReadInt32(data, ref offset),
                Goal = ExcelConfigBinary.ReadString(data, ref offset),
                GoalValue = ExcelConfigBinary.ReadInt32(data, ref offset),
            });
        }
        return list;
    }

    public static string ResolveConfigRoot(string? fromConfiguration)
    {
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
            return Path.GetFullPath(fromConfiguration);

        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "config"),
            Path.Combine(AppContext.BaseDirectory, "config"),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "config")),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "config")),
        };

        foreach (var c in candidates)
        {
            if (Directory.Exists(c) &&
                (File.Exists(Path.Combine(c, "Level.bytes")) ||
                 File.Exists(Path.Combine(c, "Level.json")) ||
                 File.Exists(Path.Combine(c, "GameRules.json"))))
                return Path.GetFullPath(c);
        }

        foreach (var c in candidates)
        {
            if (Directory.Exists(c))
                return Path.GetFullPath(c);
        }

        var fallback = Path.Combine(Directory.GetCurrentDirectory(), "config");
        Directory.CreateDirectory(fallback);
        return Path.GetFullPath(fallback);
    }
}
