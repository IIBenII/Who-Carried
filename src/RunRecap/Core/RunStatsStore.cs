using System.Text.Json;

namespace RunRecap.Core;

public static class RunStatsStore
{
    public static void Save(RunStats stats, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(stats, RunRecapJson.Default.RunStats));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The saved stats whatever their state, or null if missing or unreadable.</summary>
    public static RunStats? Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), RunRecapJson.Default.RunStats) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The saved stats if they belong to <paramref name="runKey"/> and that run hasn't finished; otherwise null.</summary>
    public static RunStats? LoadIfResumable(string path, string runKey)
    {
        try
        {
            if (!File.Exists(path)) return null;
            RunStats? stats = JsonSerializer.Deserialize(File.ReadAllText(path), RunRecapJson.Default.RunStats);
            return stats != null && stats.RunKey == runKey && !stats.Finished ? stats : null;
        }
        catch (Exception)
        {
            return null; // corrupt or from an older format: start fresh
        }
    }
}
