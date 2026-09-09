using System.Text.Json;

namespace Game.Shared.Config;

/// <summary>
/// 启动时加载 config 目录（客户端导表产物 + GameRules）。
/// 运行期只读内存字典，不重复解析文件。
/// </summary>
public sealed class GameConfigStore
{
    public GameRulesConfig Rules { get; }
    public IReadOnlyDictionary<(int MapId, int LevelId), LevelRow> Levels { get; }
    public string ConfigRoot { get; }
    public int LevelCount => Levels.Count;

    GameConfigStore(
        string configRoot,
        GameRulesConfig rules,
        Dictionary<(int, int), LevelRow> levels)
    {
        ConfigRoot = configRoot;
        Rules = rules;
        Levels = levels;
    }

    public bool TryGetLevel(int mapId, int levelId, out LevelRow? row)
        => Levels.TryGetValue((mapId, levelId), out row);

    /// <summary>
    /// 从指定根目录加载。root 通常由 Program 解析（Config:Root / 默认 ./config）。
    /// </summary>
    public static GameConfigStore Load(string configRoot)
    {
        if (string.IsNullOrWhiteSpace(configRoot))
            throw new ArgumentException("configRoot required", nameof(configRoot));

        configRoot = Path.GetFullPath(configRoot);
        Directory.CreateDirectory(configRoot);

        var rulesPath = Path.Combine(configRoot, "GameRules.json");
        var levelPath = Path.Combine(configRoot, "Level.json");

        var rules = new GameRulesConfig();
        if (File.Exists(rulesPath))
        {
            var json = File.ReadAllText(rulesPath);
            rules = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.GameRulesConfig)
                    ?? new GameRulesConfig();
            Console.WriteLine($"[GameConfig] loaded GameRules.json from {rulesPath}");
        }
        else
        {
            Console.WriteLine($"[GameConfig] GameRules.json missing at {rulesPath}, using defaults");
        }

        var levels = new Dictionary<(int, int), LevelRow>();
        if (File.Exists(levelPath))
        {
            var json = File.ReadAllText(levelPath);
            var list = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ListLevelRow)
                       ?? new List<LevelRow>();
            foreach (var row in list)
            {
                if (row.MapId < 1 || row.LevelId < 1) continue;
                levels[(row.MapId, row.LevelId)] = row;
            }
            Console.WriteLine($"[GameConfig] loaded Level.json count={levels.Count} from {levelPath}");
        }
        else
        {
            Console.WriteLine(
                $"[GameConfig] Level.json missing at {levelPath}. Run client Tools/导表 export first.");
        }

        return new GameConfigStore(configRoot, rules, levels);
    }

    /// <summary>
    /// 解析配置根目录。
    /// </summary>
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
                (File.Exists(Path.Combine(c, "Level.json")) || File.Exists(Path.Combine(c, "GameRules.json"))))
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
