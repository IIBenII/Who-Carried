using System.Text.Json;

namespace WhoCarried.Core;

public static class RunStatsStore
{
    public static void Save(RunStats stats, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(stats, WhoCarriedJson.Default.RunStats));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The saved stats whatever their state, or null if missing or unreadable.</summary>
    public static RunStats? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), WhoCarriedJson.Default.RunStats) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The saved stats if they belong to <paramref name="runKey"/> (seed:start time) and that run hasn't finished;
    /// otherwise null. When the game is loading a saved run, the seed alone is enough: a co-op guest's new run is
    /// stamped with the guest's own clock, but the save it later loads carries the host's, so the start time moves.
    /// Resumed stats take on <paramref name="runKey"/>.
    /// </summary>
    public static RunStats? LoadIfResumable(string path, string runKey, bool loadedFromSave = false)
    {
        try
        {
            if (!File.Exists(path)) return null;
            RunStats? stats = JsonSerializer.Deserialize(File.ReadAllText(path), WhoCarriedJson.Default.RunStats);
            if (stats == null || stats.Finished) return null;
            bool sameRun = stats.RunKey == runKey || (loadedFromSave && Seed(stats.RunKey) == Seed(runKey));
            if (!sameRun) return null;
            stats.RunKey = runKey;
            return stats;
        }
        catch (Exception)
        {
            return null; // corrupt or from an older format: start fresh
        }
    }

    private static string Seed(string runKey)
    {
        int colon = runKey.LastIndexOf(':');
        return colon < 0 ? runKey : runKey[..colon];
    }
}
