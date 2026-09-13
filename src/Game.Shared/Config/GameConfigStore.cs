namespace Game.Shared.Config;

/// <summary>
/// 启动时加载 config 目录（ExcelConfigCompiler 导出的 .bytes）：
/// - Level.bytes / Item.bytes / CheckInReward.bytes / GameRules.bytes
/// 解析一律走导表生成的 Level / Item / CheckInReward / GameRules 及对应 Table。
/// </summary>
public sealed class GameConfigStore
{
    public GameRules Rules { get; }
    public IReadOnlyDictionary<(int MapId, int LevelId), Level> Levels { get; }
    public IReadOnlyDictionary<int, Item> Items { get; }
    public IReadOnlyDictionary<int, CheckInReward> CheckInByDay { get; }
    public string ConfigRoot { get; }
    public int LevelCount => Levels.Count;

    GameConfigStore(
        string configRoot,
        GameRules rules,
        Dictionary<(int, int), Level> levels,
        Dictionary<int, Item> items,
        Dictionary<int, CheckInReward> checkIn)
    {
        ConfigRoot = configRoot;
        Rules = rules;
        Levels = levels;
        Items = items;
        CheckInByDay = checkIn;
    }

    public bool TryGetLevel(int mapId, int levelId, out Level level)
        => Levels.TryGetValue((mapId, levelId), out level);

    public bool TryGetItem(int id, out Item item)
        => Items.TryGetValue(id, out item);

    public bool TryGetCheckInDay(int day, out CheckInReward row)
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

    static GameRules LoadRules(string configRoot)
    {
        var bytesPath = Path.Combine(configRoot, "GameRules.bytes");
        if (!File.Exists(bytesPath))
        {
            Console.WriteLine($"[GameConfig] GameRules.bytes missing at {bytesPath}, using defaults");
            return default; // 全 0；Match3Rules.Apply 需对 0 做兜底
        }

        var data = File.ReadAllBytes(bytesPath);
        var rows = GameRulesTable.LoadAndCache(data);
        if (rows.Length == 0)
        {
            Console.WriteLine($"[GameConfig] GameRules.bytes empty, using defaults");
            return default;
        }

        // 优先 Id=1，否则取第一行
        GameRules rules = rows[0];
        if (GameRulesTable.TryGet(1, out var byId))
            rules = byId;

        Console.WriteLine(
            $"[GameConfig] loaded GameRules.bytes Id={rules.Id} LevelsPerMap={rules.LevelsPerMap} EnergyMax={rules.EnergyMax}");
        return rules;
    }

    static Dictionary<(int, int), Level> LoadLevels(string configRoot)
    {
        var bytesPath = Path.Combine(configRoot, "Level.bytes");
        var levels = new Dictionary<(int, int), Level>();

        if (!File.Exists(bytesPath))
        {
            Console.WriteLine(
                $"[GameConfig] Level.bytes missing under {configRoot}. Export via ExcelConfigCompiler first.");
            return levels;
        }

        var data = File.ReadAllBytes(bytesPath);
        var rows = LevelTable.LoadAndCache(data);
        foreach (var row in rows)
        {
            if (row.MapId < 1 || row.LevelId < 1) continue;
            levels[(row.MapId, row.LevelId)] = row;
        }

        Console.WriteLine($"[GameConfig] loaded Level.bytes count={levels.Count} from {bytesPath}");
        return levels;
    }

    static Dictionary<int, Item> LoadItems(string configRoot)
    {
        var path = Path.Combine(configRoot, "Item.bytes");
        var map = new Dictionary<int, Item>();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[GameConfig] Item.bytes missing (optional)");
            return map;
        }

        var data = File.ReadAllBytes(path);
        var rows = ItemTable.LoadAndCache(data);
        foreach (var row in rows)
        {
            if (row.Id > 0) map[row.Id] = row;
        }

        Console.WriteLine($"[GameConfig] loaded Item.bytes count={map.Count}");
        return map;
    }

    static Dictionary<int, CheckInReward> LoadCheckIn(string configRoot)
    {
        var path = Path.Combine(configRoot, "CheckInReward.bytes");
        var map = new Dictionary<int, CheckInReward>();
        if (!File.Exists(path))
        {
            Console.WriteLine($"[GameConfig] CheckInReward.bytes missing (optional)");
            return map;
        }

        var data = File.ReadAllBytes(path);
        var rows = CheckInRewardTable.LoadAndCache(data);
        foreach (var row in rows)
        {
            if (row.Day > 0) map[row.Day] = row;
        }

        Console.WriteLine($"[GameConfig] loaded CheckInReward.bytes days={map.Count}");
        return map;
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
                 File.Exists(Path.Combine(c, "GameRules.bytes")) ||
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
